using System.Text.Json;
using Themis.Integration.Tests.Identity;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.MasterData;

/// <summary>
/// ITEM.CREATE and the item reads end to end through the pipeline (step 5 brief, acceptance 3 to 6): the clerk
/// creates items in SE01, a duplicate item number is R-MD-05 (422), an unknown reference is a 400 with its own code
/// and no ruleId stored as the key's result, one audit and one outbox event per created row (D-44, R-AUD-01), the
/// preparer without ITEM.CREATE gets 403 (R-SEC-01), and the reads take their scope from the route, 403 before 404 (D-47).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ItemCommandTests : IAsyncLifetime
{
    private const string Items = "/v1/kernel/items";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private IdentityTestData? _users;
    private MasterDataTestData? _data;

    public ItemCommandTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
        try
        {
            _users = await IdentityTestData.CreateAsync(_db, Ct);
            _data = await MasterDataTestData.CreateAsync(_db, Ct);
        }
        catch
        {
            // xunit skips DisposeAsync when InitializeAsync throws; the rows written so far must not leak into the next class.
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_data is not null)
        {
            await _data.DisposeAsync();
        }

        if (_users is not null)
        {
            await _users.DisposeAsync();
        }

        await _host.DisposeAsync();
    }

    /// <summary>Items the tests created through the pipeline; the test data's orphan item does not count.</summary>
    private async Task<long> ItemsCreatedAsync() =>
        (long)(await _host.ScalarAsync("SELECT count(*) FROM kernel.item WHERE item_number <> @orphan", Ct, ("orphan", MasterDataTestData.OrphanItemNumber)))!;

    private static object Item(string number, string unit = "H87", string group = "TRADE_GOODS", Guid? warehouse = null, string? salesMultiple = null, string? minOrderQty = null) => new
    {
        companyId = Company,
        itemNumber = number,
        description = $"Test item {number}",
        itemType = "Stocked",
        baseUnit = unit,
        lotTracking = "None",
        itemPostingGroup = group,
        taxClass = "Standard",
        costingMethod = "MovingAverage",
        defaultWarehouseId = warehouse,
        netWeightKg = "0.125",
        salesMultiple,
        minOrderQty,
    };

    [Fact]
    public async Task The_clerk_creates_an_item_with_its_company_row_and_two_audit_and_two_outbox_events_and_a_replay_writes_nothing()
    {
        CommandReply reply = await _host.PostAsync(Items, Item("TEST-001", warehouse: Milestone1OrganisationFixture.WarehouseId, salesMultiple: "10", minOrderQty: "100"), "item-1", correlationId: "corr-item-1", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Items, Item("TEST-001", warehouse: Milestone1OrganisationFixture.WarehouseId, salesMultiple: "10", minOrderQty: "100"), "item-1", cancellationToken: Ct);

        Assert.Equal(200, reply.Status);
        Assert.Equal("\"1\"", reply.ETag);
        Guid id = reply.Json!["id"]!.GetValue<Guid>();
        Guid rowId = reply.Json["company"]!["id"]!.GetValue<Guid>();
        Assert.Equal("TEST-001", reply.Json["itemNumber"]!.GetValue<string>());
        Assert.Equal("Test item TEST-001", reply.Json["description"]!.GetValue<string>());
        Assert.Equal("Stocked", reply.Json["itemType"]!.GetValue<string>());
        Assert.Equal("H87", reply.Json["baseUnit"]!.GetValue<string>());
        Assert.Equal("None", reply.Json["lotTracking"]!.GetValue<string>());
        Assert.Equal(1, reply.Json["version"]!.GetValue<long>());
        Assert.Equal(Company, reply.Json["company"]!["companyId"]!.GetValue<Guid>());
        Assert.Equal("Active", reply.Json["company"]!["status"]!.GetValue<string>());
        Assert.Equal("TRADE_GOODS", reply.Json["company"]!["itemPostingGroup"]!.GetValue<string>());
        Assert.Equal("Standard", reply.Json["company"]!["taxClass"]!.GetValue<string>());
        Assert.Equal("MovingAverage", reply.Json["company"]!["costingMethod"]!.GetValue<string>());
        Assert.True(reply.Json["company"]!["salesAllowed"]!.GetValue<bool>());
        Assert.True(reply.Json["company"]!["purchaseAllowed"]!.GetValue<bool>());
        Assert.Equal(Milestone1OrganisationFixture.WarehouseId, reply.Json["company"]!["defaultWarehouseId"]!.GetValue<Guid>());
        Assert.Equal(1, reply.Json["company"]!["version"]!.GetValue<long>());

        // D-37: decimals are strings, quantities at the unit's decimals (H87: 0), the net weight without trailing zeros; integers numbers.
        Assert.Equal(JsonValueKind.String, reply.Json["netWeightKg"]!.GetValueKind());
        Assert.Equal("0.125", reply.Json["netWeightKg"]!.GetValue<string>());
        Assert.Equal("10", reply.Json["salesMultiple"]!.GetValue<string>());
        Assert.Equal("100", reply.Json["minOrderQty"]!.GetValue<string>());
        Assert.Equal(JsonValueKind.Number, reply.Json["version"]!.GetValueKind());

        Assert.Equal(1L, await ItemsCreatedAsync());
        Assert.Equal(1L, await _host.CountAsync("kernel.item_company", Ct));
        Assert.Equal(Company, await _host.ScalarAsync("SELECT company_id FROM kernel.item_company", Ct));
        Assert.Equal(id, await _host.ScalarAsync("SELECT item_id FROM kernel.item_company", Ct));

        // R-AUD-01 and D-44: one audit event and one outbox event per row, the row's type in the name and its id as aggregate.
        const string audit = "FROM kernel.audit_event WHERE command_name = 'ITEM.CREATE'";
        Assert.Equal(2L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(id, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'Item'", Ct));
        Assert.Equal(rowId, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'ItemCompany'", Ct));
        Assert.Equal(TestUsers.Clerk, await _host.ScalarAsync($"SELECT DISTINCT user_id {audit}", Ct));
        Assert.Equal("item-1", await _host.ScalarAsync($"SELECT DISTINCT idempotency_key {audit}", Ct));
        Assert.Equal("corr-item-1", await _host.ScalarAsync($"SELECT DISTINCT correlation_id {audit}", Ct));
        Assert.Equal("TEST-001", await _host.ScalarAsync($"SELECT changes->'itemNumber'->>'after' {audit} AND entity_type = 'Item'", Ct));
        Assert.Equal("0.125", await _host.ScalarAsync($"SELECT changes->'netWeightKg'->>'after' {audit} AND entity_type = 'Item'", Ct));
        Assert.Equal("10", await _host.ScalarAsync($"SELECT changes->'salesMultiple'->>'after' {audit} AND entity_type = 'Item'", Ct));
        Assert.Equal("TRADE_GOODS", await _host.ScalarAsync($"SELECT changes->'itemPostingGroup'->>'after' {audit} AND entity_type = 'ItemCompany'", Ct));
        Assert.Equal(id.ToString(), await _host.ScalarAsync($"SELECT changes->'itemId'->>'after' {audit} AND entity_type = 'ItemCompany'", Ct));
        Assert.Null(await _host.ScalarAsync($"SELECT changes->'itemNumber'->>'before' {audit} AND entity_type = 'Item'", Ct));

        Assert.Equal(2L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(id, await _host.ScalarAsync("SELECT aggregate_id FROM kernel.outbox_event WHERE event_type = 'ITEM_CREATED'", Ct));
        Assert.Equal(rowId, await _host.ScalarAsync("SELECT aggregate_id FROM kernel.outbox_event WHERE event_type = 'ITEM_COMPANY_CREATED'", Ct));
        Assert.Equal("TEST-001", await _host.ScalarAsync("SELECT payload->>'itemNumber' FROM kernel.outbox_event WHERE event_type = 'ITEM_CREATED'", Ct));
        Assert.Equal("0.125", await _host.ScalarAsync("SELECT payload->>'netWeightKg' FROM kernel.outbox_event WHERE event_type = 'ITEM_CREATED'", Ct));
        Assert.Equal(id.ToString(), await _host.ScalarAsync("SELECT payload->>'itemId' FROM kernel.outbox_event WHERE event_type = 'ITEM_COMPANY_CREATED'", Ct));
        Assert.Equal(rowId.ToString(), await _host.ScalarAsync("SELECT payload->'row'->>'id' FROM kernel.outbox_event WHERE event_type = 'ITEM_COMPANY_CREATED'", Ct));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(DISTINCT schema_version) FROM kernel.outbox_event", Ct));

        // R-API-01: the replay returns the stored result and writes nothing.
        Assert.Equal(200, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(reply.Body, replay.Body);
        Assert.Equal(1L, await ItemsCreatedAsync());
        Assert.Equal(2L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(2L, await _host.CountAsync("kernel.outbox_event", Ct));
    }

    [Fact]
    public async Task R_MD_05_the_item_number_is_stored_uppercase_and_a_duplicate_in_any_case_is_422_with_the_ruleId()
    {
        CommandReply first = await _host.PostAsync(Items, Item("test-002"), "item-2", cancellationToken: Ct);
        CommandReply duplicate = await _host.PostAsync(Items, Item("TEST-002"), "item-2-dup", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Items, Item("TEST-002"), "item-2-dup", cancellationToken: Ct);

        Assert.Equal(200, first.Status);
        Assert.Equal("TEST-002", first.Json!["itemNumber"]!.GetValue<string>());
        Assert.Equal("TEST-002", await _host.ScalarAsync("SELECT item_number FROM kernel.item WHERE item_number <> @orphan", Ct, ("orphan", MasterDataTestData.OrphanItemNumber)));
        Assert.Equal(422, duplicate.Status);
        Assert.Equal("application/problem+json", duplicate.MediaType);
        Assert.Equal("item_number_duplicate", duplicate.Code);
        Assert.Equal("R-MD-05", duplicate.RuleId);
        Assert.Equal(1L, await ItemsCreatedAsync());
        Assert.Equal(2L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = 'ITEM.CREATE'", Ct));

        // The rejection is the key's result (R-API-03).
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal("item_number_duplicate", replay.Code);
    }

    [Theory]
    [InlineData("unit", "base_unit_unknown")]
    [InlineData("group", "item_posting_group_unknown")]
    [InlineData("warehouse-unknown", "default_warehouse_unknown")]
    [InlineData("warehouse-other", "default_warehouse_other_company")]
    public async Task An_unknown_reference_is_400_with_its_own_code_no_ruleId_and_is_stored_as_the_key_result(string which, string code)
    {
        object body = which switch
        {
            "unit" => Item("TEST-400", unit: "XXX"),
            "group" => Item("TEST-400", group: "NOPE"),
            "warehouse-unknown" => Item("TEST-400", warehouse: Guid.NewGuid()),
            _ => Item("TEST-400", warehouse: _data!.OtherWarehouse),
        };
        string key = "item-400-" + which;

        CommandReply reply = await _host.PostAsync(Items, body, key, cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Items, body, key, cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Equal(code, reply.Code);
        Assert.Null(reply.RuleId);
        Assert.Equal(0L, await ItemsCreatedAsync());
        Assert.Equal(0L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(400, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(code, replay.Code);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = @key", Ct, ("key", key)));
    }

    [Theory]
    [InlineData("multiple", "salesMultiple")]
    [InlineData("minimum", "minOrderQty")]
    public async Task R_INV_02_a_sales_multiple_or_minimum_order_quantity_finer_than_the_unit_is_422_with_code_quantity_precision_and_is_stored_as_the_key_result(string which, string field)
    {
        // H87 (pieces) has 0 decimals (Build Brief, Errors: a value that breaks a rule tied to master data is a 422 with that rule).
        object body = which == "multiple" ? Item("TEST-422", salesMultiple: "1.5") : Item("TEST-422", minOrderQty: "0.25");
        string key = "item-422-" + which;

        CommandReply reply = await _host.PostAsync(Items, body, key, cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Items, body, key, cancellationToken: Ct);

        Assert.Equal(422, reply.Status);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Equal("quantity_precision", reply.Code);
        Assert.Equal("R-INV-02", reply.RuleId);
        Assert.Equal(field, reply.Json!["errors"]![0]!["details"]!["field"]!.GetValue<string>());
        Assert.Equal(0L, await ItemsCreatedAsync());
        Assert.Equal(0L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal("quantity_precision", replay.Code);
        Assert.Equal("R-INV-02", replay.RuleId);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = @key", Ct, ("key", key)));
    }

    [Fact]
    public async Task D_37_a_quantity_is_returned_at_the_units_decimals_and_reads_back_the_same()
    {
        CommandReply reply = await _host.PostAsync(Items, Item("TEST-KGM", unit: "KGM", salesMultiple: "0.25", minOrderQty: "2.5000"), "item-kgm", cancellationToken: Ct);
        CommandReply read = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/{reply.Json!["id"]!.GetValue<Guid>()}", cancellationToken: Ct);

        // KGM has 3 decimals: quantities read and return at that scale however they were entered (D-37, R-INV-02).
        Assert.Equal(200, reply.Status);
        Assert.Equal("0.250", reply.Json["salesMultiple"]!.GetValue<string>());
        Assert.Equal("2.500", reply.Json["minOrderQty"]!.GetValue<string>());
        Assert.Equal("0.125", reply.Json["netWeightKg"]!.GetValue<string>());
        Assert.Equal(reply.Body, read.Body);
    }

    [Fact]
    public async Task A_shape_error_is_400_before_the_transaction_and_is_not_stored_so_the_corrected_retry_runs()
    {
        var invalid = new { companyId = Company, itemNumber = "TEST-003", description = " ", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage", netWeightKg = "-1" };

        CommandReply reply = await _host.PostAsync(Items, invalid, "item-3", cancellationToken: Ct);
        long storedAfterRejection = await _host.CountAsync("kernel.idempotency_record", Ct);
        CommandReply retry = await _host.PostAsync(Items, Item("TEST-003"), "item-3", cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Null(reply.RuleId);
        Assert.Equal(["description_required", "net_weight_kg_negative"], reply.Json!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).Order());
        Assert.Equal(0L, storedAfterRejection);
        Assert.Equal(200, retry.Status);
        Assert.False(retry.Replayed);
    }

    [Fact]
    public async Task An_unknown_costing_method_is_refused_at_binding_with_400()
    {
        CommandReply reply = await _host.PostAsync(Items, $$"""{"companyId": "{{Company}}", "itemNumber": "TEST-FIFO", "description": "Fifo", "itemType": "Stocked", "baseUnit": "H87", "lotTracking": "None", "itemPostingGroup": "TRADE_GOODS", "taxClass": "Standard", "costingMethod": "Fifo"}""", "item-fifo", cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Equal(0L, await ItemsCreatedAsync());
        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    [Fact]
    public async Task R_SEC_01_the_preparer_without_ITEM_CREATE_gets_403_and_a_company_without_an_assignment_is_out_of_scope()
    {
        // Step 5 brief, acceptance 6: the preparer prepares adjustments and reads items, but does not create them.
        CommandReply forbidden = await _host.PostAsync(Items, Item("TEST-403"), "item-403", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        CommandReply outOfScope = await _host.PostAsync(Items, new { companyId = _data!.OtherCompany, itemNumber = "TEST-SE02", description = "Other", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "item-se02", cancellationToken: Ct);

        Assert.Equal(403, forbidden.Status);
        Assert.Equal("permission_missing", forbidden.Code);
        Assert.Equal("R-SEC-01", forbidden.RuleId);
        Assert.Equal(403, outOfScope.Status);
        Assert.Equal("out_of_scope", outOfScope.Code);
        Assert.Equal("R-SEC-01", outOfScope.RuleId);
        Assert.Equal(0L, await ItemsCreatedAsync());
        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    [Fact]
    public async Task D_47_the_item_is_read_by_id_and_by_number_in_the_company_of_the_route_403_before_404()
    {
        CommandReply created = await _host.PostAsync(Items, Item("TEST-READ"), "item-read", cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();

        CommandReply byId = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/{id}", cancellationToken: Ct);
        CommandReply byNumber = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/by-number/test-read", cancellationToken: Ct);
        CommandReply preparer = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/{id}", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        CommandReply noPermission = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/{id}", subject: _users!.PosterOnlySubject, cancellationToken: Ct);
        CommandReply otherCompany = await _host.GetAsync($"/v1/kernel/companies/{_data!.OtherCompany}/items/{id}", cancellationToken: Ct);
        CommandReply unknownCompany = await _host.GetAsync($"/v1/kernel/companies/{Guid.NewGuid()}/items/{id}", cancellationToken: Ct);
        CommandReply noRowById = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/{_data!.OrphanItem}", cancellationToken: Ct);
        CommandReply noRowByNumber = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/by-number/{MasterDataTestData.OrphanItemNumber}", cancellationToken: Ct);
        CommandReply unknownId = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/{Guid.NewGuid()}", cancellationToken: Ct);
        CommandReply anonymous = await _host.GetAsync($"/v1/kernel/companies/{Company}/items/{id}", token: "not-a-token", cancellationToken: Ct);

        Assert.Equal(200, byId.Status);
        Assert.Equal(created.Body, byId.Body);
        Assert.Equal("\"1\"", byId.ETag);
        Assert.Equal(200, byNumber.Status);
        Assert.Equal(created.Body, byNumber.Body);
        Assert.Equal(200, preparer.Status);
        Assert.Equal(403, noPermission.Status);
        Assert.Equal("permission_missing", noPermission.Code);
        Assert.Equal("R-SEC-01", noPermission.RuleId);
        Assert.Equal(403, otherCompany.Status);
        Assert.Equal("out_of_scope", otherCompany.Code);
        Assert.Equal(403, unknownCompany.Status);
        Assert.Equal("out_of_scope", unknownCompany.Code);
        Assert.Equal(404, noRowById.Status);
        Assert.Equal("item_not_found", noRowById.Code);
        Assert.Equal(404, noRowByNumber.Status);
        Assert.Equal("item_not_found", noRowByNumber.Code);
        Assert.Equal(404, unknownId.Status);
        Assert.Equal(401, anonymous.Status);
    }
}
