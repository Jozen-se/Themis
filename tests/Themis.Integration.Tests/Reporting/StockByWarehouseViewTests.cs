using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;

namespace Themis.Integration.Tests.Reporting;

/// <summary>
/// The reporting view <c>reporting.stock_by_warehouse</c> end to end (step 10 brief; D-54, I-15): after real
/// postings the rows equal ItemCostState and the inventory entries (I-03, I-04), the company total equals the
/// INVENTORY account balance (I-06), the warehouse values are the rounded shares with the remainder on the
/// warehouse with the largest on hand then the lowest id, none negative, summing exactly, and a warehouse whose
/// stock reaches 0 drops out; Blocked and Available stock split into their columns; themis_reporting reads the view
/// and nothing else (I-14); the columns and types equal a declared list.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class StockByWarehouseViewTests : IAsyncLifetime
{
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string View = "reporting.stock_by_warehouse";
    private static readonly string[] RefusedStates = ["42501", "55000"];

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Main = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private readonly Guid[] _extraWarehouses = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
    private PipelineTestHost _host = null!;
    private int _sequence;

    public StockByWarehouseViewTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid Second => _extraWarehouses[0];

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        // Three more warehouses at the fixture site, for the allocation across warehouses (test data, docs/development/test-seams.md).
        string system = Themis.Kernel.Contracts.Security.WellKnownUsers.System.Value.ToString();
        await _db.ExecuteAsMigratorAsync(string.Join("; ", _extraWarehouses.Select((id, index) =>
            $"INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by) VALUES ('{id}', '{Milestone1OrganisationFixture.SiteId}', '{Company}', 'RPT-W{index + 2}', 'Reporting test warehouse {index + 2}', false, 'Active', now(), '{system}', now(), '{system}')")), Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.TruncateAsync(CancellationToken.None);
        await _db.ExecuteAsMigratorAsync($"DELETE FROM kernel.warehouse WHERE id IN ({string.Join(", ", _extraWarehouses.Select(id => $"'{id}'"))})", CancellationToken.None);
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task I_04_after_the_reference_calculations_the_rows_equal_the_cost_states_and_the_entries_and_emptied_items_have_no_row()
    {
        Guid test001 = await CreateItemAsync("TEST-001");
        Guid test002 = await CreateItemAsync("TEST-002");
        Guid test003 = await CreateItemAsync("TEST-003");
        foreach ((Guid item, string quantity, string? cost) in new (Guid, string, string?)[] { (test001, "100", "2.00"), (test001, "100", "3.00"), (test001, "-50", null), (test001, "-150", null), (test002, "3", "1.00"), (test002, "3", "1.01"), (test002, "-1", null) })
        {
            await PostAsync(Line(1, item, quantity, enteredUnitCost: cost));
        }

        // TEST-002 after step 8: 5 on hand worth 5.02 in the fixture warehouse, equal to its cost state and its entries (I-03, I-04).
        ViewRow test002Row = Assert.Single(await RowsAsync(test002));
        Assert.Equal((Company, "SE01", "MAIN", "H87", "SEK"), (test002Row.CompanyId, test002Row.CompanyCode, test002Row.WarehouseCode, test002Row.UnitCode, test002Row.CurrencyCode));
        Assert.StartsWith("TEST-002", test002Row.ItemNumber, StringComparison.Ordinal);
        Assert.Equal($"Item {test002Row.ItemNumber}", test002Row.ItemDescription);
        Assert.Equal((5m, 5m, 0m, 0m, 5.02m, 1.004m), (test002Row.OnHandTotal, test002Row.OnHandAvailable, test002Row.OnHandBlocked, test002Row.Reserved, test002Row.StockValue, test002Row.ItemAverageCost));
        Assert.Equal((5m, 5.02m), await CostStateAsync(test002));
        Assert.Equal((5m, 5.02m), await EntrySumsAsync(test002, Main));
        Assert.Empty(await RowsAsync(test001));

        foreach ((Guid item, string quantity, string? cost) in new (Guid, string, string?)[] { (test002, "-5", null), (test003, "100000000", "0.01"), (test003, "200000000", "0.02"), (test003, "-150000000", null), (test003, "150000000", null) })
        {
            await PostAsync(Line(1, item, quantity, enteredUnitCost: cost));
        }

        Assert.Empty(await RowsAsync(test002));
        ViewRow test003Row = Assert.Single(await RowsAsync(test003));
        Assert.Equal((300_000_000m, 5_000_000.00m, 0.0166666667m), (test003Row.OnHandTotal, test003Row.StockValue, test003Row.ItemAverageCost));
        Assert.Equal((300_000_000m, 5_000_000.00m), await EntrySumsAsync(test003, Main));
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task I_15_the_warehouse_values_are_rounded_shares_the_remainder_goes_to_the_largest_on_hand_then_the_lowest_id_and_an_emptied_warehouse_drops_out()
    {
        // Item A: 0.10 over 3 units, 2 in MAIN and 1 in the second warehouse: shares 0.0666 and 0.0333 round down to 0.06 and 0.03; the 0.01 goes to MAIN.
        Guid itemA = await CreateItemAsync("RPT-A");
        await PostAsync(Line(1, itemA, "2", Main, enteredUnitCost: "0.05"), Line(2, itemA, "1", Second, enteredUnitCost: "0"));
        IReadOnlyList<ViewRow> rowsA = await RowsAsync(itemA);
        Assert.Equal(2, rowsA.Count);
        Assert.Equal(0.07m, rowsA.Single(row => row.WarehouseId == Main).StockValue);
        Assert.Equal(0.03m, rowsA.Single(row => row.WarehouseId == Second).StockValue);
        Assert.Equal(0.10m, rowsA.Sum(row => row.StockValue));

        // Item B: 0.02 over four warehouses with one unit each: every share rounds down to 0; the whole 0.02 lands on the lowest warehouse id; nothing is negative.
        Guid itemB = await CreateItemAsync("RPT-B");
        Guid[] four = [Main, .. _extraWarehouses];
        await PostAsync([.. four.Select((warehouse, index) => Line(index + 1, itemB, "1", warehouse, enteredUnitCost: index == 0 ? "0.02" : "0"))]);
        IReadOnlyList<ViewRow> rowsB = await RowsAsync(itemB);
        Guid lowestId = (Guid)(await _host.ScalarAsync("SELECT id FROM kernel.warehouse WHERE id = ANY(@ids) ORDER BY id LIMIT 1", Ct, ("ids", four)))!;
        Assert.Equal(4, rowsB.Count);
        Assert.Equal(0.02m, rowsB.Single(row => row.WarehouseId == lowestId).StockValue);
        Assert.All(rowsB.Where(row => row.WarehouseId != lowestId), row => Assert.Equal(0m, row.StockValue));
        Assert.All(rowsB, row => Assert.True(row.StockValue >= 0));
        Assert.Equal(0.02m, rowsB.Sum(row => row.StockValue));

        // Item A again: issuing the second warehouse's unit costs round(1 x 0.10 / 3) = 0.03; the warehouse drops out and MAIN carries the remaining 0.07.
        await PostAsync(Line(1, itemA, "-1", Second));
        ViewRow onlyMain = Assert.Single(await RowsAsync(itemA));
        Assert.Equal((Main, 2m, 0.07m), (onlyMain.WarehouseId, onlyMain.OnHandTotal, onlyMain.StockValue));
        Assert.Equal((2m, 0.07m), await CostStateAsync(itemA));
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task Blocked_and_Available_stock_of_one_warehouse_split_into_their_columns_and_both_count_in_the_total_and_the_value()
    {
        Guid item = await CreateItemAsync("RPT-C");
        await PostAsync(Line(1, item, "6", Main, "Available", "1.00"), Line(2, item, "4", Main, "Blocked", "1.00"));

        ViewRow row = Assert.Single(await RowsAsync(item));
        Assert.Equal((10m, 6m, 4m, 0m, 10.00m), (row.OnHandTotal, row.OnHandAvailable, row.OnHandBlocked, row.Reserved, row.StockValue));
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task I_14_the_reporting_role_reads_the_view_and_nothing_else_and_the_view_runs_with_its_owner_rights()
    {
        Guid item = await CreateItemAsync("RPT-D");
        await PostAsync(Line(1, item, "1", Main, enteredUnitCost: "1.00"));

        await using NpgsqlConnection reporting = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_reporting"), Ct);
        Assert.Equal(1L, await ScalarAsync(reporting, $"SELECT count(*) FROM {View}"));
        Assert.Equal(1.00m, await ScalarAsync(reporting, $"SELECT stock_value FROM {View} WHERE item_id = @item", ("item", item)));
        foreach (string refused in new[] { "SELECT count(*) FROM kernel.stock_balance", "SELECT count(*) FROM kernel.item_cost_state", "SELECT count(*) FROM kernel.journal_line", $"INSERT INTO {View} (company_id) VALUES (gen_random_uuid())", $"UPDATE {View} SET stock_value = 0", $"DELETE FROM {View}", "CREATE VIEW reporting.rogue AS SELECT 1" })
        {
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(reporting, refused));
            Assert.Contains(exception.SqlState, RefusedStates);
        }

        // Owned by the migrator, a plain view (no security_invoker), so the reporting role needs nothing in kernel (Build Brief, Database roles).
        Assert.Equal("themis_migrator", await _host.ScalarAsync("SELECT pg_get_userbyid(relowner) FROM pg_class WHERE oid = 'reporting.stock_by_warehouse'::regclass", Ct));
        Assert.Equal("v", ((char)(await _host.ScalarAsync("SELECT relkind FROM pg_class WHERE oid = 'reporting.stock_by_warehouse'::regclass", Ct))!).ToString());
        Assert.Null(await _host.ScalarAsync("SELECT array_to_string(reloptions, ',') FROM pg_class WHERE oid = 'reporting.stock_by_warehouse'::regclass AND reloptions IS NOT NULL", Ct));
    }

    [Fact]
    public async Task The_view_columns_and_types_equal_the_declared_list_and_the_comment_names_the_KPI_definition_and_version()
    {
        (string Name, string Type)[] expected =
        [
            ("company_id", "uuid"), ("company_code", "character varying"), ("item_id", "uuid"), ("item_number", "character varying"), ("item_description", "character varying"),
            ("warehouse_id", "uuid"), ("warehouse_code", "character varying"), ("unit_code", "character varying"),
            ("on_hand_total", "numeric"), ("on_hand_available", "numeric"), ("on_hand_blocked", "numeric"), ("reserved", "numeric"),
            ("stock_value", "numeric"), ("currency_code", "character varying"), ("item_average_cost", "numeric"),
        ];

        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using NpgsqlCommand command = new("SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'reporting' AND table_name = 'stock_by_warehouse' ORDER BY ordinal_position", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct);
        List<(string, string)> actual = [];
        while (await reader.ReadAsync(Ct))
        {
            actual.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.Equal(expected, actual);
        string comment = (string)(await _host.ScalarAsync("SELECT obj_description('reporting.stock_by_warehouse'::regclass, 'pg_class')", Ct))!;
        Assert.Contains($"{ReportingModel.KpiDefinition} version {ReportingModel.KpiVersion}", comment, StringComparison.Ordinal);
        Assert.Contains("D-54", comment, StringComparison.Ordinal);
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    private sealed record ViewRow(Guid CompanyId, string CompanyCode, Guid ItemId, string ItemNumber, string ItemDescription, Guid WarehouseId, string WarehouseCode, string UnitCode, decimal OnHandTotal, decimal OnHandAvailable, decimal OnHandBlocked, decimal Reserved, decimal StockValue, string CurrencyCode, decimal? ItemAverageCost);

    private async Task<Guid> CreateItemAsync(string number)
    {
        string unique = $"{number}-{++_sequence}";
        CommandReply reply = await _host.PostAsync("/v1/kernel/items", new { companyId = Company, itemNumber = unique, description = $"Item {unique}", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "rpt-item-" + unique, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private static object Line(int number, Guid item, string quantity, Guid? warehouse = null, string stockStatus = "Available", string? enteredUnitCost = null) =>
        new { lineNumber = number, itemId = item, quantity, warehouseId = warehouse, stockStatus, enteredUnitCost };

    private async Task PostAsync(params object[] lines)
    {
        string key = $"rpt-{++_sequence}";
        CommandReply created = await _host.PostAsync(Adjustments, new { companyId = Company, documentDate = "2026-06-15", reasonCode = "CountDifference", warehouseId = Main, postingDate = "2026-06-15", lines }, key + "-create", cancellationToken: Ct);
        Assert.True(created.Status == 200, created.Body);
        CommandReply posted = await _host.PostAsync($"{Adjustments}/{created.Json!["id"]!.GetValue<Guid>()}/post", null, key, ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(posted.Status == 200, posted.Body);
    }

    private async Task<IReadOnlyList<ViewRow>> RowsAsync(Guid item)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using NpgsqlCommand command = new($"SELECT company_id, company_code, item_id, item_number, item_description, warehouse_id, warehouse_code, unit_code, on_hand_total, on_hand_available, on_hand_blocked, reserved, stock_value, currency_code, item_average_cost FROM {View} WHERE item_id = @item ORDER BY warehouse_code", connection);
        command.Parameters.AddWithValue("item", item);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct);
        List<ViewRow> rows = [];
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new ViewRow(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6), reader.GetString(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12), reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetDecimal(14)));
        }

        return rows;
    }

    private async Task<(decimal Quantity, decimal Value)> CostStateAsync(Guid item) =>
        ((decimal)(await _host.ScalarAsync("SELECT quantity_base FROM kernel.item_cost_state WHERE item_id = @item", Ct, ("item", item)))!, (decimal)(await _host.ScalarAsync("SELECT inventory_value FROM kernel.item_cost_state WHERE item_id = @item", Ct, ("item", item)))!);

    private async Task<(decimal Quantity, decimal Cost)> EntrySumsAsync(Guid item, Guid warehouse) =>
        ((decimal)(await _host.ScalarAsync("SELECT coalesce(sum(quantity_base), 0) FROM kernel.inventory_entry WHERE item_id = @item AND warehouse_id = @warehouse", Ct, ("item", item), ("warehouse", warehouse)))!, (decimal)(await _host.ScalarAsync("SELECT coalesce(sum(cost_amount), 0) FROM kernel.inventory_entry WHERE item_id = @item AND warehouse_id = @warehouse", Ct, ("item", item), ("warehouse", warehouse)))!);

    /// <summary>I-15 over everything posted so far: per company and item the warehouse values sum to the cost state, none negative, every item with stock has rows; per company the total equals the INVENTORY-role balance (I-06).</summary>
    private async Task AssertInvariantsAsync()
    {
        Assert.Equal(0L, await _host.ScalarAsync($"SELECT count(*) FROM (SELECT company_id, item_id, sum(stock_value) AS total, min(stock_value) AS least FROM {View} GROUP BY company_id, item_id) v JOIN kernel.item_cost_state s ON s.company_id = v.company_id AND s.item_id = v.item_id WHERE v.total <> s.inventory_value OR v.least < 0", Ct));
        Assert.Equal(0L, await _host.ScalarAsync($"SELECT count(*) FROM kernel.item_cost_state s WHERE s.quantity_base <> 0 AND NOT EXISTS (SELECT 1 FROM {View} v WHERE v.company_id = s.company_id AND v.item_id = s.item_id)", Ct));
        Assert.Equal(0L, await _host.ScalarAsync($"SELECT count(*) FROM {View} WHERE on_hand_total = 0", Ct));
        Assert.Equal(0L, await _host.ScalarAsync($"SELECT count(*) FROM kernel.company c WHERE (SELECT coalesce(sum(stock_value), 0) FROM {View} v WHERE v.company_id = c.id) <> (SELECT coalesce(sum(l.amount), 0) FROM kernel.journal_line l WHERE l.company_id = c.id AND l.account_role = '{AccountRoles.Inventory}')", Ct));
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
}
