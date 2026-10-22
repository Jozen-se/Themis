using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;
using Themis.Kernel.Settings;

namespace Themis.Kernel.Tests.Settings;

/// <summary>The deciding row of one slot (Phase 0 Specification, Resolution; D-58), without a database.</summary>
[Trait("Category", "Unit")]
public sealed class SettingResolutionTests
{
    private static readonly IReadOnlySet<SettingLevel> Company = new HashSet<SettingLevel> { SettingLevel.Company };
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static SettingDefinition Plain { get; } = new("test.plain", SettingValueType.Integer, "1", Company, SettingBacking.Setting, "R-API-01", "Plain.");

    private static SettingDefinition History { get; } = new("test.history", SettingValueType.Integer, "1", Company, SettingBacking.Setting, "R-API-01", "History.", HistoryBearing: true);

    private static SettingValueRecord Row(long version, string? value, string? validFrom = null) =>
        new(Guid.NewGuid(), SettingLevel.Company, CompanyId, null, null, value is null ? SettingValueKind.Cleared : SettingValueKind.Set, value, validFrom is null ? null : BusinessDate.Parse(validFrom), version, DateTimeOffset.UnixEpoch);

    [Fact]
    public void A_plain_definition_takes_the_highest_version_and_a_Cleared_row_on_top_means_no_value()
    {
        Assert.Equal("3", SettingsReader.Decide(Plain, [Row(1, "1"), Row(3, "3"), Row(2, "2")], null)!.Value);
        Assert.Equal(SettingValueKind.Cleared, SettingsReader.Decide(Plain, [Row(1, "1"), Row(2, null)], null)!.Kind);
        Assert.Null(SettingsReader.Decide(Plain, [], null));
    }

    [Fact]
    public void A_history_bearing_definition_takes_the_latest_valid_from_on_or_before_the_date_ties_going_to_the_highest_version()
    {
        SettingValueRecord[] rows = [Row(1, "10", "2026-01-01"), Row(2, "30", "2026-07-01"), Row(3, "20", "2026-03-01"), Row(4, "25", "2026-03-01")];
        Assert.Equal("10", SettingsReader.Decide(History, rows, BusinessDate.Parse("2026-02-15"))!.Value);
        // A row written later (version 3, then 4) with an earlier date never overrides the later-dated row 2 once that date is reached.
        Assert.Equal("25", SettingsReader.Decide(History, rows, BusinessDate.Parse("2026-06-30"))!.Value);
        Assert.Equal("30", SettingsReader.Decide(History, rows, BusinessDate.Parse("2026-07-01"))!.Value);
        Assert.Null(SettingsReader.Decide(History, rows, BusinessDate.Parse("2025-12-31")));
    }

    [Fact]
    public void A_Cleared_row_with_a_valid_from_applies_from_that_date_and_the_search_order_is_warehouse_site_company_installation()
    {
        SettingValueRecord[] rows = [Row(1, "10", "2026-01-01"), Row(2, null, "2026-04-01")];
        Assert.Equal("10", SettingsReader.Decide(History, rows, BusinessDate.Parse("2026-03-31"))!.Value);
        Assert.Equal(SettingValueKind.Cleared, SettingsReader.Decide(History, rows, BusinessDate.Parse("2026-04-01"))!.Kind);
        Assert.Equal([SettingLevel.Warehouse, SettingLevel.Site, SettingLevel.Company, SettingLevel.Installation], SettingsReader.SearchOrder);
    }
}
