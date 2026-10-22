using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Persistence.Seeding;
using Themis.Kernel.Settings;

namespace Themis.Integration.Tests.Settings;

/// <summary>
/// The test-only definitions of the settings tests (docs/development/test-seams.md): a plain definition writable at
/// every level and a history-bearing one, since no milestone 2 definition allows a site or warehouse value or carries
/// valid_from. They are registered through the definition seam of the test host and written to the catalogue table as
/// the migrator writes it, beside the real catalogue (D-58).
/// </summary>
internal static class SettingTestData
{
    public const string AllLevelsKey = "test.all_levels";
    public const string HistoryKey = "test.history_bearing";

    private static readonly IReadOnlySet<SettingLevel> AllLevels = new HashSet<SettingLevel> { SettingLevel.Installation, SettingLevel.Company, SettingLevel.Site, SettingLevel.Warehouse };

    public static SettingDefinition AllLevelsDefinition { get; } = new(AllLevelsKey, SettingValueType.Integer, "0", AllLevels, SettingBacking.Setting, "R-API-01", "Test-only: a plain definition at every level.", Min: "0", Max: "1000");

    public static SettingDefinition HistoryDefinition { get; } = new(HistoryKey, SettingValueType.Integer, "0", AllLevels, SettingBacking.Setting, "R-API-01", "Test-only: a history-bearing definition at every level.", Min: "0", Max: "1000", HistoryBearing: true);

    /// <summary>Adds the two definitions to the host's seam and to kernel.setting_definition, beside the real catalogue.</summary>
    public static async Task RegisterAsync(PipelineTestHost host, PostgresFixture db, CancellationToken cancellationToken)
    {
        host.SettingSeam.Add(AllLevelsDefinition);
        host.SettingSeam.Add(HistoryDefinition);
        await SettingDefinitionSeeder.ApplyCatalogueAsync(db.ConnectionStringFor("themis_migrator"), [.. SettingCatalog.Definitions, AllLevelsDefinition, HistoryDefinition], SettingValueFormat.IsValid, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, cancellationToken);
    }
}
