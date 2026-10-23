using System.Text.RegularExpressions;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Settings;

namespace Themis.Architecture.Tests;

/// <summary>
/// D-58: every configurable default of the Foundation's classification list is in the settings catalogue at least
/// once, as a Setting, a Column-backed, a Data-backed or a Planned definition, and the catalogue holds nothing that is
/// not in that list or in the specification's milestone 2 definitions. Read from the snapshots under docs/design, in
/// the shape of <see cref="DesignReferenceTests"/>.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class SettingCatalogCoverageTests
{
    [GeneratedRegex(@"^\| (R-[A-Z0-9]{2,4}-\d{2}) \|", RegexOptions.Multiline)]
    private static partial Regex ListRow();

    [GeneratedRegex(@"^\| ([a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+) \|.*\| ((?:R-[A-Z0-9]{2,4}-\d{2}|RR-\d{2})(?:, [^|]*)?) \|$", RegexOptions.Multiline)]
    private static partial Regex SpecificationRow();

    [GeneratedRegex(@"(R-[A-Z0-9]{2,4}-\d{2}|RR-\d{2})")]
    private static partial Regex RuleToken();

    /// <summary>
    /// RR-97: the definition keys of this build. A rule ID check alone lets an extra key under a listed rule through
    /// (a third R-MON-02 definition, say), so the keys are compared as well; a change to the catalogue changes this list.
    /// </summary>
    private static readonly string[] ExpectedKeys =
    [
        "company.rounding_mode", "company.tax_rounding_level", "document.discarded_draft_retention_days", "document.max_lines",
        "document.over_delivery_tolerance_percent", "document.user_states", "fiscal_period.reopen_closed_allowed",
        "idempotency.retention_days", "inventory.availability_components", "inventory.costing_method",
        "inventory.negative_stock_allowed", "inventory.retroactive_cost_adjustment", "master_data.duplicate_check_fields",
        "master_data.owner_roles", "payments.difference_tolerance", "sales.allocation_priority", "sales.credit_check_mode",
        "sales.goods_issue_posting_point", "sales.invoice_grouping", "sales.partial_delivery_allowed", "sales.price_sources",
        "sales.return_valuation", "security.four_eyes_posting", "security.sod_conflicts", "tax.determination",
    ];

    [Fact]
    public void RR_97_the_catalogue_holds_exactly_the_expected_definition_keys()
    {
        (string[] missing, string[] extra) = CompareKeys(ExpectedKeys, SettingCatalog.Definitions.Select(definition => definition.Key));
        Assert.True(missing.Length == 0 && extra.Length == 0, $"Catalogue keys differ from the expected list. Missing: {string.Join(", ", missing)}. Extra: {string.Join(", ", extra)} (RR-97).");
    }

    [Fact]
    public void RR_97_the_key_comparison_reports_a_missing_and_an_extra_definition_key()
    {
        string[] catalogue = [.. ExpectedKeys.Where(key => key != "document.max_lines"), "sales.invented_default"];

        (string[] missing, string[] extra) = CompareKeys(ExpectedKeys, catalogue);

        Assert.Equal(["document.max_lines"], missing);
        Assert.Equal(["sales.invented_default"], extra);
    }

    [Fact]
    public void D_63_the_shape_check_refuses_a_Planned_definition_with_a_type_a_Data_definition_with_a_default_and_a_Setting_definition_without_a_type()
    {
        IReadOnlySet<SettingLevel> company = new HashSet<SettingLevel> { SettingLevel.Company };
        SettingDefinition planned = new("test.planned", null, null, company, SettingBacking.Planned, "R-O2C-01", "A planned test definition.", Phase: "Phase 1");

        Assert.True(planned.HasShapeOfItsBacking);
        Assert.False((planned with { ValueType = SettingValueType.Code }).HasShapeOfItsBacking);
        Assert.False((planned with { AllowedValues = ["Block"] }).HasShapeOfItsBacking);
        Assert.False((planned with { Backing = SettingBacking.Data, DefaultValue = "A|B" }).HasShapeOfItsBacking);
        Assert.False((planned with { Backing = SettingBacking.Setting, Phase = null }).HasShapeOfItsBacking);
        Assert.True((planned with { Backing = SettingBacking.Setting, Phase = null, ValueType = SettingValueType.Integer }).HasShapeOfItsBacking);
    }

    [Fact]
    public void D_62_security_four_eyes_posting_is_a_Planned_company_definition_for_step_13_with_the_document_type_as_planned_level()
    {
        SettingDefinition definition = SettingCatalog.Find("security.four_eyes_posting") ?? throw new InvalidOperationException("security.four_eyes_posting is missing.");
        Assert.Equal(SettingBacking.Planned, definition.Backing);
        Assert.Equal("R-SEC-03", definition.RuleId);
        Assert.Equal("D-62", definition.DecisionId);
        Assert.Equal([SettingLevel.Company], definition.Levels);
        Assert.Equal("DocumentType", definition.PlannedLevels);
        Assert.Contains("step 13", definition.Phase, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(definition.Description));
        Assert.True(definition.HasShapeOfItsBacking);
    }

    [Fact]
    public void D_58_every_configurable_default_of_the_Foundation_list_is_in_the_catalogue_at_least_once_and_the_catalogue_holds_nothing_else()
    {
        string root = RepositoryRoot();
        string foundation = File.ReadAllText(Path.Combine(root, "docs", "design", "themis-foundation-design.md"));
        string specification = File.ReadAllText(Path.Combine(root, "docs", "design", "themis-phase-0-specification.md"));

        string list = Section(foundation, "**Configurable defaults**");
        HashSet<string> listed = ListRow().Matches(list).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.True(listed.Count >= 20, $"The Foundation's configurable defaults list looks short: {listed.Count} rules.");

        string milestone2 = Section(specification, "**Definitions in milestone 2**");
        Dictionary<string, string> specified = SpecificationRow().Matches(milestone2).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);
        Assert.True(specified.Count >= 5, $"The specification's milestone 2 definitions look short: {specified.Count} keys.");

        HashSet<string> catalogued = SettingCatalog.Definitions.Select(definition => definition.RuleId).ToHashSet(StringComparer.Ordinal);
        string[] missing = [.. listed.Where(rule => !catalogued.Contains(rule)).Order(StringComparer.Ordinal)];
        Assert.True(missing.Length == 0, $"Configurable defaults of the Foundation's list with no definition in the catalogue: {string.Join(", ", missing)}");

        HashSet<string> specifiedRules = specified.Values.SelectMany(cell => RuleToken().Matches(cell).Select(match => match.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        string[] extra = [.. SettingCatalog.Definitions.Where(definition => !listed.Contains(definition.RuleId) && !specifiedRules.Contains(definition.RuleId)).Select(definition => $"{definition.Key} ({definition.RuleId})").Order(StringComparer.Ordinal)];
        Assert.True(extra.Length == 0, $"Catalogue definitions whose rule is neither in the Foundation's list nor in the specification's milestone 2 definitions: {string.Join(", ", extra)}");

        // The specification's milestone 2 keys exist with the specification's rule, and no key is defined twice.
        foreach ((string key, string ruleCell) in specified)
        {
            SettingDefinition? definition = SettingCatalog.Find(key);
            Assert.True(definition is not null, $"The specification's milestone 2 definition {key} is not in the catalogue.");
            Assert.Contains(definition!.RuleId, ruleCell, StringComparison.Ordinal);
        }

        Assert.Equal(SettingCatalog.Definitions.Count, SettingCatalog.Definitions.Select(definition => definition.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void D_63_every_definition_has_the_shape_of_its_backing_a_Planned_one_names_its_phase_and_a_Column_backed_one_its_column()
    {
        foreach (SettingDefinition definition in SettingCatalog.Definitions)
        {
            Assert.True(SettingKeys.IsValid(definition.Key), $"{definition.Key} is not a valid key.");
            Assert.NotEmpty(definition.Levels);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), $"{definition.Key} has no description.");
            Assert.Equal(definition.Backing == SettingBacking.Planned, definition.Phase is not null);
            Assert.Equal(definition.Backing == SettingBacking.Column, definition.ColumnName is not null);
            Assert.True(definition.PlannedLevels is null || definition.Backing == SettingBacking.Planned, $"{definition.Key} names planned levels without being Planned.");
            Assert.True(definition.LockedReason is null || definition.Backing == SettingBacking.Setting, $"{definition.Key} is locked but not a Setting.");
            // D-63, RR-98, RR-99: Setting and Column carry a value type; Data and Planned carry no type, default or validation.
            Assert.True(definition.HasShapeOfItsBacking, $"{definition.Key} ({definition.Backing}) does not have the shape of its backing (D-63).");
            // The default, where there is one, is a valid value of its own definition (a definition with an open decision has none, D-10).
            Assert.True(definition.DefaultValue is null || SettingValueFormat.IsValid(definition, definition.DefaultValue), $"{definition.Key}'s default '{definition.DefaultValue}' fails its own validation.");
        }
    }

    private static (string[] Missing, string[] Extra) CompareKeys(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        HashSet<string> expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        HashSet<string> actualSet = actual.ToHashSet(StringComparer.Ordinal);
        return ([.. expectedSet.Except(actualSet).Order(StringComparer.Ordinal)], [.. actualSet.Except(expectedSet).Order(StringComparer.Ordinal)]);
    }

    private static string Section(string markdown, string heading)
    {
        int start = markdown.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{heading}' not found in the snapshot.");
        int end = markdown.IndexOf("\n\n", markdown.IndexOf("\n|", start, StringComparison.Ordinal), StringComparison.Ordinal);
        return end < 0 ? markdown[start..] : markdown[start..end];
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
