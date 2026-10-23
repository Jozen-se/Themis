using Microsoft.Extensions.Logging;
using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// The database backstops of the code review (Remediation Register, Code review 2026-10-18): a company's fiscal
/// periods never overlap (RR-44), stock on hand never goes below 0 (RR-47, I-03), a journal entry is rejected at commit
/// when it has fewer than two lines or does not sum to 0 (RR-47, I-01), the trigger function runs as its owner and no
/// role holds EXECUTE on any kernel or reporting function (I-14), and a commit the triggers refuse is handled by the
/// pipeline: a 500, the SQLSTATE in the log, no idempotency record, the key free for the retry. The backstops stand
/// behind code that checks first; here they are exercised directly, as the table owner and as the kernel role.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class DatabaseBackstopTests : IAsyncLifetime
{
    private const string CheckViolation = "23514";
    private const string ExclusionViolation = "23P01";
    private const string June = "2026-06-15";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;
    private static readonly Guid Inventory = Themis.Kernel.Posting.Milestone1GeneralLedgerFixture.InventoryAccountId;
    private static readonly Guid Adjustment = Themis.Kernel.Posting.Milestone1GeneralLedgerFixture.AdjustmentAccountId;
    private static readonly string SystemUser = WellKnownUsers.System.Value.ToString();
    private static readonly string Audit = $"now(), '{SystemUser}', now(), '{SystemUser}'";

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public DatabaseBackstopTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync()
    {
        _host.PostingFailures.AfterLedgerWrites = null;
        await _db.TruncateAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    // ---- RR-44 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_44_a_fiscal_period_that_overlaps_another_of_the_same_company_is_refused_by_the_exclusion_constraint_and_an_adjacent_one_is_not()
    {
        await using NpgsqlConnection connection = await Migrator();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        string insert = "INSERT INTO kernel.fiscal_period (id, fiscal_year_id, company_id, period_number, start_date, end_date, status, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{0}', '{1}', {2}, '{3}', '{4}', 'Open', {5})";

        // Period 13 of 2026 starting on the last day of period 12 overlaps by one day; starting the day after is adjacent (the year's own range is not the constraint's concern).
        await AssertRejectedAsync(connection, ExclusionViolation, "ex_fiscal_period_no_overlap", string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, Milestone1OrganisationFixture.FiscalYearId, Company, 13, "2026-12-31", "2027-01-31", Audit));
        await ExecuteAsync(connection, string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, Milestone1OrganisationFixture.FiscalYearId, Company, 13, "2027-01-01", "2027-01-31", Audit));
        await transaction.RollbackAsync(Ct);
    }

    // ---- RR-47 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_47_the_database_refuses_stock_on_hand_below_0_in_every_warehouse_I_03()
    {
        await using NpgsqlConnection connection = await Migrator();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        Guid item = await ScaffoldItemAsync(connection, "RR47-STOCK");
        await ExecuteAsync(connection, $"INSERT INTO kernel.stock_balance (id, company_id, item_id, warehouse_id, stock_status, on_hand_base, reserved_base, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Company}', '{item}', '{Warehouse}', 'Available', 1, 0, {Audit})");

        await AssertRejectedAsync(connection, CheckViolation, "ck_stock_balance_on_hand", $"UPDATE kernel.stock_balance SET on_hand_base = -1 WHERE item_id = '{item}'");
        await AssertRejectedAsync(connection, CheckViolation, "ck_stock_balance_on_hand", $"INSERT INTO kernel.stock_balance (id, company_id, item_id, warehouse_id, stock_status, on_hand_base, reserved_base, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Company}', '{item}', '{Warehouse}', 'Blocked', -0.000001, 0, {Audit})");
        await ExecuteAsync(connection, $"UPDATE kernel.stock_balance SET on_hand_base = 0 WHERE item_id = '{item}'");
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task RR_47_a_journal_entry_with_fewer_than_two_lines_or_out_of_balance_is_rejected_at_commit_and_a_balanced_one_commits_I_01()
    {
        Guid document = Guid.NewGuid();
        await _db.ExecuteAsMigratorAsync($"INSERT INTO kernel.document (id, company_id, document_type, status, draft_reference, document_date, posting_date, currency, created_at, created_by, updated_at, updated_by) VALUES ('{document}', '{Company}', 'INVENTORY_ADJUSTMENT', 'Posted', '{document}', '{June}', '{June}', 'SEK', {Audit})", Ct);

        // As the kernel role, the way the posting service writes (the function runs as its owner, SECURITY DEFINER).
        Guid single = Guid.NewGuid();
        PostgresException fewerThanTwo = await Assert.ThrowsAsync<PostgresException>(() => AsKernelAsync(JournalEntrySql(single, document, "JE2026-000001") + JournalLineSql(single, 1, Inventory, "INVENTORY", "5")));
        Assert.Equal((CheckViolation, "ct_journal_entry_balanced"), (fewerThanTwo.SqlState, fewerThanTwo.ConstraintName));
        Assert.Contains("fewer than 2 lines", fewerThanTwo.MessageText, StringComparison.Ordinal);

        Guid unbalanced = Guid.NewGuid();
        PostgresException notZero = await Assert.ThrowsAsync<PostgresException>(() => AsKernelAsync(JournalEntrySql(unbalanced, document, "JE2026-000002") + JournalLineSql(unbalanced, 1, Inventory, "INVENTORY", "5") + JournalLineSql(unbalanced, 2, Adjustment, "INVENTORY_ADJUSTMENT", "-4.99")));
        Assert.Equal((CheckViolation, "ct_journal_entry_balanced"), (notZero.SqlState, notZero.ConstraintName));
        Assert.Contains("does not sum to 0", notZero.MessageText, StringComparison.Ordinal);

        Guid balanced = Guid.NewGuid();
        await AsKernelAsync(JournalEntrySql(balanced, document, "JE2026-000003") + JournalLineSql(balanced, 1, Inventory, "INVENTORY", "5") + JournalLineSql(balanced, 2, Adjustment, "INVENTORY_ADJUSTMENT", "-5"));
        Assert.Equal(1L, await _host.CountAsync("kernel.journal_entry", Ct));

        // A line added later to the committed entry re-sums it at its own commit.
        PostgresException later = await Assert.ThrowsAsync<PostgresException>(() => AsKernelAsync(JournalLineSql(balanced, 3, Inventory, "INVENTORY", "0.01")));
        Assert.Equal("ct_journal_entry_balanced", later.ConstraintName);
        Assert.Equal(2L, await _host.CountAsync("kernel.journal_line", Ct));
    }

    [Fact]
    public async Task RR_47_D_60_every_kernel_and_reporting_function_runs_as_themis_migrator_with_a_fixed_search_path_and_no_role_holds_EXECUTE_on_any_I_14()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using NpgsqlCommand functions = new(
            """
            SELECT n.nspname || '.' || p.proname, pg_get_userbyid(p.proowner), p.prosecdef, array_to_string(p.proconfig, ';'), coalesce(p.proacl::text, 'default'), p.oid::regprocedure::text
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname IN ('kernel', 'reporting')
              AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
            ORDER BY 1
            """, connection);
        List<(string Name, string Owner, bool SecurityDefiner, string? Config, string Acl, string Signature)> rows = [];
        await using (NpgsqlDataReader reader = await functions.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            }
        }

        // The RR-47 balance check and the D-60 immutability trigger function, and nothing else outside extensions.
        Assert.Equal(["kernel.journal_entry_balance_check", "kernel.ledger_row_immutable"], rows.Select(row => row.Name));
        foreach ((string name, string owner, bool securityDefiner, string? config, string acl, string signature) in rows)
        {
            Assert.Equal((name, "themis_migrator", true, "search_path=pg_catalog, kernel"), (name, owner, securityDefiner, config));
            // An empty access list: not the default (owner all, PUBLIC execute), nobody. A trigger fires without EXECUTE.
            Assert.Equal((name, "{}"), (name, acl));
            foreach (string role in new[] { "public", "themis_app", "themis_kernel", "themis_reporting", "themis_fixture", "themis_migrator" })
            {
                await using NpgsqlCommand privilege = new("SELECT has_function_privilege(@role, @function, 'EXECUTE')", connection);
                privilege.Parameters.AddWithValue("role", role);
                privilege.Parameters.AddWithValue("function", signature);
                Assert.False((bool)(await privilege.ExecuteScalarAsync(Ct))!, $"{role} holds EXECUTE on {signature}.");
            }
        }
    }

    [Fact]
    public async Task RR_47_a_planted_unbalanced_line_fails_the_posting_at_commit_with_a_500_the_SQLSTATE_in_the_log_no_idempotency_record_and_a_clean_retry()
    {
        Guid item = await CreateItemAsync("RR47-COMMIT");
        CommandReply created = await _host.PostAsync("/v1/kernel/inventory-adjustments", new { companyId = Company, documentDate = June, reasonCode = "CountDifference", warehouseId = Warehouse, postingDate = June, lines = new[] { new { lineNumber = 1, itemId = item, quantity = "10", stockStatus = "Available", enteredUnitCost = "2.00" } } }, "rr47-create", cancellationToken: Ct);
        Assert.True(created.Status == 200, created.Body);
        Guid document = created.Json!["id"]!.GetValue<Guid>();

        // The seam writes, as the kernel role on the command's own unit of work, a third line that breaks the entry; the deferred trigger refuses the commit.
        _host.PostingFailures.AfterLedgerWrites = async (unitOfWork, documentId, cancellationToken) =>
        {
            await unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
                $"INSERT INTO kernel.journal_line (id, journal_entry_id, company_id, line_number, account_id, account_role, amount, txn_currency, txn_amount) SELECT gen_random_uuid(), e.id, e.company_id, 3, '{Inventory}', 'INVENTORY', 0.01, 'SEK', 0.01 FROM kernel.journal_entry e WHERE e.source_document_id = @document",
                new Dictionary<string, object?> { ["document"] = documentId }, cancellationToken);
        };
        CommandReply failed = await _host.PostAsync($"/v1/kernel/inventory-adjustments/{document}/post", null, "rr47-post", ifMatch: "\"1\"", cancellationToken: Ct);
        _host.PostingFailures.AfterLedgerWrites = null;

        Assert.Equal(500, failed.Status);
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Error && line.Message.Contains("23514", StringComparison.Ordinal) && line.Message.Contains("does not sum to 0", StringComparison.Ordinal));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'rr47-post'", Ct));
        Assert.Equal(0L, await _host.CountAsync("kernel.journal_entry", Ct));
        Assert.Equal("Draft", await _host.ScalarAsync("SELECT status FROM kernel.document WHERE id = @id", Ct, ("id", document)));

        // The same key runs afresh once the plant is gone: the unit of work was marked completed when the commit threw, nothing lingered.
        CommandReply retry = await _host.PostAsync($"/v1/kernel/inventory-adjustments/{document}/post", null, "rr47-post", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(retry.Status == 200, retry.Body);
        Assert.False(retry.Replayed);
        Assert.Equal(1L, await _host.CountAsync("kernel.journal_entry", Ct));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------

    private async Task<Guid> CreateItemAsync(string number)
    {
        CommandReply reply = await _host.PostAsync("/v1/kernel/items", new { companyId = Company, itemNumber = number, description = $"Item {number}", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "item-" + number, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private static async Task<Guid> ScaffoldItemAsync(NpgsqlConnection connection, string number)
    {
        Guid item = Guid.NewGuid();
        await ExecuteAsync(connection, $"INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, created_at, created_by, updated_at, updated_by) VALUES ('{item}', '{number}', 'Backstop probe', 'Stocked', 'H87', 'None', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.item_company (id, item_id, company_id, status, item_posting_group, tax_class, costing_method, sales_allowed, purchase_allowed, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{item}', '{Company}', 'Active', 'TRADE_GOODS', 'Standard', 'MovingAverage', true, true, {Audit})");
        return item;
    }

    private static string JournalEntrySql(Guid id, Guid document, string number) =>
        $"INSERT INTO kernel.journal_entry (id, company_id, entry_number, fiscal_period_id, posting_date, document_date, source_document_id, command_id, description, recorded_at, recorded_by) VALUES ('{id}', '{Company}', '{number}', '{Milestone1OrganisationFixture.PeriodId(6)}', '{June}', '{June}', '{document}', gen_random_uuid(), 'Backstop probe', now(), '{SystemUser}'); ";

    private static string JournalLineSql(Guid entry, int lineNumber, Guid account, string role, string amount) =>
        $"INSERT INTO kernel.journal_line (id, journal_entry_id, company_id, line_number, account_id, account_role, amount, txn_currency, txn_amount) VALUES (gen_random_uuid(), '{entry}', '{Company}', {lineNumber}, '{account}', '{role}', {amount}, 'SEK', {amount}); ";

    /// <summary>Runs the statements as themis_kernel in one transaction and commits, the way a command's unit of work does; the commit raises the trigger's error.</summary>
    private async Task AsKernelAsync(string sql)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel; " + sql);
        await transaction.CommitAsync(Ct);
    }

    private async Task<NpgsqlConnection> Migrator() => await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task AssertRejectedAsync(NpgsqlConnection connection, string sqlState, string constraint, string sql)
    {
        await ExecuteAsync(connection, "SAVEPOINT probe");
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal((sqlState, constraint), (error.SqlState, error.ConstraintName));
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT probe");
    }
}
