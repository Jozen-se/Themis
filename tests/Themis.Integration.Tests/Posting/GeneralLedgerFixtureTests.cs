using Npgsql;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence.Privileges;
using Themis.Kernel.Posting;

namespace Themis.Integration.Tests.Posting;

/// <summary>
/// Milestone 1 step 6 (brief, Tests): the general ledger fixture exists after Themis.Migrator ran with the fixture
/// flag with one audit event per row and no outbox event, a second run inserts nothing, and the database enforces
/// the constraints generated from the account role catalogue (the role list, R-PP-06, R-PP-04 through the composite
/// foreign key), the account number unique per chart (R-MD-05), no overlap with empty keys as a value (R-MD-02), and
/// no deletion (R-MD-03). The drift test proves the database agrees with the catalogue role by role.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class GeneralLedgerFixtureTests
{
    private const string InsufficientPrivilege = "42501";
    private const string ForeignKeyViolation = "23503";
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string ExclusionViolation = "23P01";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Chart = Milestone1OrganisationFixture.ChartOfAccountsId;
    private static readonly string System = WellKnownUsers.System.Value.ToString();

    private readonly PostgresFixture _db;

    public GeneralLedgerFixtureTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Audit => $"now(), '{System}', now(), '{System}'";

    [Fact]
    public async Task The_fixture_holds_1460_and_TEST_ADJ_in_the_companys_chart_and_the_two_rules_with_an_audit_event_each_and_no_outbox_event()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        const string seedAudit = "FROM kernel.audit_event WHERE command_name = 'SYSTEM.SEED_FIXTURE' AND entity_type IN ('Account', 'PostingProfileRule')";

        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.account"));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.account WHERE chart_id = @chart", ("chart", Chart)));
        Assert.Equal("Lager av handelsvaror", await ScalarAsync(connection, "SELECT name FROM kernel.account WHERE account_number = '1460'"));
        Assert.Equal("Asset", await ScalarAsync(connection, "SELECT account_type FROM kernel.account WHERE account_number = '1460'"));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT is_control FROM kernel.account WHERE account_number = '1460'"));
        Assert.Equal("Test placeholder, inventory adjustment (D-20 open)", await ScalarAsync(connection, "SELECT name FROM kernel.account WHERE account_number = 'TEST-ADJ'"));
        Assert.Equal("Expense", await ScalarAsync(connection, "SELECT account_type FROM kernel.account WHERE account_number = 'TEST-ADJ'"));
        Assert.Equal(false, await ScalarAsync(connection, "SELECT is_control FROM kernel.account WHERE account_number = 'TEST-ADJ'"));
        Assert.Equal("Active,Active", await ScalarAsync(connection, "SELECT string_agg(status, ',') FROM kernel.account"));

        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.posting_profile_rule WHERE company_id = @company", ("company", Company)));
        Assert.Equal(Milestone1GeneralLedgerFixture.InventoryAccountId, await ScalarAsync(connection, "SELECT account_id FROM kernel.posting_profile_rule WHERE account_role = 'INVENTORY' AND item_posting_group = 'TRADE_GOODS' AND reason_code IS NULL"));
        Assert.Equal(Milestone1GeneralLedgerFixture.AdjustmentAccountId, await ScalarAsync(connection, "SELECT account_id FROM kernel.posting_profile_rule WHERE account_role = 'INVENTORY_ADJUSTMENT' AND item_posting_group = 'TRADE_GOODS' AND reason_code = 'CountDifference'"));
        Assert.Equal(new DateOnly(2026, 1, 1), await ScalarAsync(connection, "SELECT DISTINCT valid_from FROM kernel.posting_profile_rule"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.posting_profile_rule WHERE valid_to IS NOT NULL"));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT expects_control FROM kernel.posting_profile_rule WHERE account_role = 'INVENTORY'"));
        Assert.Equal(false, await ScalarAsync(connection, "SELECT expects_control FROM kernel.posting_profile_rule WHERE account_role = 'INVENTORY_ADJUSTMENT'"));

        // R-AUD-01: one audit event per seeded row under the system user; no outbox event, because no command ran.
        Assert.Equal(4L, await ScalarAsync(connection, $"SELECT count(*) {seedAudit}"));
        Assert.Equal(4L, await ScalarAsync(connection, $"SELECT count(DISTINCT entity_id) {seedAudit}"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync(connection, $"SELECT DISTINCT user_id {seedAudit}"));
        Assert.Equal("1460", await ScalarAsync(connection, $"SELECT changes->'accountNumber'->>'after' {seedAudit} AND entity_id = @id", ("id", Milestone1GeneralLedgerFixture.InventoryAccountId)));
        Assert.Equal("CountDifference", await ScalarAsync(connection, $"SELECT changes->'reasonCode'->>'after' {seedAudit} AND entity_id = @id", ("id", Milestone1GeneralLedgerFixture.InventoryAdjustmentRuleId)));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.outbox_event WHERE aggregate_id IN (SELECT id FROM kernel.account UNION SELECT id FROM kernel.posting_profile_rule)"));

        Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", _db.ConnectionStringFor("themis_migrator"), "--fixture", "milestone-1"]));

        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.account"));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.posting_profile_rule"));
        Assert.Equal(4L, await ScalarAsync(connection, $"SELECT count(*) {seedAudit}"));
    }

    [Fact]
    public async Task R_PP_04_and_R_PP_06_and_the_role_list_are_refused_at_insert_and_a_referenced_accounts_control_flag_cannot_change()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        Guid control = Milestone1GeneralLedgerFixture.InventoryAccountId;
        Guid nonControl = Milestone1GeneralLedgerFixture.AdjustmentAccountId;

        // R-PP-04 in both directions, through the composite foreign key on (account_id, expects_control). The probes run
        // in 2020, before the fixture's open-ended rules, so the no-overlap constraint stays out of the way.
        await AssertFailsAsync(connection, ForeignKeyViolation, Rule("INVENTORY", nonControl, group: "TRADE_GOODS", from: "2020-01-01", to: "2021-01-01"));
        await AssertFailsAsync(connection, ForeignKeyViolation, Rule("INVENTORY_ADJUSTMENT", control, group: "TRADE_GOODS", reason: "Scrap"));
        // R-PP-06: a key the role does not use.
        await AssertFailsAsync(connection, CheckViolation, Rule("INVENTORY", control, group: "TRADE_GOODS", reason: "Scrap", from: "2020-01-01", to: "2021-01-01"));
        await AssertFailsAsync(connection, CheckViolation, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "Scrap", party: "EU"));
        // The role list, the enum values, the validity order and the item posting group reference.
        await AssertFailsAsync(connection, CheckViolation, Rule("NOT_A_ROLE", nonControl));
        await AssertFailsAsync(connection, CheckViolation, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "Bogus"));
        await AssertFailsAsync(connection, CheckViolation, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "Scrap", from: "2026-01-01", to: "2026-01-01"));
        await AssertFailsAsync(connection, ForeignKeyViolation, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "NOPE", reason: "Scrap"));
        // A rule that follows every constraint is accepted.
        await ExecuteAsync(connection, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "Scrap"));

        // R-PP-04: the referenced account cannot flip its control flag; the alternate key (id, is_control) is what the rule points at.
        await AssertFailsAsync(connection, ForeignKeyViolation, $"UPDATE kernel.account SET is_control = false WHERE id = '{control}'");
        // R-MD-05: the number is unique per chart; the same number in another chart is fine.
        await AssertFailsAsync(connection, UniqueViolation, $"INSERT INTO kernel.account (id, chart_id, account_number, name, account_type, is_control, status, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Chart}', '1460', 'Again', 'Asset', true, 'Active', {Audit})");
        await AssertFailsAsync(connection, CheckViolation, $"INSERT INTO kernel.account (id, chart_id, account_number, name, account_type, is_control, status, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Chart}', '9999', 'Bogus type', 'Bogus', true, 'Active', {Audit})");
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task R_MD_02_rules_with_the_same_role_and_keys_cannot_overlap_in_time_also_when_both_leave_a_key_empty_and_adjacent_ranges_are_accepted()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        Guid nonControl = Milestone1GeneralLedgerFixture.AdjustmentAccountId;

        // The fixture's CountDifference rule runs from 2026-01-01 open-ended: the same keys anywhere in that range overlap.
        await AssertFailsAsync(connection, ExclusionViolation, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "CountDifference", from: "2026-06-01"));
        await AssertFailsAsync(connection, ExclusionViolation, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "CountDifference", from: "2025-01-01", to: "2026-01-02"));
        // Different keys do not overlap: a "any reason" rule beside the CountDifference rule.
        await ExecuteAsync(connection, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS"));
        // Two rules that both leave the reason empty overlap: an empty key counts as a value (Specification, Effective dating).
        await AssertFailsAsync(connection, ExclusionViolation, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", from: "2026-03-01"));
        // Adjacent ranges touch without overlapping: valid_to is exclusive.
        await ExecuteAsync(connection, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "Scrap", from: "2026-01-01", to: "2026-07-01"));
        await ExecuteAsync(connection, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "Scrap", from: "2026-07-01"));
        await AssertFailsAsync(connection, ExclusionViolation, Rule("INVENTORY_ADJUSTMENT", nonControl, group: "TRADE_GOODS", reason: "Scrap", from: "2026-06-30", to: "2026-07-02"));
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task The_database_agrees_with_the_account_role_catalogue_role_by_role()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        Guid control = Milestone1GeneralLedgerFixture.InventoryAccountId;
        Guid nonControl = Milestone1GeneralLedgerFixture.AdjustmentAccountId;
        Guid taxCode = Guid.NewGuid();

        // Per role: a rule with exactly the role's keys and the right kind of account is accepted; the other kind of
        // account is refused (R-PP-04); each key the role does not use is refused when set (R-PP-06). Together with the
        // refusal of a code outside the list (previous test) this proves the three generated constraints role by role.
        // Closed one-year ranges before 2026, so no probe overlaps the fixture's open-ended rules or an earlier probe.
        int year = 2000;
        foreach (AccountRoleDefinition role in AccountRoles.All)
        {
            Guid right = role.ExpectsControlAccount ? control : nonControl;
            Guid wrong = role.ExpectsControlAccount ? nonControl : control;
            string? group = role.Uses(MappingKeys.ItemPostingGroup) ? "TRADE_GOODS" : null;
            string? party = role.Uses(MappingKeys.PartyPostingGroup) ? "EU" : null;
            string? scope = role.Uses(MappingKeys.ShipToScope) ? "EU" : null;
            Guid? tax = role.Uses(MappingKeys.TaxCode) ? taxCode : null;
            string? reason = role.Uses(MappingKeys.ReasonCode) ? "Other" : null;

            await ExecuteAsync(connection, Rule(role.Code, right, group, party, scope, tax, reason, from: $"{year}-01-01", to: $"{++year}-01-01"));
            await AssertFailsAsync(connection, ForeignKeyViolation, Rule(role.Code, wrong, group, party, scope, tax, reason, from: $"{year}-01-01", to: $"{++year}-01-01"));
            foreach ((MappingKeys key, string _) in AccountRoleConstraints.KeyColumns)
            {
                if (role.Uses(key))
                {
                    continue;
                }

                string extra = Rule(role.Code, right,
                    key == MappingKeys.ItemPostingGroup ? "TRADE_GOODS" : group,
                    key == MappingKeys.PartyPostingGroup ? "EU" : party,
                    key == MappingKeys.ShipToScope ? "EU" : scope,
                    key == MappingKeys.TaxCode ? taxCode : tax,
                    key == MappingKeys.ReasonCode ? "Other" : reason,
                    from: $"{year}-01-01", to: $"{++year}-01-01");
                await AssertFailsAsync(connection, CheckViolation, extra);
            }
        }

        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task R_MD_03_the_kernel_role_updates_accounts_and_rules_but_never_deletes_them()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "UPDATE kernel.account SET name = 'Renamed', version = version + 1 WHERE account_number = 'TEST-ADJ'");
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT version FROM kernel.account WHERE account_number = 'TEST-ADJ'"));
        await AssertFailsAsync(connection, InsufficientPrivilege, "DELETE FROM kernel.account");
        await AssertFailsAsync(connection, InsufficientPrivilege, "DELETE FROM kernel.posting_profile_rule");
        await transaction.RollbackAsync(Ct);
    }

    /// <summary>An insert into posting_profile_rule as themis_migrator, every key given or left NULL.</summary>
    private static string Rule(string role, Guid account, string? group = null, string? party = null, string? scope = null, Guid? taxCode = null, string? reason = null, string from = "2026-01-01", string? to = null)
    {
        static string Text(string? value) => value is null ? "NULL" : $"'{value}'";
        return "INSERT INTO kernel.posting_profile_rule (id, company_id, account_role, item_posting_group, party_posting_group, ship_to_scope, tax_code_id, reason_code, account_id, valid_from, valid_to, created_at, created_by, updated_at, updated_by) " +
            $"VALUES (gen_random_uuid(), '{Company}', '{role}', {Text(group)}, {Text(party)}, {Text(scope)}, {(taxCode is null ? "NULL" : $"'{taxCode}'")}, {Text(reason)}, '{account}', '{from}', {Text(to)}, {Audit})";
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        object? result = await command.ExecuteScalarAsync(Ct);
        return result is DBNull ? null : result;
    }

    /// <summary>Asserts the SQLSTATE inside a savepoint so the transaction stays usable.</summary>
    private static async Task AssertFailsAsync(NpgsqlConnection connection, string sqlState, string sql)
    {
        await ExecuteAsync(connection, "SAVEPOINT probe");
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal(sqlState, exception.SqlState);
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT probe");
    }
}
