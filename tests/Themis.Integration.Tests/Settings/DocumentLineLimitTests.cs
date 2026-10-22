using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Settings;

/// <summary>
/// document.max_lines (RR-54, D-58): a draft with more lines than the effective limit at the installation or the
/// company is a 400 line_limit_exceeded, read inside the transaction and therefore stored as the key's result; a
/// company value overrides the installation value.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class DocumentLineLimitTests : IAsyncLifetime
{
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string Set = "/v1/kernel/settings/set";
    private const string MaxLines = "document.max_lines";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private Guid _bolt;

    public DocumentLineLimitTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
        CommandReply item = await _host.PostAsync("/v1/kernel/items", new { companyId = Company, itemNumber = "LIMIT-BOLT", description = "Bolt", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "item-limit-bolt", cancellationToken: Ct);
        Assert.Equal(200, item.Status);
        _bolt = item.Json!["id"]!.GetValue<Guid>();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private object Draft(int lineCount) => new
    {
        companyId = Company,
        documentDate = "2026-06-15",
        reasonCode = "CountDifference",
        warehouseId = Warehouse,
        lines = Enumerable.Range(1, lineCount).Select(number => new { lineNumber = number, itemId = _bolt, quantity = "1", stockStatus = "Available" }).ToArray(),
    };

    [Fact]
    public async Task RR_54_the_line_limit_applies_at_the_installation_and_a_company_value_overrides_it_on_create_and_update()
    {
        Assert.Equal(200, (await _host.PostAsync(Set, new { key = MaxLines, level = "Installation", value = "2" }, "limit-i", ifMatch: "\"0\"", cancellationToken: Ct)).Status);

        CommandReply over = await _host.PostAsync(Adjustments, Draft(3), "ia-over", cancellationToken: Ct);
        Assert.Equal(400, over.Status);
        Assert.Equal("line_limit_exceeded", over.Code);
        Assert.Null(over.RuleId);
        Assert.Equal("2", over.Json!["errors"]![0]!["details"]!["limit"]!.GetValue<string>());
        Assert.Equal(0L, await _host.CountAsync("kernel.document", Ct));
        // Found inside the transaction, so it is the key's result (Build Brief, Errors).
        Assert.True((await _host.PostAsync(Adjustments, Draft(3), "ia-over", cancellationToken: Ct)).Replayed);

        CommandReply within = await _host.PostAsync(Adjustments, Draft(2), "ia-within", cancellationToken: Ct);
        Assert.Equal(200, within.Status);
        Guid id = within.Json!["id"]!.GetValue<Guid>();

        CommandReply updateOver = await _host.PostAsync($"{Adjustments}/{id}/update", new { documentDate = "2026-06-15", reasonCode = "CountDifference", warehouseId = Warehouse, lines = Enumerable.Range(1, 3).Select(number => new { lineNumber = number, itemId = _bolt, quantity = "1", stockStatus = "Available" }).ToArray() }, "ia-upd-over", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(400, updateOver.Status);
        Assert.Equal("line_limit_exceeded", updateOver.Code);

        // The company's own limit wins over the installation's.
        Assert.Equal(200, (await _host.PostAsync(Set, new { key = MaxLines, level = "Company", companyId = Company, value = "5" }, "limit-c", ifMatch: "\"0\"", cancellationToken: Ct)).Status);
        CommandReply nowWithin = await _host.PostAsync(Adjustments, Draft(3), "ia-now-within", cancellationToken: Ct);
        Assert.Equal(200, nowWithin.Status);
        CommandReply stillOver = await _host.PostAsync(Adjustments, Draft(6), "ia-still-over", cancellationToken: Ct);
        Assert.Equal(400, stillOver.Status);
        Assert.Equal("5", stillOver.Json!["errors"]![0]!["details"]!["limit"]!.GetValue<string>());
    }
}
