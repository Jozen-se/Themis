using System.Text.Json;
using Microsoft.Extensions.Logging;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// The input fixes of milestone 2 step 2a, each through the real pipeline over HTTP: a zero or missing companyId
/// reaches the validators and gets 400 company_required instead of 500 (RR-60), every parse failure of the JSON
/// converters is a 400 (RR-61), and an unhandled exception answers 500 with the correlation id in its body and log
/// line (RR-72). Nothing here changes a behaviour the milestone 1 or step 2 tests prove.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class InterimFixTests : IAsyncLifetime
{
    private const string Counters = "/v1/fixture/counters";
    private const string Items = "/v1/kernel/items";
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string June = "2026-06-15";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public InterimFixTests(PostgresFixture db)
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
        await _db.TruncateAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    // ---- RR-60 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_60_ITEM_CREATE_without_a_companyId_is_400_company_required()
    {
        CommandReply reply = await _host.PostAsync(Items, ItemFields(), "rr60-item-missing", cancellationToken: Ct);

        AssertCompanyRequired(reply);
    }

    [Fact]
    public async Task RR_60_ITEM_CREATE_with_an_all_zero_companyId_is_400_company_required()
    {
        Dictionary<string, object?> body = ItemFields();
        body["companyId"] = Guid.Empty;

        CommandReply reply = await _host.PostAsync(Items, body, "rr60-item-zero", cancellationToken: Ct);

        AssertCompanyRequired(reply);
    }

    [Fact]
    public async Task RR_60_INVENTORY_ADJUSTMENT_CREATE_without_a_companyId_is_400_company_required()
    {
        CommandReply reply = await _host.PostAsync(Adjustments, DraftFields(), "rr60-adjustment-missing", cancellationToken: Ct);

        AssertCompanyRequired(reply);
    }

    [Fact]
    public async Task RR_60_INVENTORY_ADJUSTMENT_CREATE_with_an_all_zero_companyId_is_400_company_required()
    {
        Dictionary<string, object?> body = DraftFields();
        body["companyId"] = Guid.Empty;

        CommandReply reply = await _host.PostAsync(Adjustments, body, "rr60-adjustment-zero", cancellationToken: Ct);

        AssertCompanyRequired(reply);
    }

    // ---- RR-61 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_61_a_posting_date_that_does_not_exist_is_a_400_at_binding_not_a_500()
    {
        Dictionary<string, object?> body = DraftFields();
        body["companyId"] = Company;
        body["postingDate"] = "2026-02-30";

        CommandReply reply = await _host.PostAsync(Adjustments, body, "rr61-date", cancellationToken: Ct);

        Assert.True(reply.Status == 400, reply.Body);
        Assert.Equal("request_body_invalid", reply.Code);
        Assert.Contains("2026-02-30", reply.Body, StringComparison.Ordinal);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'rr61-date'", Ct));
    }

    [Fact]
    public async Task RR_61_a_quantity_outside_the_decimal_range_is_a_400_at_binding_not_a_500()
    {
        string body = $$"""
            {"companyId":"{{Company}}","documentDate":"{{June}}","reasonCode":"CountDifference","warehouseId":"{{Warehouse}}","postingDate":"{{June}}",
             "lines":[{"lineNumber":1,"itemId":"{{Guid.NewGuid()}}","quantity":1e400,"warehouseId":"{{Warehouse}}","stockStatus":"Available"}]}
            """;

        CommandReply reply = await _host.PostAsync(Adjustments, body, "rr61-quantity", cancellationToken: Ct);

        Assert.True(reply.Status == 400, reply.Body);
        Assert.Equal("request_body_invalid", reply.Code);
        Assert.Contains("decimal range", reply.Body, StringComparison.Ordinal);
    }

    // ---- RR-72 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_72_an_unhandled_exception_answers_500_with_the_correlation_id_in_the_body_and_in_its_log_line()
    {
        CommandReply created = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "1" }, "rr72-create", cancellationToken: Ct);
        Assert.True(created.Status == 200, created.Body);
        Guid id = created.Json!["id"]!.GetValue<Guid>();

        CommandReply reply = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1, failure = "ThrowAfterWrite" }, "rr72-throw", ifMatch: "\"1\"", correlationId: "rr72-corr-1", cancellationToken: Ct);

        Assert.Equal(500, reply.Status);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Equal("rr72-corr-1", reply.CorrelationId);
        Assert.Equal("rr72-corr-1", reply.Json!["correlationId"]!.GetValue<string>());
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Error
            && line.Category.Contains("ExceptionHandler", StringComparison.Ordinal)
            && line.Scopes.TryGetValue("CorrelationId", out object? correlation) && Equals(correlation, "rr72-corr-1"));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------

    private static void AssertCompanyRequired(CommandReply reply)
    {
        Assert.True(reply.Status == 400, reply.Body);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Contains(reply.Json!["errors"]!.AsArray(), error => error!["code"]!.GetValue<string>() == "company_required");
    }

    private static Dictionary<string, object?> ItemFields() => new(StringComparer.Ordinal)
    {
        ["itemNumber"] = "RR60-001",
        ["description"] = "No company",
        ["itemType"] = "Stocked",
        ["baseUnit"] = "H87",
        ["lotTracking"] = "None",
        ["itemPostingGroup"] = "TRADE_GOODS",
        ["taxClass"] = "Standard",
        ["costingMethod"] = "MovingAverage",
    };

    private static Dictionary<string, object?> DraftFields() => new(StringComparer.Ordinal)
    {
        ["documentDate"] = June,
        ["reasonCode"] = "CountDifference",
        ["warehouseId"] = Warehouse,
        ["postingDate"] = June,
        ["lines"] = JsonSerializer.SerializeToNode(new[] { new { lineNumber = 1, itemId = Guid.NewGuid(), quantity = "1", warehouseId = Warehouse, stockStatus = "Available" } }),
    };
}
