using ArchUnitNET.Fluent;

namespace Themis.Architecture.Tests;

/// <summary>
/// The rules of <see cref="ThemisArchitectureRules"/> hold for the production code and the fixture module (D-26).
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ModuleBoundaryTests : IClassFixture<ArchitectureFixture>
{
    private readonly ArchitectureFixture _architecture;

    public ModuleBoundaryTests(ArchitectureFixture architecture)
    {
        _architecture = architecture;
    }

    [Fact]
    public void The_production_architecture_holds_the_kernel_the_host_the_migrator_and_the_fixture_module()
    {
        string[] expected =
        [
            "Themis.Host", "Themis.Migrator", "Themis.FixtureModule",
            "Themis.Kernel.Primitives", "Themis.Kernel.Contracts", "Themis.Kernel.Pipeline", "Themis.Kernel.Persistence",
            "Themis.Kernel.Organisation", "Themis.Kernel.MasterData", "Themis.Kernel.Documents", "Themis.Kernel.Posting",
            "Themis.Kernel.Messaging", "Themis.Kernel.Identity", "Themis.Kernel.Settings",
        ];
        foreach (string name in expected)
        {
            Assert.Contains(name, _architecture.ProductionAssemblyNames);
        }

        Assert.DoesNotContain(_architecture.ProductionAssemblyNames, name => name.EndsWith("Tests", StringComparison.Ordinal));
        Assert.Equal(["Themis.FixtureModule"], ThemisArchitectureRules.ModuleRoots(_architecture.Production));
        Assert.Equal(["Documents", "Identity", "MasterData", "Messaging", "Organisation", "Persistence", "Pipeline", "Posting", "Settings"], ThemisArchitectureRules.KernelCapabilities(_architecture.Production));
    }

    [Fact]
    public void ARCH_01_namespaces_follow_assembly_names() => AssertHold(ThemisArchitectureRules.NamespacesFollowAssemblyNames(_architecture.Production));

    [Fact]
    public void ARCH_02_kernel_depends_on_no_module_host_or_migrator() => AssertHolds("ARCH-02", ThemisArchitectureRules.KernelDependsOnNoModuleHostOrMigrator);

    [Fact]
    public void ARCH_03_nothing_depends_on_the_host_or_the_migrator() => AssertHolds("ARCH-03", ThemisArchitectureRules.NothingDependsOnHostOrMigrator);

    [Fact]
    public void ARCH_04_modules_use_only_primitives_and_contracts() => AssertHold(ThemisArchitectureRules.ModulesUseOnlyPrimitivesAndContracts(_architecture.Production));

    [Fact]
    public void ARCH_05_module_layers_depend_downwards() => AssertHold(ThemisArchitectureRules.LayersDependDownwards(_architecture.Production));

    [Fact]
    public void ARCH_06_shared_types_are_framework_free() => AssertHolds("ARCH-06", ThemisArchitectureRules.SharedTypesAreFrameworkFree);

    [Fact]
    public void ARCH_07_public_types_only_in_the_shared_surface() => AssertHolds("ARCH-07", ThemisArchitectureRules.PublicTypesOnlyInSharedSurface);

    [Fact]
    public void ARCH_08_kernel_capabilities_use_only_contracts() => AssertHold(ThemisArchitectureRules.KernelCapabilitiesUseOnlyContracts(_architecture.Production));

    private void AssertHold(IEnumerable<(string Id, IArchRule Rule)> rules)
    {
        foreach ((string id, IArchRule rule) in rules)
        {
            AssertHolds(id, rule);
        }
    }

    private void AssertHolds(string id, IArchRule rule)
    {
        IReadOnlyList<string> violations = ArchitectureFixture.Violations(rule, _architecture.Production);
        Assert.True(violations.Count == 0, $"{id} violated:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }
}
