using ArchUnitNET.Fluent;

namespace Themis.ArchitectureTests;

/// <summary>
/// The rules of <see cref="ThemisArchitectureRules"/> hold for the production code (D-26).
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
    public void The_production_architecture_contains_the_kernel_and_the_host()
    {
        Assert.Contains("Themis.Host", _architecture.ProductionAssemblyNames);
        Assert.Contains("Themis.Kernel.Contracts", _architecture.ProductionAssemblyNames);
        Assert.Contains("Themis.Kernel.Domain", _architecture.ProductionAssemblyNames);
        Assert.Contains("Themis.Kernel.Application", _architecture.ProductionAssemblyNames);
        Assert.Contains("Themis.Kernel.Infrastructure", _architecture.ProductionAssemblyNames);
        Assert.Contains("Themis.Kernel.Api", _architecture.ProductionAssemblyNames);
        Assert.DoesNotContain(_architecture.ProductionAssemblyNames, name => name.EndsWith("Tests", StringComparison.Ordinal));
    }

    [Fact]
    public void ARCH_01_namespaces_follow_assembly_names()
    {
        foreach ((string id, IArchRule rule) in ThemisArchitectureRules.NamespacesFollowAssemblyNames(_architecture.Production))
        {
            AssertHolds(id, rule);
        }
    }

    [Fact]
    public void ARCH_02_kernel_depends_on_no_module_or_host() => AssertHolds("ARCH-02", ThemisArchitectureRules.KernelDependsOnNoModuleOrHost);

    [Fact]
    public void ARCH_03_nothing_depends_on_the_host() => AssertHolds("ARCH-03", ThemisArchitectureRules.NothingDependsOnHost);

    [Fact]
    public void ARCH_04_modules_use_only_contracts()
    {
        foreach ((string id, IArchRule rule) in ThemisArchitectureRules.ModulesUseOnlyContracts(_architecture.Production))
        {
            AssertHolds(id, rule);
        }
    }

    [Fact]
    public void ARCH_05_layers_depend_downwards()
    {
        foreach ((string id, IArchRule rule) in ThemisArchitectureRules.LayersDependDownwards(_architecture.Production))
        {
            AssertHolds(id, rule);
        }
    }

    [Fact]
    public void ARCH_06_domain_and_contracts_are_framework_free() => AssertHolds("ARCH-06", ThemisArchitectureRules.DomainAndContractsAreFrameworkFree);

    [Fact]
    public void ARCH_07_public_types_only_in_contracts() => AssertHolds("ARCH-07", ThemisArchitectureRules.PublicTypesOnlyInContracts);

    private void AssertHolds(string id, IArchRule rule)
    {
        IReadOnlyList<string> violations = ArchitectureFixture.Violations(rule, _architecture.Production);
        Assert.True(violations.Count == 0, $"{id} violated:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }
}
