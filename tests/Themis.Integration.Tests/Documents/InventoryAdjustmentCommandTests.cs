using System.Net;
using System.Text.Json.Nodes;
using Themis.Integration.Tests.Identity;
using Themis.Integration.Tests.MasterData;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Documents;

/// <summary>
/// The inventory adjustment draft commands and read end to end through the pipeline (step 7 brief): CREATE writes
/// the document, its extension and its lines with one audit event per row and one outbox event carrying the full
/// envelope (R-AUD-01, D-44, D-50); UPDATE carries the whole draft and matches lines by number (insert, change,
/// cancel, reopen; nothing deleted) on the current version (R-DOC-02, R-API-02); DISCARD keeps the row (R-DOC-06)
/// and a transition the registry does not list is R-DOC-01; unknown references and another company's rows are
/// 400s stored as the key's result, a quantity finer than the unit is R-INV-02 (422); the read takes its scope from
/// the route, 403 before 404 (D-47); the permission tests of step 4 run against the real command (R-SEC-01).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class InventoryAdjustmentCommandTests : IAsyncLifetime
{
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string Audit = "FROM kernel.audit_event WHERE command_name = @command AND entity_id = @id AND entity_type = @entity";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private IdentityTestData? _users;
    private MasterDataTestData? _data;
    private Guid _bolt;
    private Guid _wire;

    public InventoryAdjustmentCommandTests(PostgresFixture db)
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
            _bolt = await CreateItemAsync("IA-BOLT", "H87");
            _wire = await CreateItemAsync("IA-WIRE", "KGM");
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The lines reference the items' company rows, so the drafts go before the test data's rows.
        await _db.TruncateAsync(CancellationToken.None);
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

    private async Task<Guid> CreateItemAsync(string number, string unit)
    {
        CommandReply reply = await _host.PostAsync("/v1/kernel/items", new { companyId = Company, itemNumber = number, description = $"Item {number}", itemType = "Stocked", baseUnit = unit, lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "item-" + number, cancellationToken: Ct);
        Assert.Equal(200, reply.Status);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private object Line(int number, Guid? item = null, string quantity = "10", Guid? warehouse = null, string stockStatus = "Available", string? enteredUnitCost = null) =>
        new { lineNumber = number, itemId = item ?? _bolt, quantity, warehouseId = warehouse, stockStatus, enteredUnitCost };

    private static object Draft(string? reasonCode = "CountDifference", Guid? warehouse = null, string? externalReference = null, string? postingDate = null, params object[] lines) =>
        new { companyId = Company, documentDate = "2026-06-15", reasonCode, warehouseId = warehouse, externalReference, postingDate, lines };

    private static object Update(string? reasonCode = "CountDifference", Guid? warehouse = null, string? externalReference = null, string documentDate = "2026-06-15", params object[] lines) =>
        new { documentDate, reasonCode, warehouseId = warehouse, externalReference, lines };

    private async Task<CommandReply> CreateAsync(string key, params object[] lines) =>
        await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, lines: lines), key, cancellationToken: Ct);

    /// <summary>The outbox events of inventory adjustments; the items the setup created have ITEM events of their own.</summary>
    private async Task<long> DocumentEventsAsync() => (long)(await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE aggregate_type = 'INVENTORY_ADJUSTMENT'", Ct))!;

    private Task<object?> AuditAsync(string select, string command, Guid id, string entity, string where = "") =>
        _host.ScalarAsync($"SELECT {select} {Audit} {where}", Ct, ("command", command), ("id", id), ("entity", entity));

    [Fact]
    public async Task CREATE_writes_the_draft_its_extension_and_its_lines_with_one_audit_event_per_row_and_one_outbox_event_with_the_full_envelope()
    {
        CommandReply reply = await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, externalReference: " Count 2026-06 ", lines: [Line(1, quantity: "10", enteredUnitCost: "2.50"), Line(2, _wire, "-0.250", stockStatus: "Blocked")]), "ia-1", correlationId: "corr-ia-1", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, externalReference: " Count 2026-06 ", lines: [Line(1, quantity: "10", enteredUnitCost: "2.50"), Line(2, _wire, "-0.250", stockStatus: "Blocked")]), "ia-1", cancellationToken: Ct);

        Assert.Equal(200, reply.Status);
        Assert.Equal("\"1\"", reply.ETag);
        JsonNode json = reply.Json!;
        Guid id = json["id"]!.GetValue<Guid>();
        Assert.Equal(Company, json["companyId"]!.GetValue<Guid>());
        Assert.Equal("INVENTORY_ADJUSTMENT", json["documentType"]!.GetValue<string>());
        Assert.Equal("Draft", json["status"]!.GetValue<string>());
        Assert.Null(json["documentNumber"]);
        Assert.Equal(id.ToString(), json["draftReference"]!.GetValue<string>());
        Assert.Equal("2026-06-15", json["documentDate"]!.GetValue<string>());
        Assert.Null(json["postingDate"]);
        // The currency is the company's base currency, set by the system; the reference is trimmed; the lines take the header's warehouse when they name none.
        Assert.Equal("SEK", json["currency"]!.GetValue<string>());
        Assert.Equal("Count 2026-06", json["externalReference"]!.GetValue<string>());
        Assert.Equal("CountDifference", json["reasonCode"]!.GetValue<string>());
        Assert.Equal(Warehouse, json["warehouseId"]!.GetValue<Guid>());
        Assert.Equal(1, json["version"]!.GetValue<long>());
        JsonArray lines = json["lines"]!.AsArray();
        Assert.Equal(2, lines.Count);
        Guid line1 = lines[0]!["id"]!.GetValue<Guid>();
        Assert.Equal(1, lines[0]!["lineNumber"]!.GetValue<int>());
        Assert.Equal("Open", lines[0]!["lineStatus"]!.GetValue<string>());
        Assert.Equal(_bolt, lines[0]!["itemId"]!.GetValue<Guid>());
        Assert.Equal("Item IA-BOLT", lines[0]!["description"]!.GetValue<string>());
        Assert.Equal("10", lines[0]!["quantity"]!.GetValue<string>());
        Assert.Equal("H87", lines[0]!["unitCode"]!.GetValue<string>());
        Assert.Equal("10", lines[0]!["quantityBase"]!.GetValue<string>());
        Assert.Equal(Warehouse, lines[0]!["warehouseId"]!.GetValue<Guid>());
        Assert.Equal("2.5", lines[0]!["enteredUnitCost"]!.GetValue<string>());
        Assert.Null(lines[0]!["unitCost"]);
        Assert.Null(lines[0]!["costAmount"]);
        Assert.Equal("-0.250", lines[1]!["quantity"]!.GetValue<string>());
        Assert.Equal("KGM", lines[1]!["unitCode"]!.GetValue<string>());
        Assert.Equal("Blocked", lines[1]!["stockStatus"]!.GetValue<string>());

        Assert.Equal(1L, await _host.CountAsync("kernel.document", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.inventory_adjustment", Ct));
        Assert.Equal(2L, await _host.CountAsync("kernel.document_line", Ct));
        Assert.Equal(id.ToString(), await _host.ScalarAsync("SELECT draft_reference FROM kernel.document", Ct));
        Assert.Equal("CountDifference", await _host.ScalarAsync("SELECT reason_code FROM kernel.inventory_adjustment", Ct));
        Assert.Equal(Company, await _host.ScalarAsync("SELECT DISTINCT company_id FROM kernel.document_line", Ct));
        Assert.Equal("Item", await _host.ScalarAsync("SELECT DISTINCT line_type FROM kernel.document_line", Ct));

        // R-AUD-01: one audit event per row written: the document, the extension and each line.
        Assert.Equal(4L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = 'INVENTORY_ADJUSTMENT.CREATE'", Ct));
        Assert.Equal("Document,InventoryAdjustment", await _host.ScalarAsync("SELECT string_agg(entity_type, ',' ORDER BY entity_type) FROM kernel.audit_event WHERE command_name = 'INVENTORY_ADJUSTMENT.CREATE' AND entity_id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await AuditAsync("count(*)", "INVENTORY_ADJUSTMENT.CREATE", line1, "DocumentLine"));
        Assert.Equal("Draft", await AuditAsync("changes->'status'->>'after'", "INVENTORY_ADJUSTMENT.CREATE", id, "Document"));
        Assert.Equal("CountDifference", await AuditAsync("changes->'reasonCode'->>'after'", "INVENTORY_ADJUSTMENT.CREATE", id, "InventoryAdjustment"));
        Assert.Equal("10", await AuditAsync("changes->'quantity'->>'after'", "INVENTORY_ADJUSTMENT.CREATE", line1, "DocumentLine"));
        Assert.Equal("ia-1", await _host.ScalarAsync("SELECT DISTINCT idempotency_key FROM kernel.audit_event WHERE command_name = 'INVENTORY_ADJUSTMENT.CREATE'", Ct));

        // D-44 and D-50: one outbox event for the document, named by the registry, with every envelope column filled (the two items of the setup have theirs).
        Assert.Equal(1L, await DocumentEventsAsync());
        const string outbox = "FROM kernel.outbox_event WHERE event_type = 'INVENTORY_ADJUSTMENT_CREATED'";
        Assert.Equal(id, await _host.ScalarAsync($"SELECT aggregate_id {outbox}", Ct));
        Assert.Equal("INVENTORY_ADJUSTMENT", await _host.ScalarAsync($"SELECT aggregate_type {outbox}", Ct));
        Assert.Equal(Company, await _host.ScalarAsync($"SELECT company_id {outbox}", Ct));
        Assert.Equal("INVENTORY_ADJUSTMENT.CREATE", await _host.ScalarAsync($"SELECT command_name {outbox}", Ct));
        Assert.Equal("corr-ia-1", await _host.ScalarAsync($"SELECT correlation_id {outbox}", Ct));
        Assert.Equal(1, await _host.ScalarAsync($"SELECT schema_version {outbox}", Ct));
        Assert.NotNull(await _host.ScalarAsync($"SELECT occurred_at {outbox}", Ct));
        Assert.Null(await _host.ScalarAsync($"SELECT published_at {outbox}", Ct));
        Assert.Equal(id.ToString(), await _host.ScalarAsync($"SELECT payload->>'id' {outbox}", Ct));
        Assert.Equal("Draft", await _host.ScalarAsync($"SELECT payload->'changes'->'status'->>'after' {outbox}", Ct));
        Assert.Equal("CountDifference", await _host.ScalarAsync($"SELECT payload->'changes'->'reasonCode'->>'after' {outbox}", Ct));
        Assert.Equal(2, await _host.ScalarAsync($"SELECT jsonb_array_length(payload->'lines') {outbox}", Ct));
        Assert.Equal("Inserted", await _host.ScalarAsync($"SELECT payload->'lines'->0->>'change' {outbox}", Ct));
        // The idempotency key and the user never travel with an event.
        Assert.DoesNotContain("ia-1", (string)(await _host.ScalarAsync($"SELECT payload::text {outbox}", Ct))!, StringComparison.Ordinal);
        Assert.DoesNotContain(TestUsers.Clerk.ToString(), (string)(await _host.ScalarAsync($"SELECT payload::text {outbox}", Ct))!, StringComparison.OrdinalIgnoreCase);

        // R-API-01: the replay returns the stored result and writes nothing.
        Assert.Equal(200, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(reply.Body, replay.Body);
        Assert.Equal(1L, await _host.CountAsync("kernel.document", Ct));
        Assert.Equal(1L, await DocumentEventsAsync());
    }

    [Fact]
    public async Task UPDATE_carries_the_whole_draft_and_matches_lines_by_number_inserting_changing_cancelling_and_reopening_and_nothing_is_deleted()
    {
        CommandReply created = await CreateAsync("ia-upd-create", Line(1), Line(2, _wire, "1.5"));
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        Guid line1 = created.Json["lines"]![0]!["id"]!.GetValue<Guid>();
        Guid line2 = created.Json["lines"]![1]!["id"]!.GetValue<Guid>();

        // Line 1 changes, line 2 is left out (Cancelled), line 3 is new; the reason code stays, so the extension is untouched.
        CommandReply first = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: [Line(1, quantity: "12"), Line(3, _wire, "-2")]), "ia-upd-1", ifMatch: created.ETag, correlationId: "corr-upd-1", cancellationToken: Ct);

        Assert.Equal(200, first.Status);
        Assert.Equal("\"2\"", first.ETag);
        Assert.Equal(2, first.Json!["version"]!.GetValue<long>());
        Assert.Equal([1, 3], first.Json["lines"]!.AsArray().Select(line => line!["lineNumber"]!.GetValue<int>()));
        Assert.Equal("12", first.Json["lines"]![0]!["quantity"]!.GetValue<string>());
        Assert.Equal(line1, first.Json["lines"]![0]!["id"]!.GetValue<Guid>());
        Assert.Equal(2, first.Json["lines"]![0]!["version"]!.GetValue<long>());
        Guid line3 = first.Json["lines"]![1]!["id"]!.GetValue<Guid>();
        Assert.Equal(3L, await _host.CountAsync("kernel.document_line", Ct));
        Assert.Equal("Cancelled", await _host.ScalarAsync("SELECT line_status FROM kernel.document_line WHERE id = @id", Ct, ("id", line2)));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM kernel.document_line WHERE id = @id", Ct, ("id", line2)));
        Assert.Equal(1.5m, await _host.ScalarAsync("SELECT quantity FROM kernel.document_line WHERE id = @id", Ct, ("id", line2)));

        // R-AUD-01: the document and each changed line, not the extension (reason code unchanged); D-44: one event with the line changes.
        Assert.Equal(4L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = 'INVENTORY_ADJUSTMENT.UPDATE' AND correlation_id = 'corr-upd-1'", Ct));
        Assert.Equal(1L, await AuditAsync("count(*)", "INVENTORY_ADJUSTMENT.UPDATE", id, "Document"));
        Assert.Equal(0L, await AuditAsync("count(*)", "INVENTORY_ADJUSTMENT.UPDATE", id, "InventoryAdjustment"));
        Assert.Equal(("1", "2"), ((string?)await AuditAsync("changes->'version'->>'before'", "INVENTORY_ADJUSTMENT.UPDATE", id, "Document"), (string?)await AuditAsync("changes->'version'->>'after'", "INVENTORY_ADJUSTMENT.UPDATE", id, "Document")));
        Assert.Equal(("10", "12"), ((string?)await AuditAsync("changes->'quantity'->>'before'", "INVENTORY_ADJUSTMENT.UPDATE", line1, "DocumentLine"), (string?)await AuditAsync("changes->'quantity'->>'after'", "INVENTORY_ADJUSTMENT.UPDATE", line1, "DocumentLine")));
        Assert.Equal("Cancelled", await AuditAsync("changes->'lineStatus'->>'after'", "INVENTORY_ADJUSTMENT.UPDATE", line2, "DocumentLine"));
        Assert.Null(await AuditAsync("changes->'quantity'->>'before'", "INVENTORY_ADJUSTMENT.UPDATE", line3, "DocumentLine"));
        const string outbox = "FROM kernel.outbox_event WHERE event_type = 'INVENTORY_ADJUSTMENT_UPDATED' AND correlation_id = 'corr-upd-1'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {outbox}", Ct));
        Assert.Equal("INVENTORY_ADJUSTMENT.UPDATE", await _host.ScalarAsync($"SELECT command_name {outbox}", Ct));
        Assert.Equal(Company, await _host.ScalarAsync($"SELECT company_id {outbox}", Ct));
        Assert.Equal("Changed,Inserted,Cancelled", await _host.ScalarAsync($"SELECT string_agg(line->>'change', ',') FROM kernel.outbox_event, jsonb_array_elements(payload->'lines') line WHERE event_type = 'INVENTORY_ADJUSTMENT_UPDATED' AND correlation_id = 'corr-upd-1'", Ct));

        // Line 2 given again reopens it with the new values, line 3 is left out, line 1 is unchanged and stays at its version; the reason code change audits the extension.
        CommandReply second = await _host.PostAsync($"{Adjustments}/{id}/update", Update(reasonCode: "Scrap", warehouse: Warehouse, lines: [Line(1, quantity: "12"), Line(2, _wire, "4.000")]), "ia-upd-2", ifMatch: first.ETag, correlationId: "corr-upd-2", cancellationToken: Ct);

        Assert.Equal(200, second.Status);
        Assert.Equal("\"3\"", second.ETag);
        Assert.Equal("Scrap", second.Json!["reasonCode"]!.GetValue<string>());
        Assert.Equal([1, 2], second.Json["lines"]!.AsArray().Select(line => line!["lineNumber"]!.GetValue<int>()));
        Assert.Equal(2, second.Json["lines"]![0]!["version"]!.GetValue<long>());
        Assert.Equal(line2, second.Json["lines"]![1]!["id"]!.GetValue<Guid>());
        Assert.Equal("Open", second.Json["lines"]![1]!["lineStatus"]!.GetValue<string>());
        Assert.Equal("4.000", second.Json["lines"]![1]!["quantity"]!.GetValue<string>());
        Assert.Equal(3, second.Json["lines"]![1]!["version"]!.GetValue<long>());
        Assert.Equal(3L, await _host.CountAsync("kernel.document_line", Ct));
        Assert.Equal("Cancelled", await _host.ScalarAsync("SELECT line_status FROM kernel.document_line WHERE id = @id", Ct, ("id", line3)));
        Assert.Equal("Scrap", await _host.ScalarAsync("SELECT reason_code FROM kernel.inventory_adjustment WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM kernel.inventory_adjustment WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(4L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = 'INVENTORY_ADJUSTMENT.UPDATE' AND correlation_id = 'corr-upd-2'", Ct));
        Assert.Equal("Document,InventoryAdjustment", await _host.ScalarAsync("SELECT string_agg(entity_type, ',' ORDER BY entity_type) FROM kernel.audit_event WHERE correlation_id = 'corr-upd-2' AND entity_id = @id", Ct, ("id", id)));
        Assert.Equal(("CountDifference", "Scrap"), ((string?)await AuditAsync("changes->'reasonCode'->>'before'", "INVENTORY_ADJUSTMENT.UPDATE", id, "InventoryAdjustment"), (string?)await AuditAsync("changes->'reasonCode'->>'after'", "INVENTORY_ADJUSTMENT.UPDATE", id, "InventoryAdjustment")));
        Assert.Equal("Reopened", await _host.ScalarAsync("SELECT line->>'change' FROM kernel.outbox_event, jsonb_array_elements(payload->'lines') line WHERE correlation_id = 'corr-upd-2' AND (line->>'lineNumber')::int = 2", Ct));

        // The read shows the draft as the last command left it, Cancelled lines left out, the version as ETag.
        CommandReply read = await _host.GetAsync($"/v1/kernel/companies/{Company}/inventory-adjustments/{id}", cancellationToken: Ct);
        Assert.Equal(200, read.Status);
        Assert.Equal(second.Body, read.Body);
        Assert.Equal("\"3\"", read.ETag);
    }

    [Fact]
    public async Task A_line_without_a_warehouse_takes_the_headers_current_warehouse_on_every_write_and_the_stored_value_is_what_the_line_keeps()
    {
        // Owner's decision on step 7, point 2: the header's default is resolved on every write.
        Guid second = _data!.SecondWarehouse;
        CommandReply created = await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, lines: [Line(1), Line(2, _wire, "1.5", warehouse: second)]), "ia-wh-create", cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        Guid line1 = created.Json["lines"]![0]!["id"]!.GetValue<Guid>();

        Assert.Equal(200, created.Status);
        Assert.Equal(Warehouse, created.Json["lines"]![0]!["warehouseId"]!.GetValue<Guid>());
        Assert.Equal(second, created.Json["lines"]![1]!["warehouseId"]!.GetValue<Guid>());

        // The header moves to the second warehouse: line 1 (sent without a warehouse) follows it, line 2 keeps its own.
        CommandReply moved = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: second, lines: [Line(1), Line(2, _wire, "1.5", warehouse: Warehouse)]), "ia-wh-1", ifMatch: created.ETag, correlationId: "corr-wh-1", cancellationToken: Ct);

        Assert.Equal(200, moved.Status);
        Assert.Equal(second, moved.Json!["warehouseId"]!.GetValue<Guid>());
        Assert.Equal(second, moved.Json["lines"]![0]!["warehouseId"]!.GetValue<Guid>());
        Assert.Equal(Warehouse, moved.Json["lines"]![1]!["warehouseId"]!.GetValue<Guid>());
        // The stored line value is what POST will read (step 8).
        Assert.Equal(second, await _host.ScalarAsync("SELECT warehouse_id FROM kernel.document_line WHERE id = @id", Ct, ("id", line1)));
        Assert.Equal(("" + Warehouse, "" + second), ((string?)await AuditAsync("changes->'warehouseId'->>'before'", "INVENTORY_ADJUSTMENT.UPDATE", line1, "DocumentLine", "AND correlation_id = 'corr-wh-1'"), (string?)await AuditAsync("changes->'warehouseId'->>'after'", "INVENTORY_ADJUSTMENT.UPDATE", line1, "DocumentLine", "AND correlation_id = 'corr-wh-1'")));

        // A header without a warehouse: a line that names none is refused, one that names its own passes.
        CommandReply noHeader = await _host.PostAsync($"{Adjustments}/{id}/update", Update(lines: [Line(1), Line(2, _wire, "1.5", warehouse: Warehouse)]), "ia-wh-2", ifMatch: moved.ETag, cancellationToken: Ct);
        CommandReply explicitOnly = await _host.PostAsync($"{Adjustments}/{id}/update", Update(lines: [Line(1, warehouse: Warehouse), Line(2, _wire, "1.5", warehouse: Warehouse)]), "ia-wh-3", ifMatch: moved.ETag, cancellationToken: Ct);

        Assert.Equal(400, noHeader.Status);
        Assert.Equal("line_warehouse_required", noHeader.Code);
        Assert.Equal("1", noHeader.Json!["errors"]![0]!["details"]!["lineNumber"]!.GetValue<string>());
        Assert.Equal(200, explicitOnly.Status);
        Assert.Null(explicitOnly.Json!["warehouseId"]);
        Assert.Equal(Warehouse, await _host.ScalarAsync("SELECT warehouse_id FROM kernel.document_line WHERE id = @id", Ct, ("id", line1)));
    }

    [Fact]
    public async Task An_UPDATE_that_repeats_the_stored_draft_advances_the_version_with_one_document_audit_event_and_one_event_holding_only_the_version()
    {
        // Owner's decision on step 7, point 4.
        object[] lines = [Line(1, quantity: "10", enteredUnitCost: "2.50"), Line(2, _wire, "1.500", stockStatus: "Blocked")];
        CommandReply created = await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, externalReference: "Same", lines: lines), "ia-same-create", cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();

        CommandReply repeated = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, externalReference: "Same", lines: [Line(1, quantity: "10.0", enteredUnitCost: "2.5"), Line(2, _wire, "1.5", stockStatus: "Blocked")]), "ia-same-1", ifMatch: created.ETag, correlationId: "corr-same-1", cancellationToken: Ct);

        Assert.Equal(200, repeated.Status);
        Assert.Equal("\"2\"", repeated.ETag);
        Assert.Equal(2, repeated.Json!["version"]!.GetValue<long>());
        Assert.Equal(created.Json["lines"]!.ToJsonString(), repeated.Json["lines"]!.ToJsonString());
        // One audit event, for the document row, whose changed fields hold only the version; no line or extension event.
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE correlation_id = 'corr-same-1'", Ct));
        Assert.Equal("Document", await _host.ScalarAsync("SELECT entity_type FROM kernel.audit_event WHERE correlation_id = 'corr-same-1'", Ct));
        Assert.Equal("version", await _host.ScalarAsync("SELECT string_agg(key, ',') FROM kernel.audit_event, jsonb_object_keys(changes) key WHERE correlation_id = 'corr-same-1'", Ct));
        Assert.Equal(("1", "2"), ((string?)await AuditAsync("changes->'version'->>'before'", "INVENTORY_ADJUSTMENT.UPDATE", id, "Document"), (string?)await AuditAsync("changes->'version'->>'after'", "INVENTORY_ADJUSTMENT.UPDATE", id, "Document")));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT DISTINCT version FROM kernel.document_line WHERE document_id = @id", Ct, ("id", id)));
        // One INVENTORY_ADJUSTMENT_UPDATED event whose changed fields hold only the version and no lines.
        const string outbox = "FROM kernel.outbox_event WHERE correlation_id = 'corr-same-1'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {outbox}", Ct));
        Assert.Equal("INVENTORY_ADJUSTMENT_UPDATED", await _host.ScalarAsync($"SELECT event_type {outbox}", Ct));
        Assert.Equal("version", await _host.ScalarAsync($"SELECT string_agg(key, ',') FROM kernel.outbox_event, jsonb_object_keys(payload->'changes') key WHERE correlation_id = 'corr-same-1'", Ct));
        Assert.Equal(0, await _host.ScalarAsync($"SELECT jsonb_array_length(payload->'lines') {outbox}", Ct));
        Assert.Equal(2, await _host.ScalarAsync($"SELECT (payload->>'version')::int {outbox}", Ct));
    }

    [Fact]
    public async Task R_API_02_UPDATE_and_DISCARD_need_the_current_version_from_If_Match_and_a_stale_or_missing_one_is_not_stored()
    {
        CommandReply created = await CreateAsync("ia-ver-create", Line(1));
        Guid id = created.Json!["id"]!.GetValue<Guid>();

        CommandReply updated = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "3")), "ia-ver-1", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply stale = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "3")), "ia-ver-2", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply missing = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "3")), "ia-ver-3", ifMatch: null, cancellationToken: Ct);
        CommandReply weak = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "4")), "ia-ver-4", ifMatch: "W/\"2\"", cancellationToken: Ct);
        CommandReply staleDiscard = await _host.PostAsync($"{Adjustments}/{id}/discard", null, "ia-ver-5", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply missingDiscard = await _host.PostAsync($"{Adjustments}/{id}/discard", null, "ia-ver-6", ifMatch: null, cancellationToken: Ct);

        Assert.Equal(200, updated.Status);
        Assert.Equal("\"2\"", updated.ETag);
        Assert.Equal(412, stale.Status);
        Assert.Equal("version_stale", stale.Code);
        Assert.Equal("R-API-02", stale.RuleId);
        Assert.Equal(428, missing.Status);
        Assert.Equal("if_match_required", missing.Code);
        Assert.Equal("R-API-02", missing.RuleId);
        Assert.Equal(200, weak.Status);
        Assert.Equal(3, weak.Json!["version"]!.GetValue<long>());
        Assert.Equal(412, staleDiscard.Status);
        Assert.Equal("version_stale", staleDiscard.Code);
        Assert.Equal(428, missingDiscard.Status);
        // Not stored, so the retry with the fresh version runs (owner's condition on pull request 5).
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key IN ('ia-ver-2', 'ia-ver-3', 'ia-ver-5', 'ia-ver-6')", Ct));
        CommandReply retry = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "3")), "ia-ver-2", ifMatch: weak.ETag, cancellationToken: Ct);
        Assert.Equal(200, retry.Status);
        Assert.False(retry.Replayed);
        Assert.Equal("\"4\"", retry.ETag);
    }

    [Fact]
    public async Task R_API_04_the_fingerprint_includes_If_Match_and_R_API_01_keys_are_scoped_per_user_and_the_body_cannot_override_the_route_id_or_the_version()
    {
        CommandReply created = await CreateAsync("ia-fp-create", Line(1));
        Guid id = created.Json!["id"]!.GetValue<Guid>();

        CommandReply first = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "2")), "ia-fp-1", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "2")), "ia-fp-1", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply otherVersion = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "2")), "ia-fp-1", ifMatch: "\"2\"", cancellationToken: Ct);
        // Both users hold INVENTORY_ADJUSTMENT.UPDATE: the preparer's "ia-fp-1" is another key (R-API-01, per user).
        CommandReply preparer = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "5")), "ia-fp-1", ifMatch: "\"2\"", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        // RR-73: a body id that differs from the route is refused (400, not stored); a body version is dropped and If-Match decides.
        CommandReply overridingId = await _host.PostAsync($"{Adjustments}/{id}/update", new { documentDate = "2026-06-15", warehouseId = Warehouse, lines = new[] { Line(1) }, id = Guid.NewGuid(), expectedVersion = 99 }, "ia-fp-3", ifMatch: "\"3\"", cancellationToken: Ct);
        CommandReply overriding = await _host.PostAsync($"{Adjustments}/{id}/update", new { documentDate = "2026-06-15", warehouseId = Warehouse, lines = new[] { Line(1) }, expectedVersion = 99 }, "ia-fp-2", ifMatch: "\"3\"", cancellationToken: Ct);

        Assert.Equal(200, first.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(422, otherVersion.Status);
        Assert.Equal("idempotency_key_reused", otherVersion.Code);
        Assert.Equal("R-API-04", otherVersion.RuleId);
        Assert.Equal(200, preparer.Status);
        Assert.False(preparer.Replayed);
        Assert.Equal(3, preparer.Json!["version"]!.GetValue<long>());
        Assert.Equal(2L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'ia-fp-1'", Ct));
        Assert.Equal(400, overridingId.Status);
        Assert.Equal("route_body_mismatch", overridingId.Code);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'ia-fp-3'", Ct));
        Assert.Equal(200, overriding.Status);
        Assert.Equal(id, overriding.Json!["id"]!.GetValue<Guid>());
        Assert.Equal(4, overriding.Json["version"]!.GetValue<long>());
        Assert.Equal(TestUsers.Preparer, await _host.ScalarAsync("SELECT user_id FROM kernel.audit_event WHERE command_name = 'INVENTORY_ADJUSTMENT.UPDATE' AND entity_type = 'Document' AND changes->'version'->>'after' = '3'", Ct));
    }

    [Fact]
    public async Task R_DOC_06_DISCARD_keeps_the_row_and_R_DOC_02_an_UPDATE_and_R_DOC_01_a_second_DISCARD_on_the_Discarded_draft_are_422_and_stored()
    {
        CommandReply created = await CreateAsync("ia-discard-create", Line(1), Line(2, _wire, "0.5"));
        Guid id = created.Json!["id"]!.GetValue<Guid>();

        CommandReply discarded = await _host.PostAsync($"{Adjustments}/{id}/discard", null, "ia-discard-1", ifMatch: created.ETag, correlationId: "corr-discard-1", cancellationToken: Ct);
        CommandReply update = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "3")), "ia-discard-2", ifMatch: discarded.ETag, cancellationToken: Ct);
        CommandReply again = await _host.PostAsync($"{Adjustments}/{id}/discard", null, "ia-discard-3", ifMatch: discarded.ETag, cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync($"{Adjustments}/{id}/discard", null, "ia-discard-3", ifMatch: discarded.ETag, cancellationToken: Ct);
        CommandReply read = await _host.GetAsync($"/v1/kernel/companies/{Company}/inventory-adjustments/{id}", cancellationToken: Ct);

        Assert.Equal(200, discarded.Status);
        Assert.Equal("\"2\"", discarded.ETag);
        Assert.Equal("Discarded", discarded.Json!["status"]!.GetValue<string>());
        Assert.Equal(2, discarded.Json["lines"]!.AsArray().Count);
        // R-DOC-06: nothing is deleted; the row, its extension and its lines stay.
        Assert.Equal(1L, await _host.CountAsync("kernel.document", Ct));
        Assert.Equal(2L, await _host.CountAsync("kernel.document_line", Ct));
        Assert.Equal("Discarded", await _host.ScalarAsync("SELECT status FROM kernel.document WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = 'INVENTORY_ADJUSTMENT.DISCARD'", Ct));
        Assert.Equal(("Draft", "Discarded"), ((string?)await AuditAsync("changes->'status'->>'before'", "INVENTORY_ADJUSTMENT.DISCARD", id, "Document"), (string?)await AuditAsync("changes->'status'->>'after'", "INVENTORY_ADJUSTMENT.DISCARD", id, "Document")));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'INVENTORY_ADJUSTMENT_DISCARDED' AND command_name = 'INVENTORY_ADJUSTMENT.DISCARD' AND correlation_id = 'corr-discard-1' AND company_id = @company AND aggregate_id = @id", Ct, ("company", Company), ("id", id)));

        // R-DOC-02 before R-DOC-01: an UPDATE on a Discarded draft is refused as "not a Draft"; a second DISCARD finds no transition.
        Assert.Equal(422, update.Status);
        Assert.Equal("document_not_draft", update.Code);
        Assert.Equal("R-DOC-02", update.RuleId);
        Assert.Equal(422, again.Status);
        Assert.Equal("transition_not_allowed", again.Code);
        Assert.Equal("R-DOC-01", again.RuleId);
        Assert.True(replay.Replayed);
        Assert.Equal(422, replay.Status);
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM kernel.document WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(200, read.Status);
        Assert.Equal("Discarded", read.Json!["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("item-unknown", "line_item_unknown", 400, null)]
    [InlineData("item-no-company-row", "line_item_unknown", 400, null)]
    [InlineData("line-warehouse-unknown", "line_warehouse_unknown", 400, null)]
    [InlineData("line-warehouse-other", "line_warehouse_other_company", 400, null)]
    [InlineData("header-warehouse-other", "warehouse_other_company", 400, null)]
    [InlineData("header-warehouse-unknown", "warehouse_unknown", 400, null)]
    [InlineData("no-warehouse", "line_warehouse_required", 400, null)]
    [InlineData("precision", "quantity_precision", 422, "R-INV-02")]
    public async Task An_unknown_or_other_company_reference_is_400_with_its_own_code_and_a_quantity_finer_than_the_unit_is_422_with_R_INV_02_both_stored_as_the_key_result(string which, string code, int status, string? ruleId)
    {
        object body = which switch
        {
            "item-unknown" => Draft(warehouse: Warehouse, lines: Line(1, Guid.NewGuid())),
            "item-no-company-row" => Draft(warehouse: Warehouse, lines: Line(1, _data!.OrphanItem)),
            "line-warehouse-unknown" => Draft(warehouse: Warehouse, lines: Line(1, warehouse: Guid.NewGuid())),
            "line-warehouse-other" => Draft(warehouse: Warehouse, lines: Line(1, warehouse: _data!.OtherWarehouse)),
            "header-warehouse-other" => Draft(warehouse: _data!.OtherWarehouse, lines: Line(1)),
            "header-warehouse-unknown" => Draft(warehouse: Guid.NewGuid(), lines: Line(1)),
            "no-warehouse" => Draft(lines: Line(1)),
            // H87 (pieces) has 0 decimals: 1.5 pieces is finer than the unit allows (Build Brief, Errors).
            _ => Draft(warehouse: Warehouse, lines: Line(1, quantity: "1.5")),
        };
        string key = "ia-ref-" + which;

        CommandReply reply = await _host.PostAsync(Adjustments, body, key, cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Adjustments, body, key, cancellationToken: Ct);

        Assert.Equal(status, reply.Status);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Equal(code, reply.Code);
        Assert.Equal(ruleId, reply.RuleId);
        if (which.StartsWith("header", StringComparison.Ordinal))
        {
            Assert.Null(reply.Json!["errors"]![0]!["details"]!["lineNumber"]);
        }
        else
        {
            Assert.Equal("1", reply.Json!["errors"]![0]!["details"]!["lineNumber"]!.GetValue<string>());
        }
        Assert.Equal(0L, await _host.CountAsync("kernel.document", Ct));
        Assert.Equal(0L, await DocumentEventsAsync());
        Assert.Equal(status, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = @key", Ct, ("key", key)));
    }

    [Theory]
    [InlineData("zero", "line_quantity_zero")]
    [InlineData("duplicate", "line_number_duplicate")]
    [InlineData("not-positive", "line_number_not_positive")]
    public async Task A_zero_quantity_or_a_duplicate_or_non_positive_line_number_is_400_before_the_transaction_and_not_stored(string which, string code)
    {
        object[] lines = which switch
        {
            "zero" => [Line(1, quantity: "0")],
            "duplicate" => [Line(1), Line(1, _wire, "2")],
            _ => [Line(0)],
        };

        CommandReply reply = await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, lines: lines), "ia-shape-" + which, cancellationToken: Ct);
        CommandReply retry = await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, lines: Line(1)), "ia-shape-" + which, cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Equal(code, reply.Code);
        Assert.Null(reply.RuleId);
        Assert.Equal(200, retry.Status);
        Assert.False(retry.Replayed);
    }

    [Fact]
    public async Task R_SEC_01_the_preparer_creates_updates_and_discards_drafts_a_user_without_the_permission_gets_403_and_another_company_is_out_of_scope()
    {
        CommandReply created = await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, lines: Line(1)), "ia-prep-1", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        CommandReply updated = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1, quantity: "7")), "ia-prep-2", ifMatch: created.ETag, subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        CommandReply posterOnlyCreate = await _host.PostAsync(Adjustments, Draft(warehouse: Warehouse, lines: Line(1)), "ia-poster-1", subject: _users!.PosterOnlySubject, cancellationToken: Ct);
        CommandReply posterOnlyUpdate = await _host.PostAsync($"{Adjustments}/{id}/update", Update(warehouse: Warehouse, lines: Line(1)), "ia-poster-2", ifMatch: updated.ETag, subject: _users.PosterOnlySubject, cancellationToken: Ct);
        CommandReply otherCompany = await _host.PostAsync(Adjustments, new { companyId = _data!.OtherCompany, documentDate = "2026-06-15", warehouseId = _data.OtherWarehouse, lines = Array.Empty<object>() }, "ia-se02", cancellationToken: Ct);
        CommandReply anonymous = await _host.PostAnonymousAsync(Adjustments, Draft(warehouse: Warehouse), "ia-anon", Ct);
        CommandReply discarded = await _host.PostAsync($"{Adjustments}/{id}/discard", null, "ia-prep-3", ifMatch: updated.ETag, subject: TestUsers.PreparerSubject, cancellationToken: Ct);

        Assert.Equal(200, created.Status);
        Assert.Equal(TestUsers.Preparer, await _host.ScalarAsync("SELECT created_by FROM kernel.document WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(200, updated.Status);
        Assert.Equal(403, posterOnlyCreate.Status);
        Assert.Equal("permission_missing", posterOnlyCreate.Code);
        Assert.Equal("R-SEC-01", posterOnlyCreate.RuleId);
        Assert.Equal(403, posterOnlyUpdate.Status);
        Assert.Equal("permission_missing", posterOnlyUpdate.Code);
        Assert.Equal(403, otherCompany.Status);
        Assert.Equal("out_of_scope", otherCompany.Code);
        Assert.Equal((int)HttpStatusCode.Unauthorized, anonymous.Status);
        Assert.Equal(200, discarded.Status);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key IN ('ia-poster-1', 'ia-poster-2', 'ia-se02')", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.document", Ct));
    }

    [Fact]
    public async Task D_47_the_read_takes_its_scope_from_the_route_403_before_404_and_a_missing_draft_is_404_on_every_command()
    {
        CommandReply created = await CreateAsync("ia-read-create", Line(1));
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        string path = $"/v1/kernel/companies/{Company}/inventory-adjustments/{id}";

        CommandReply clerk = await _host.GetAsync(path, cancellationToken: Ct);
        CommandReply preparer = await _host.GetAsync(path, subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        CommandReply noPermission = await _host.GetAsync(path, subject: _users!.PosterOnlySubject, cancellationToken: Ct);
        CommandReply otherCompany = await _host.GetAsync($"/v1/kernel/companies/{_data!.OtherCompany}/inventory-adjustments/{id}", cancellationToken: Ct);
        CommandReply unknownCompany = await _host.GetAsync($"/v1/kernel/companies/{Guid.NewGuid()}/inventory-adjustments/{id}", cancellationToken: Ct);
        CommandReply unknownId = await _host.GetAsync($"/v1/kernel/companies/{Company}/inventory-adjustments/{Guid.NewGuid()}", cancellationToken: Ct);
        CommandReply anonymous = await _host.GetAsync(path, token: "not-a-token", cancellationToken: Ct);
        CommandReply missingUpdate = await _host.PostAsync($"{Adjustments}/{Guid.NewGuid()}/update", Update(warehouse: Warehouse, lines: Line(1)), "ia-missing-1", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply missingDiscard = await _host.PostAsync($"{Adjustments}/{Guid.NewGuid()}/discard", null, "ia-missing-2", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(200, clerk.Status);
        Assert.Equal(created.Body, clerk.Body);
        Assert.Equal("\"1\"", clerk.ETag);
        Assert.Equal(200, preparer.Status);
        Assert.Equal(403, noPermission.Status);
        Assert.Equal("permission_missing", noPermission.Code);
        Assert.Equal(403, otherCompany.Status);
        Assert.Equal("out_of_scope", otherCompany.Code);
        Assert.Equal(403, unknownCompany.Status);
        Assert.Equal(404, unknownId.Status);
        Assert.Equal("inventory_adjustment_not_found", unknownId.Code);
        Assert.Equal(401, anonymous.Status);
        Assert.Equal(404, missingUpdate.Status);
        Assert.Equal("inventory_adjustment_not_found", missingUpdate.Code);
        Assert.Equal(404, missingDiscard.Status);
    }

    [Fact]
    public async Task R_API_02_the_OpenAPI_document_describes_If_Match_on_the_update_route_and_the_line_decimals_as_strings()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        JsonNode root = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        JsonNode update = root["paths"]!["/v1/kernel/inventory-adjustments/{id}/update"]!["post"]!;
        JsonArray parameters = update["parameters"]!.AsArray();
        Assert.Contains(parameters, p => p!["name"]!.GetValue<string>() == "Idempotency-Key" && p["in"]!.GetValue<string>() == "header" && p["required"]!.GetValue<bool>());
        Assert.Contains(parameters, p => p!["name"]!.GetValue<string>() == "If-Match" && p["in"]!.GetValue<string>() == "header" && p["required"]!.GetValue<bool>());
        Assert.Contains(parameters, p => p!["name"]!.GetValue<string>() == "id" && p["in"]!.GetValue<string>() == "path");
        Assert.NotNull(update["responses"]!["200"]!["headers"]!["ETag"]);
        Assert.DoesNotContain(root["paths"]!["/v1/kernel/inventory-adjustments"]!["post"]!["parameters"]!.AsArray(), p => p!["name"]!.GetValue<string>() == "If-Match");
        JsonNode line = root["components"]!["schemas"]!["InventoryAdjustmentLineView"]!["properties"]!;
        Assert.Equal("string", line["quantity"]!["type"]!.GetValue<string>());
        Assert.Equal("decimal", line["quantity"]!["format"]!.GetValue<string>());
        Assert.Equal("integer", line["lineNumber"]!["type"]!.GetValue<string>());
    }
}
