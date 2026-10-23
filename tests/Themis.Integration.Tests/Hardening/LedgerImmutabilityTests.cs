using Npgsql;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// The ledger immutability backstop (D-60, RR-63; I-14, R-LED-06): the table owner, themis_migrator, cannot UPDATE,
/// DELETE or TRUNCATE journal_entry, journal_line, inventory_entry, audit_event or setting_value; each refusal is
/// SQLSTATE 23514 with the guarded table's constraint name ct_&lt;table&gt;_immutable. The triggers on the five tables
/// are exactly the D-60 ones and the RR-47 balance checks, all enabled in origin mode, and no role but a superuser may
/// set session_replication_role, which would silence them. Every row is planted in a transaction that rolls back.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class LedgerImmutabilityTests
{
    private const string CheckViolation = "23514";
    private const string June = "2026-06-15";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;
    private static readonly Guid Inventory = Themis.Kernel.Posting.Milestone1GeneralLedgerFixture.InventoryAccountId;
    private static readonly Guid Adjustment = Themis.Kernel.Posting.Milestone1GeneralLedgerFixture.AdjustmentAccountId;
    private static readonly string SystemUser = WellKnownUsers.System.Value.ToString();
    private static readonly string Audit = $"now(), '{SystemUser}', now(), '{SystemUser}'";

    private static readonly string[] Guarded = ["journal_entry", "journal_line", "inventory_entry", "audit_event", "setting_value"];

    private readonly PostgresFixture _db;

    public LedgerImmutabilityTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task I_14_D_60_the_table_owner_cannot_update_or_delete_a_planted_row_of_any_guarded_table()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        Planted planted = await PlantAsync(connection);

        (string Table, string Update, string Delete)[] cases =
        [
            ("journal_entry", $"UPDATE kernel.journal_entry SET description = 'edited' WHERE id = '{planted.Entry}'", $"DELETE FROM kernel.journal_entry WHERE id = '{planted.Entry}'"),
            ("journal_line", $"UPDATE kernel.journal_line SET amount = amount WHERE journal_entry_id = '{planted.Entry}'", $"DELETE FROM kernel.journal_line WHERE journal_entry_id = '{planted.Entry}'"),
            ("inventory_entry", $"UPDATE kernel.inventory_entry SET cost_amount = cost_amount WHERE id = '{planted.InventoryEntry}'", $"DELETE FROM kernel.inventory_entry WHERE id = '{planted.InventoryEntry}'"),
            ("audit_event", $"UPDATE kernel.audit_event SET changes = '{{\"edited\": true}}' WHERE id = '{planted.AuditEvent}'", $"DELETE FROM kernel.audit_event WHERE id = '{planted.AuditEvent}'"),
            ("setting_value", $"UPDATE kernel.setting_value SET value = '8' WHERE id = '{planted.SettingValue}'", $"DELETE FROM kernel.setting_value WHERE id = '{planted.SettingValue}'"),
        ];
        Assert.Equal(Guarded, cases.Select(c => c.Table));
        foreach ((string table, string update, string delete) in cases)
        {
            await AssertImmutableAsync(connection, table, update, "UPDATE");
            await AssertImmutableAsync(connection, table, delete, "DELETE");
        }

        await transaction.RollbackAsync(Ct);
    }

    [Theory]
    [InlineData("journal_line", "TRUNCATE kernel.journal_line")]
    [InlineData("audit_event", "TRUNCATE kernel.audit_event")]
    [InlineData("setting_value", "TRUNCATE kernel.setting_value")]
    [InlineData("journal_entry", "TRUNCATE kernel.journal_entry CASCADE")]
    [InlineData("inventory_entry", "TRUNCATE kernel.inventory_entry CASCADE")]
    public async Task I_14_D_60_the_table_owner_cannot_truncate_a_guarded_table_and_the_refusal_names_that_table(string table, string truncate)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        await PlantAsync(connection);

        await AssertImmutableAsync(connection, table, truncate, "TRUNCATE");

        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task D_60_the_five_tables_carry_exactly_the_D_60_triggers_and_the_RR_47_balance_checks_all_enabled()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using NpgsqlCommand triggers = new(
            """
            SELECT c.relname || '.' || t.tgname, t.tgenabled::text
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'kernel' AND c.relname = ANY(@tables) AND NOT t.tgisinternal
            ORDER BY 1
            """, connection);
        triggers.Parameters.AddWithValue("tables", Guarded);
        List<(string Trigger, string Enabled)> rows = [];
        await using (NpgsqlDataReader reader = await triggers.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        string[] expected =
        [
            .. Guarded.SelectMany(table => new[] { $"{table}.tr_{table}_immutable", $"{table}.tr_{table}_no_truncate" }),
            "journal_entry.ct_journal_entry_balanced",
            "journal_line.ct_journal_line_balanced",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), rows.Select(row => row.Trigger).Order(StringComparer.Ordinal));
        // 'O': fires in origin and local mode; a trigger disabled ('D') or limited to replica ('R') or always ('A') is not D-60.
        Assert.All(rows, row => Assert.Equal((row.Trigger, "O"), (row.Trigger, row.Enabled)));
    }

    [Fact]
    public async Task D_60_no_role_holds_SET_on_session_replication_role()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using (NpgsqlCommand granted = new("SELECT count(*) FROM pg_parameter_acl WHERE parname = 'session_replication_role'", connection))
        {
            Assert.Equal(0L, await granted.ExecuteScalarAsync(Ct));
        }

        // The roles of db/bootstrap/01-roles.sql and the fixture bootstrap; the pg_parameter_acl count above covers every other role.
        string[] themis = ["themis_app", "themis_fixture", "themis_kernel", "themis_migrator", "themis_reporting"];
        await using (NpgsqlCommand roles = new("SELECT count(*) FROM pg_roles WHERE rolname = ANY(@roles) AND NOT rolsuper", connection))
        {
            roles.Parameters.AddWithValue("roles", themis);
            Assert.Equal((long)themis.Length, await roles.ExecuteScalarAsync(Ct));
        }

        foreach (string role in themis)
        {
            await using NpgsqlCommand privilege = new("SELECT has_parameter_privilege(@role, 'session_replication_role', 'SET')", connection);
            privilege.Parameters.AddWithValue("role", role);
            Assert.False((bool)(await privilege.ExecuteScalarAsync(Ct))!, $"{role} may set session_replication_role.");
        }

        // The table owner is refused the setting itself, so it cannot silence the D-60 triggers that way.
        await using NpgsqlConnection migrator = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlCommand replica = new("SET session_replication_role = replica", migrator);
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => replica.ExecuteNonQueryAsync(Ct));
        Assert.Equal("42501", refused.SqlState);
    }

    private sealed record Planted(Guid Entry, Guid InventoryEntry, Guid AuditEvent, Guid SettingValue);

    /// <summary>One row in each guarded table, with the parents they need, written as the table owner; the caller rolls back.</summary>
    private static async Task<Planted> PlantAsync(NpgsqlConnection connection)
    {
        Guid item = Guid.NewGuid();
        Guid document = Guid.NewGuid();
        Guid line = Guid.NewGuid();
        Planted planted = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        string number = $"D60-{item.ToString("N")[..8].ToUpperInvariant()}";
        Guid june = Milestone1OrganisationFixture.PeriodId(6);

        await ExecuteAsync(connection, $"INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, created_at, created_by, updated_at, updated_by) VALUES ('{item}', '{number}', 'Planted item', 'Stocked', 'H87', 'None', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.item_company (id, item_id, company_id, status, item_posting_group, tax_class, costing_method, sales_allowed, purchase_allowed, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{item}', '{Company}', 'Active', 'TRADE_GOODS', 'Standard', 'MovingAverage', true, true, {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.document (id, company_id, document_type, status, document_number, draft_reference, document_date, posting_date, currency, warehouse_id, posted_at, posted_by, created_at, created_by, updated_at, updated_by) VALUES ('{document}', '{Company}', 'INVENTORY_ADJUSTMENT', 'Posted', '{number}', '{document}', '{June}', '{June}', 'SEK', '{Warehouse}', now(), '{SystemUser}', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.document_line (id, company_id, document_id, line_number, line_type, item_id, description, quantity, unit_code, quantity_base, warehouse_id, stock_status, line_status, created_at, created_by, updated_at, updated_by) VALUES ('{line}', '{Company}', '{document}', 1, 'Item', '{item}', 'Planted line', 1, 'H87', 1, '{Warehouse}', 'Available', 'Open', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.journal_entry (id, company_id, entry_number, fiscal_period_id, posting_date, document_date, source_document_id, command_id, description, recorded_at, recorded_by) VALUES ('{planted.Entry}', '{Company}', '{number}', '{june}', '{June}', '{June}', '{document}', gen_random_uuid(), 'Planted entry', now(), '{SystemUser}')");
        await ExecuteAsync(connection, $"INSERT INTO kernel.journal_line (id, journal_entry_id, company_id, line_number, account_id, account_role, amount, txn_currency, txn_amount) VALUES (gen_random_uuid(), '{planted.Entry}', '{Company}', 1, '{Inventory}', 'INVENTORY', 10, 'SEK', 10), (gen_random_uuid(), '{planted.Entry}', '{Company}', 2, '{Adjustment}', 'INVENTORY_ADJUSTMENT', -10, 'SEK', -10)");
        await ExecuteAsync(connection, $"INSERT INTO kernel.inventory_entry (id, company_id, item_id, warehouse_id, stock_status, movement_type, quantity_base, cost_amount, unit_cost, posting_date, document_date, source_line_id, journal_entry_id, command_id, recorded_at, recorded_by) VALUES ('{planted.InventoryEntry}', '{Company}', '{item}', '{Warehouse}', 'Available', 'AdjustmentIn', 1, 10, 10, '{June}', '{June}', '{line}', '{planted.Entry}', gen_random_uuid(), now(), '{SystemUser}')");
        await ExecuteAsync(connection, $"INSERT INTO kernel.audit_event (id, command_id, occurred_at, user_id, command_name, entity_type, entity_id, changes) VALUES ('{planted.AuditEvent}', gen_random_uuid(), now(), '{SystemUser}', 'D60.PLANT', 'Planted', gen_random_uuid(), '{{}}')");
        await ExecuteAsync(connection, $"INSERT INTO kernel.setting_value (id, definition_key, level, kind, value, version, recorded_at, recorded_by, command_id) VALUES ('{planted.SettingValue}', 'idempotency.retention_days', 'Installation', 'Set', '7', 900, now(), '{SystemUser}', gen_random_uuid())");
        // The RR-47 balance checks run now, so no deferred trigger event is pending: TRUNCATE refuses a table with one (55006).
        await ExecuteAsync(connection, "SET CONSTRAINTS ALL IMMEDIATE");
        return planted;
    }

    /// <summary>Runs the statement in a savepoint and expects the D-60 trigger of the table to refuse it; the transaction stays usable.</summary>
    private static async Task AssertImmutableAsync(NpgsqlConnection connection, string table, string sql, string operation)
    {
        await ExecuteAsync(connection, "SAVEPOINT probe");
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal((table, operation, CheckViolation, $"ct_{table}_immutable"), (table, operation, error.SqlState, error.ConstraintName));
        Assert.Contains($"kernel.{table}", error.MessageText, StringComparison.Ordinal);
        Assert.Contains("D-60", error.MessageText, StringComparison.Ordinal);
        Assert.Contains(operation, error.MessageText, StringComparison.Ordinal);
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT probe");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
