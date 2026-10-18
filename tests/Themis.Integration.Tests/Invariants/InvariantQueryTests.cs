using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Invariants;

/// <summary>
/// The invariant queries of db/invariants are complete and detect what they claim (step 11 brief): every invariant
/// of the Specification has a query, is held by the privilege tests (I-11, I-14) or is listed with its phase; every
/// query is empty on the seeded database; where a constraint makes a violation impossible the constraint is shown to
/// reject the row (I-02, the entry part of I-08, the zero-value part of I-04, duplicates of I-09), and for every other
/// part the table owner plants a violating row inside a rolled-back transaction and the query must report it. The
/// constraints are never dropped.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed partial class InvariantQueryTests : IAsyncLifetime
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string June = "2026-06-15";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;
    private static readonly Guid JunePeriod = Milestone1OrganisationFixture.PeriodId(6);
    private static readonly Guid Inventory = Themis.Kernel.Posting.Milestone1GeneralLedgerFixture.InventoryAccountId;
    private static readonly Guid Adjustment = Themis.Kernel.Posting.Milestone1GeneralLedgerFixture.AdjustmentAccountId;
    private static readonly string SystemUser = WellKnownUsers.System.Value.ToString();
    private static readonly string Audit = $"now(), '{SystemUser}', now(), '{SystemUser}'";

    private readonly PostgresFixture _db;

    public InvariantQueryTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^\| (I-\d{2}) \|", RegexOptions.Multiline)]
    private static partial Regex InvariantRow();

    public async ValueTask InitializeAsync() => await _db.TruncateAsync(Ct);

    public async ValueTask DisposeAsync() => await _db.TruncateAsync(CancellationToken.None);

    // ---- completeness ---------------------------------------------------------------------------------------------

    [Fact]
    public void Every_invariant_of_the_specification_has_a_query_or_is_held_by_the_privilege_tests_or_is_listed_with_the_phase_that_makes_it_checkable()
    {
        string specification = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "design", "themis-phase-0-specification.md"));
        HashSet<string> specified = InvariantRow().Matches(specification).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        HashSet<string> accounted = InvariantQueries.Covered
            .Concat(InvariantQueries.HeldByPrivilegeTests)
            .Concat(InvariantQueries.NotYetApplicable.Select(entry => entry.Id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(specified.Count >= 15, "The Specification snapshot lists fewer invariants than expected.");
        Assert.Equal(specified.Order(StringComparer.Ordinal), accounted.Order(StringComparer.Ordinal));
        Assert.Equal(InvariantQueries.Covered.Count + InvariantQueries.HeldByPrivilegeTests.Count + InvariantQueries.NotYetApplicable.Count, accounted.Count);
        Assert.All(InvariantQueries.Covered, id => Assert.True(File.Exists(InvariantQueries.PathOf(id)), $"{id}.sql is missing from db/invariants."));
        Assert.All(InvariantQueries.NotYetApplicable, entry => Assert.False(File.Exists(InvariantQueries.PathOf(entry.Id)), $"{entry.Id} has a query and should leave the not-yet-applicable list."));
        Assert.All(InvariantQueries.NotYetApplicable, entry => Assert.StartsWith("Phase ", entry.Phase, StringComparison.Ordinal));
        Assert.Equal(InvariantQueries.Covered.Order(StringComparer.Ordinal), Directory.GetFiles(InvariantQueries.Directory, "I-*.sql").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_query_returns_no_rows_on_the_migrated_and_seeded_database()
    {
        await using NpgsqlConnection connection = await Migrator();
        Assert.Empty(await InvariantQueries.ViolationsAsync(connection, Ct));
    }

    // ---- the constraints that make a violation impossible --------------------------------------------------------

    [Fact]
    public async Task I_02_the_database_refuses_a_journal_line_with_amount_0_a_sign_mismatch_and_an_inventory_entry_with_quantity_0()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            Guid entry = await JournalEntryAsync(connection, scaffold, "JE2026-000001");
            await AssertRejectedAsync(connection, CheckViolation, "ck_journal_line_amount", JournalLineSql(entry, 1, Inventory, "INVENTORY", "0", "0"));
            await AssertRejectedAsync(connection, CheckViolation, "ck_journal_line_txn_sign", JournalLineSql(entry, 1, Inventory, "INVENTORY", "5", "-5"));
            await AssertRejectedAsync(connection, CheckViolation, "ck_inventory_entry_quantity", InventoryEntrySql(scaffold, "0", "0", June, entry));
        });
    }

    [Fact]
    public async Task I_08_the_database_refuses_an_inventory_entry_with_a_cost_and_no_journal_entry()
    {
        await InTransactionAsync(async (connection, scaffold) =>
            await AssertRejectedAsync(connection, CheckViolation, "ck_inventory_entry_journal_entry", InventoryEntrySql(scaffold, "5", "10.00", June, journalEntry: null)));
    }

    [Fact]
    public async Task I_04_the_database_refuses_an_item_cost_state_with_a_value_at_quantity_0_or_a_negative_value()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            await AssertRejectedAsync(connection, CheckViolation, "ck_item_cost_state_value", CostStateSql(scaffold, "0", "10.00"));
            await AssertRejectedAsync(connection, CheckViolation, "ck_item_cost_state_value", CostStateSql(scaffold, "5", "-1.00"));
        });
    }

    [Fact]
    public async Task I_09_the_database_refuses_a_duplicate_journal_entry_number_in_a_company()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            await JournalEntryAsync(connection, scaffold, "JE2026-000001");
            await AssertRejectedAsync(connection, UniqueViolation, "ux_journal_entry_company_number", JournalEntrySql(Guid.NewGuid(), scaffold, "JE2026-000001", June));
        });
    }

    // ---- planted violations the queries must report -------------------------------------------------------------

    [Fact]
    public async Task I_01_a_planted_journal_entry_whose_lines_do_not_sum_to_0_is_reported()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            Guid entry = await JournalEntryAsync(connection, scaffold, "JE2026-000001");
            await ExecuteAsync(connection, JournalLineSql(entry, 1, Inventory, "INVENTORY", "10.00", "10.00"));
            await ExecuteAsync(connection, JournalLineSql(entry, 2, Adjustment, "INVENTORY_ADJUSTMENT", "-9.99", "-9.99"));

            IReadOnlyList<string> rows = await InvariantQueries.RowsAsync(connection, "I-01", Ct);
            string row = Assert.Single(rows);
            Assert.Contains($"journal_entry_id={entry}", row, StringComparison.Ordinal);
            Assert.Contains("balance=0.0100", row, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task I_03_a_planted_balance_that_differs_from_its_entries_a_negative_balance_and_entries_without_a_balance_row_are_reported()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            // (a) a balance of 5 with no entries behind it.
            await ExecuteAsync(connection, StockBalanceSql(scaffold, Warehouse, "Available", "5"));
            // (b) a balance of -1 that matches its entry, in a warehouse that refuses negative stock (every warehouse in v1, D-09).
            Guid entry = await JournalEntryAsync(connection, scaffold, "JE2026-000001");
            await ExecuteAsync(connection, InventoryEntrySql(scaffold, "-1", "0", June, entry, stockStatus: "Blocked"));
            await ExecuteAsync(connection, StockBalanceSql(scaffold, Warehouse, "Blocked", "-1"));
            // (c) an entry in the second fixture-independent key with no balance row at all.
            await ExecuteAsync(connection, InventoryEntrySql(scaffold, "3", "0", June, entry, warehouse: scaffold.OtherWarehouse));

            IReadOnlyList<string> rows = await InvariantQueries.RowsAsync(connection, "I-03", Ct);
            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, row => row.Contains("stock_status=Available", StringComparison.Ordinal) && row.Contains("violation=on hand differs from the entries", StringComparison.Ordinal));
            Assert.Contains(rows, row => row.Contains("stock_status=Blocked", StringComparison.Ordinal) && row.Contains("violation=on hand below 0", StringComparison.Ordinal));
            Assert.Contains(rows, row => row.Contains($"warehouse_id={scaffold.OtherWarehouse}", StringComparison.Ordinal) && row.Contains("violation=entries without a stock balance row", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task I_04_a_planted_cost_state_that_differs_from_its_entries_and_entries_without_a_cost_state_row_are_reported()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            Guid entry = await JournalEntryAsync(connection, scaffold, "JE2026-000001");
            await ExecuteAsync(connection, InventoryEntrySql(scaffold, "4", "8.00", June, entry));
            // Quantity right, value wrong.
            await ExecuteAsync(connection, CostStateSql(scaffold, "4", "8.01"));
            IReadOnlyList<string> valueRows = await InvariantQueries.RowsAsync(connection, "I-04", Ct);
            Assert.Contains("violation=value differs from the entries", Assert.Single(valueRows), StringComparison.Ordinal);

            // Quantity wrong.
            await ExecuteAsync(connection, $"UPDATE kernel.item_cost_state SET quantity_base = 5, inventory_value = 8.00 WHERE item_id = '{scaffold.Item}'");
            IReadOnlyList<string> quantityRows = await InvariantQueries.RowsAsync(connection, "I-04", Ct);
            Assert.Contains("violation=quantity differs from the entries", Assert.Single(quantityRows), StringComparison.Ordinal);

            // No cost state row for an item with entries.
            await ExecuteAsync(connection, $"DELETE FROM kernel.item_cost_state WHERE item_id = '{scaffold.Item}'");
            IReadOnlyList<string> missingRows = await InvariantQueries.RowsAsync(connection, "I-04", Ct);
            string missing = Assert.Single(missingRows);
            Assert.Contains($"item_id={scaffold.Item}", missing, StringComparison.Ordinal);
            Assert.Contains("violation=entries without an item cost state row", missing, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task I_06_a_planted_INVENTORY_line_without_an_entry_cost_and_an_entry_cost_without_a_ledger_line_are_reported_at_their_dates()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            // May: an entry cost of 7.00 with no INVENTORY line; June: an INVENTORY line of 7.00 balances the cumulative totals again, so only May is off.
            Guid may = await JournalEntryAsync(connection, scaffold, "JE2026-000001", "2026-05-10");
            await ExecuteAsync(connection, InventoryEntrySql(scaffold, "7", "7.00", "2026-05-10", may));
            Guid june = await JournalEntryAsync(connection, scaffold, "JE2026-000002", June);
            await ExecuteAsync(connection, JournalLineSql(june, 1, Inventory, "INVENTORY", "7.00", "7.00"));
            await ExecuteAsync(connection, JournalLineSql(june, 2, Adjustment, "INVENTORY_ADJUSTMENT", "-7.00", "-7.00"));

            IReadOnlyList<string> rows = await InvariantQueries.RowsAsync(connection, "I-06", Ct);
            string row = Assert.Single(rows);
            Assert.Contains("posting_date=05/10/2026", row, StringComparison.Ordinal);
            Assert.Contains("ledger_balance=0", row, StringComparison.Ordinal);
            Assert.Contains("inventory_cost=7.0000", row, StringComparison.Ordinal);

            // July: an INVENTORY line of 1.00 with no entry behind it is off from July on.
            Guid july = await JournalEntryAsync(connection, scaffold, "JE2026-000003", "2026-07-01");
            await ExecuteAsync(connection, JournalLineSql(july, 1, Inventory, "INVENTORY", "1.00", "1.00"));
            await ExecuteAsync(connection, JournalLineSql(july, 2, Adjustment, "INVENTORY_ADJUSTMENT", "-1.00", "-1.00"));
            IReadOnlyList<string> later = await InvariantQueries.RowsAsync(connection, "I-06", Ct);
            Assert.Equal(2, later.Count);
            Assert.Contains(later, r => r.Contains("posting_date=07/01/2026", StringComparison.Ordinal) && r.Contains("difference=1.0000", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task I_08_a_planted_posted_document_with_a_costed_entry_and_no_journal_entry_and_one_with_a_journal_entry_and_no_cost_are_reported()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            // The costed entry links to a journal entry of another document, so the scaffold document has a cost and no entry of its own.
            Scaffold other = await ScaffoldAsync(connection, "OTHER");
            Guid othersEntry = await JournalEntryAsync(connection, other, "JE2026-000001");
            await ExecuteAsync(connection, InventoryEntrySql(scaffold, "2", "4.00", June, othersEntry));
            // The other document has a journal entry and no costed entry.
            IReadOnlyList<string> rows = await InvariantQueries.RowsAsync(connection, "I-08", Ct);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => row.Contains($"entity_id={scaffold.Document}", StringComparison.Ordinal) && row.Contains("violation=posted document with a costed entry and no journal entry", StringComparison.Ordinal));
            Assert.Contains(rows, row => row.Contains($"entity_id={other.Document}", StringComparison.Ordinal) && row.Contains("violation=posted document without a costed entry and a journal entry", StringComparison.Ordinal));

            // A Draft is never reported, whatever it carries.
            await ExecuteAsync(connection, $"UPDATE kernel.document SET status = 'Draft' WHERE id IN ('{scaffold.Document}', '{other.Document}')");
            Assert.Empty(await InvariantQueries.RowsAsync(connection, "I-08", Ct));
        });
    }

    [Fact]
    public async Task I_09_a_planted_gap_a_number_beyond_the_series_counter_and_a_gapless_series_of_an_uncovered_type_are_reported()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            Guid series = Guid.Parse("00000000-0000-7000-8000-000000000802");
            await JournalEntryAsync(connection, scaffold, "JE2026-000001");
            await JournalEntryAsync(connection, scaffold, "JE2026-000003");
            await ExecuteAsync(connection, $"UPDATE kernel.number_series SET next_number = 4 WHERE id = '{series}'");
            IReadOnlyList<string> gap = await InvariantQueries.RowsAsync(connection, "I-09", Ct);
            string gapRow = Assert.Single(gap);
            Assert.Contains("number=2; violation=number missing (gap)", gapRow, StringComparison.Ordinal);

            await ExecuteAsync(connection, $"UPDATE kernel.number_series SET next_number = 3 WHERE id = '{series}'");
            IReadOnlyList<string> beyond = await InvariantQueries.RowsAsync(connection, "I-09", Ct);
            Assert.Equal(2, beyond.Count);
            Assert.Contains(beyond, row => row.Contains("number=3; violation=number at or above next_number", StringComparison.Ordinal));

            // A journal entry of another fiscal year is matched to its own series, never to this one: without a 2027 series it is simply not counted.
            await ExecuteAsync(connection, $"UPDATE kernel.number_series SET next_number = 4 WHERE id = '{series}'");
            await ExecuteAsync(connection, "INSERT INTO kernel.number_series (id, company_id, document_type, scope, fiscal_year_id, prefix, padding, next_number, gapless, type_requires_gapless, created_at, created_by, updated_at, updated_by) " +
                $"VALUES (gen_random_uuid(), '{Company}', 'INVENTORY_ADJUSTMENT', 'PerFiscalYear', '{Milestone1OrganisationFixture.FiscalYearId}', 'IAG-', 6, 1, true, false, {Audit})");
            IReadOnlyList<string> uncovered = await InvariantQueries.RowsAsync(connection, "I-09", Ct);
            Assert.Equal(2, uncovered.Count);
            Assert.Contains(uncovered, row => row.Contains("document_type=INVENTORY_ADJUSTMENT", StringComparison.Ordinal) && row.Contains("violation=gapless series of a document type this query does not cover", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task I_15_a_planted_item_with_stock_and_no_reported_row_a_negative_reported_value_and_a_company_total_without_a_ledger_balance_are_reported()
    {
        await InTransactionAsync(async (connection, scaffold) =>
        {
            // The cost state says 2 worth 10.00 and there is no balance row: the view reports nothing for the item; the company total of 0 still matches the empty ledger.
            await ExecuteAsync(connection, CostStateSql(scaffold, "2", "10.00"));
            IReadOnlyList<string> noRow = await InvariantQueries.RowsAsync(connection, "I-15", Ct);
            Assert.Contains("violation=item with stock and no reported row", Assert.Single(noRow), StringComparison.Ordinal);

            // A balance of 2 behind it: the view reports 10.00, the item ties, the company total of 10.00 has no INVENTORY balance behind it.
            await ExecuteAsync(connection, StockBalanceSql(scaffold, Warehouse, "Available", "2"));
            IReadOnlyList<string> noLedger = await InvariantQueries.RowsAsync(connection, "I-15", Ct);
            Assert.Contains("violation=reported company total differs from the INVENTORY balance", Assert.Single(noLedger), StringComparison.Ordinal);

            // A balance of -2 in the main warehouse beside 4 in another, against a cost state of 2 worth 10.00: the other warehouse
            // ranks first and takes the remainder, the main warehouse keeps its share of -10.00.
            await ExecuteAsync(connection, $"UPDATE kernel.stock_balance SET on_hand_base = -2 WHERE item_id = '{scaffold.Item}'");
            await ExecuteAsync(connection, StockBalanceSql(scaffold, scaffold.OtherWarehouse, "Available", "4"));
            IReadOnlyList<string> negative = await InvariantQueries.RowsAsync(connection, "I-15", Ct);
            Assert.Contains(negative, row => row.Contains($"item_id={scaffold.Item}", StringComparison.Ordinal) && row.Contains("violation=negative reported warehouse value", StringComparison.Ordinal));
            await ExecuteAsync(connection, $"DELETE FROM kernel.stock_balance WHERE warehouse_id = '{scaffold.OtherWarehouse}'");

            // Two parts cannot be planted because the view guarantees them by construction (ReportingModel, D-54): the reported
            // values always sum to the item cost state value, since the remainder of the truncated shares goes to one warehouse
            // (a cost state quantity of 4 behind a balance of 2 still reports the full 10.00), and a warehouse with no stock on
            // hand never has a row (HAVING on hand <> 0). StockByWarehouseViewTests prove both on real postings; the query keeps
            // the checks so that a changed view cannot pass unnoticed.
            await ExecuteAsync(connection, $"UPDATE kernel.stock_balance SET on_hand_base = 2 WHERE item_id = '{scaffold.Item}'");
            await ExecuteAsync(connection, $"UPDATE kernel.item_cost_state SET quantity_base = 4 WHERE item_id = '{scaffold.Item}'");
            IReadOnlyList<string> structural = await InvariantQueries.RowsAsync(connection, "I-15", Ct);
            Assert.DoesNotContain(structural, row => row.Contains("violation=reported values do not sum", StringComparison.Ordinal));
            Assert.DoesNotContain(structural, row => row.Contains("violation=reported row for a warehouse without stock", StringComparison.Ordinal));
        });
    }

    // ---- scaffolding: the parents a planted row needs, written as the table owner ------------------------------

    private sealed record Scaffold(Guid Item, Guid Document, Guid Line, Guid OtherWarehouse);

    private async Task InTransactionAsync(Func<NpgsqlConnection, Scaffold, Task> body)
    {
        await using NpgsqlConnection connection = await Migrator();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        Scaffold scaffold = await ScaffoldAsync(connection, "PLANT");
        await body(connection, scaffold);
        await transaction.RollbackAsync(Ct);
    }

    private static async Task<Scaffold> ScaffoldAsync(NpgsqlConnection connection, string prefix)
    {
        Scaffold scaffold = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        string number = $"{prefix}-{scaffold.Item.ToString("N")[..8].ToUpperInvariant()}";
        await ExecuteAsync(connection, $"INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, created_at, created_by, updated_at, updated_by) VALUES ('{scaffold.Item}', '{number}', 'Planted item', 'Stocked', 'H87', 'None', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.item_company (id, item_id, company_id, status, item_posting_group, tax_class, costing_method, sales_allowed, purchase_allowed, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{scaffold.Item}', '{Company}', 'Active', 'TRADE_GOODS', 'Standard', 'MovingAverage', true, true, {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by) VALUES ('{scaffold.OtherWarehouse}', '{Milestone1OrganisationFixture.SiteId}', '{Company}', '{number}', 'Planted warehouse', false, 'Active', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.document (id, company_id, document_type, status, document_number, draft_reference, document_date, posting_date, currency, warehouse_id, posted_at, posted_by, created_at, created_by, updated_at, updated_by) VALUES ('{scaffold.Document}', '{Company}', 'INVENTORY_ADJUSTMENT', 'Posted', '{number}', '{scaffold.Document}', '{June}', '{June}', 'SEK', '{Warehouse}', now(), '{SystemUser}', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.document_line (id, company_id, document_id, line_number, line_type, item_id, description, quantity, unit_code, quantity_base, warehouse_id, stock_status, line_status, created_at, created_by, updated_at, updated_by) VALUES ('{scaffold.Line}', '{Company}', '{scaffold.Document}', 1, 'Item', '{scaffold.Item}', 'Planted line', 1, 'H87', 1, '{Warehouse}', 'Available', 'Open', {Audit})");
        return scaffold;
    }

    private static string JournalEntrySql(Guid id, Scaffold scaffold, string number, string postingDate) =>
        $"INSERT INTO kernel.journal_entry (id, company_id, entry_number, fiscal_period_id, posting_date, document_date, source_document_id, command_id, description, recorded_at, recorded_by) " +
        $"VALUES ('{id}', '{Company}', '{number}', '{PeriodOf(postingDate)}', '{postingDate}', '{postingDate}', '{scaffold.Document}', gen_random_uuid(), 'Planted entry', now(), '{SystemUser}')";

    private static async Task<Guid> JournalEntryAsync(NpgsqlConnection connection, Scaffold scaffold, string number, string postingDate = June)
    {
        Guid id = Guid.NewGuid();
        await ExecuteAsync(connection, JournalEntrySql(id, scaffold, number, postingDate));
        return id;
    }

    private static string JournalLineSql(Guid entry, int lineNumber, Guid account, string role, string amount, string txnAmount) =>
        $"INSERT INTO kernel.journal_line (id, journal_entry_id, company_id, line_number, account_id, account_role, amount, txn_currency, txn_amount) " +
        $"VALUES (gen_random_uuid(), '{entry}', '{Company}', {lineNumber}, '{account}', '{role}', {amount}, 'SEK', {txnAmount})";

    private static string InventoryEntrySql(Scaffold scaffold, string quantity, string cost, string postingDate, Guid? journalEntry, string stockStatus = "Available", Guid? warehouse = null) =>
        $"INSERT INTO kernel.inventory_entry (id, company_id, item_id, warehouse_id, stock_status, movement_type, quantity_base, cost_amount, unit_cost, posting_date, document_date, source_line_id, journal_entry_id, command_id, recorded_at, recorded_by) " +
        $"VALUES (gen_random_uuid(), '{Company}', '{scaffold.Item}', '{warehouse ?? Warehouse}', '{stockStatus}', 'AdjustmentIn', {quantity}, {cost}, 0, '{postingDate}', '{postingDate}', '{scaffold.Line}', {(journalEntry is null ? "NULL" : $"'{journalEntry}'")}, gen_random_uuid(), now(), '{SystemUser}')";

    private static string StockBalanceSql(Scaffold scaffold, Guid warehouse, string stockStatus, string onHand) =>
        $"INSERT INTO kernel.stock_balance (id, company_id, item_id, warehouse_id, stock_status, on_hand_base, reserved_base, created_at, created_by, updated_at, updated_by) " +
        $"VALUES (gen_random_uuid(), '{Company}', '{scaffold.Item}', '{warehouse}', '{stockStatus}', {onHand}, 0, {Audit})";

    private static string CostStateSql(Scaffold scaffold, string quantity, string value) =>
        $"INSERT INTO kernel.item_cost_state (id, company_id, item_id, quantity_base, inventory_value, average_cost, created_at, created_by, updated_at, updated_by) " +
        $"VALUES (gen_random_uuid(), '{Company}', '{scaffold.Item}', {quantity}, {value}, NULL, {Audit})";

    private static Guid PeriodOf(string postingDate) => Milestone1OrganisationFixture.PeriodId(int.Parse(postingDate.AsSpan(5, 2), CultureInfo.InvariantCulture));

    private async Task<NpgsqlConnection> Migrator() => await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Runs the statement in a savepoint and expects the named constraint to refuse it; the transaction stays usable.</summary>
    private static async Task AssertRejectedAsync(NpgsqlConnection connection, string sqlState, string constraint, string sql)
    {
        await ExecuteAsync(connection, "SAVEPOINT probe");
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal((sqlState, constraint), (error.SqlState, error.ConstraintName));
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT probe");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Themis.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Themis.sln not found above the test output directory.");
    }
}
