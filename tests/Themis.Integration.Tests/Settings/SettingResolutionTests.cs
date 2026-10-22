using System.Text.Json.Nodes;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Settings;

/// <summary>
/// Resolution across warehouse, site, company, installation and default (Phase 0 Specification, Resolution; D-58),
/// on the test-only definitions of <see cref="SettingTestData"/>: the most specific value wins, a Cleared row falls
/// through to the next level, and a history-bearing definition resolves by the company's business date, where a row
/// written later with an earlier valid_from never overrides an already scheduled later-dated value.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SettingResolutionTests : IAsyncLifetime
{
    private const string Set = "/v1/kernel/settings/set";
    private const string Clear = "/v1/kernel/settings/clear";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Site = Milestone1OrganisationFixture.SiteId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public SettingResolutionTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
        await SettingTestData.RegisterAsync(_host, _db, Ct);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private async Task<CommandReply> SetAsync(string key, string level, string value, string idempotencyKey, string ifMatch, string? validFrom = null)
    {
        object body = level switch
        {
            "Installation" => new { key, level, value, validFrom },
            "Company" => new { key, level, value, companyId = Company, validFrom },
            "Site" => new { key, level, value, companyId = Company, siteId = Site, validFrom },
            _ => new { key, level, value, companyId = Company, warehouseId = Warehouse, validFrom },
        };
        CommandReply reply = await _host.PostAsync(Set, body, idempotencyKey, ifMatch: $"\"{ifMatch}\"", cancellationToken: Ct);
        Assert.True(reply.Status == 200, $"{level} {value}: {reply.Status} {reply.Body}");
        return reply;
    }

    private async Task<JsonNode> ReadAsync(string key, string query)
    {
        CommandReply reply = await _host.GetAsync($"/v1/kernel/settings/{key}{query}", cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!;
    }

    [Fact]
    public async Task D_58_the_most_specific_level_wins_a_level_without_a_value_is_passed_over_and_the_default_closes_the_chain()
    {
        string key = SettingTestData.AllLevelsKey;
        string warehouseQuery = $"?companyId={Company}&warehouseId={Warehouse}";
        JsonNode nothing = await ReadAsync(key, warehouseQuery);
        Assert.Equal("0", nothing["value"]!.GetValue<string>());
        Assert.Equal("Default", nothing["source"]!.GetValue<string>());
        Assert.Equal(["Installation", "Company", "Site", "Warehouse"], nothing["levels"]!.AsArray().Select(level => level!["level"]!.GetValue<string>()));

        await SetAsync(key, "Installation", "1", "res-i", "0");
        Assert.Equal(("1", "Installation"), Effective(await ReadAsync(key, warehouseQuery)));

        await SetAsync(key, "Company", "2", "res-c", "0");
        Assert.Equal(("2", "Company"), Effective(await ReadAsync(key, warehouseQuery)));
        // A site target reads the company value too, and the installation target never sees a company value.
        Assert.Equal(("2", "Company"), Effective(await ReadAsync(key, $"?companyId={Company}&siteId={Site}")));
        Assert.Equal(("1", "Installation"), Effective(await ReadAsync(key, string.Empty)));

        await SetAsync(key, "Warehouse", "4", "res-w", "0");
        Assert.Equal(("4", "Warehouse"), Effective(await ReadAsync(key, warehouseQuery)));
        // A site value sits between the warehouse and the company; the warehouse value still wins at the warehouse.
        await SetAsync(key, "Site", "3", "res-s", "0");
        Assert.Equal(("4", "Warehouse"), Effective(await ReadAsync(key, warehouseQuery)));
        Assert.Equal(("3", "Site"), Effective(await ReadAsync(key, $"?companyId={Company}&siteId={Site}")));

        // With the warehouse value cleared, a read naming only the warehouse finds the site value: the site is completed from the warehouse row.
        CommandReply cleared = await _host.PostAsync(Clear, new { key, level = "Warehouse", companyId = Company, warehouseId = Warehouse }, "res-w-clear", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(200, cleared.Status);
        Assert.Equal(("3", "Site"), Effective(await ReadAsync(key, warehouseQuery)));
    }

    [Fact]
    public async Task D_58_a_Cleared_row_falls_through_to_the_next_level_and_a_cleared_slot_can_be_set_again_on_its_next_version()
    {
        string key = SettingTestData.AllLevelsKey;
        string warehouseQuery = $"?companyId={Company}&warehouseId={Warehouse}";
        await SetAsync(key, "Installation", "1", "clr-i", "0");
        await SetAsync(key, "Company", "2", "clr-c", "0");
        await SetAsync(key, "Warehouse", "4", "clr-w", "0");

        CommandReply cleared = await _host.PostAsync(Clear, new { key, level = "Warehouse", companyId = Company, warehouseId = Warehouse }, "clr-w-clear", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(200, cleared.Status);
        Assert.Equal(("2", "Company"), Effective(await ReadAsync(key, warehouseQuery)));

        CommandReply clearedCompany = await _host.PostAsync(Clear, new { key, level = "Company", companyId = Company }, "clr-c-clear", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(200, clearedCompany.Status);
        Assert.Equal(("1", "Installation"), Effective(await ReadAsync(key, warehouseQuery)));

        await SetAsync(key, "Warehouse", "5", "clr-w-again", "2");
        Assert.Equal(("5", "Warehouse"), Effective(await ReadAsync(key, warehouseQuery)));
        Assert.Equal(3, (await ReadAsync(key, warehouseQuery))["levels"]!.AsArray().Single(level => level!["level"]!.GetValue<string>() == "Warehouse")!["version"]!.GetValue<int>());
    }

    [Fact]
    public async Task D_58_a_history_bearing_definition_resolves_by_the_business_date_and_a_later_write_with_an_earlier_date_never_overrides_a_scheduled_value()
    {
        string key = SettingTestData.HistoryKey;
        string query = $"?companyId={Company}";
        // Dates two days either side of today keep the company's time zone out of the picture (R-TIME-01).
        DateOnly today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        string past = today.AddDays(-30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string future = today.AddDays(30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string later = today.AddDays(-2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        CommandReply withoutDate = await _host.PostAsync(Set, new { key, level = "Company", companyId = Company, value = "10" }, "his-nodate", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, withoutDate.Status);
        Assert.Equal("valid_from_required", withoutDate.Code);

        await SetAsync(key, "Company", "10", "his-1", "0", past);
        Assert.Equal(("10", "Company"), Effective(await ReadAsync(key, query)));

        // A value scheduled for the future is not effective yet; the current value stays.
        await SetAsync(key, "Company", "30", "his-2", "1", future);
        Assert.Equal(("10", "Company"), Effective(await ReadAsync(key, query)));

        // A row written later with an earlier valid_from than the scheduled one applies now and does not override the scheduled value.
        await SetAsync(key, "Company", "20", "his-3", "2", later);
        Assert.Equal(("20", "Company"), Effective(await ReadAsync(key, query)));
        JsonNode read = await ReadAsync(key, query);
        JsonNode company = read["levels"]!.AsArray().Single(level => level!["level"]!.GetValue<string>() == "Company")!;
        Assert.Equal(3, company["version"]!.GetValue<int>());
        Assert.Equal(later, company["validFrom"]!.GetValue<string>());

        // A Cleared row with a valid_from applies from that date: cleared as of two days ago, so the company level falls through today.
        CommandReply cleared = await _host.PostAsync(Clear, new { key, level = "Company", companyId = Company, validFrom = later }, "his-clear", ifMatch: "\"3\"", cancellationToken: Ct);
        Assert.Equal(200, cleared.Status);
        Assert.Equal(("0", "Default"), Effective(await ReadAsync(key, query)));

        // The future value is untouched and would decide on its date: it still sits in the slot at version 2.
        Assert.Equal("30", await _host.ScalarAsync("SELECT value FROM kernel.setting_value WHERE definition_key = @key AND version = 2", Ct, ("key", key)));
    }

    private static (string? Value, string Source) Effective(JsonNode read) => (read["value"]?.GetValue<string>(), read["source"]!.GetValue<string>());
}
