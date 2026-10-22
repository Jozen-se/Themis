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
    public void Every_definition_is_well_formed_and_a_Planned_definition_names_its_phase_while_a_Column_backed_one_names_its_column()
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
            // The default, where there is one, is a valid value of its own definition (a definition with an open decision has none, D-10).
            Assert.True(definition.DefaultValue is null || SettingValueFormat.IsValid(definition, definition.DefaultValue), $"{definition.Key}'s default '{definition.DefaultValue}' fails its own validation.");
        }
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
