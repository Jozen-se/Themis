using ArchUnitNET.Fluent;

namespace Themis.ArchitectureTests;

/// <summary>
/// Each boundary rule is proven against the deliberate violations in Fixtures/. A rule that stops catching its
/// violation fails here, so the guard cannot silently rot.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class RuleSelfTests : IClassFixture<ArchitectureFixture>
{
    private readonly ArchitectureFixture _architecture;

    public RuleSelfTests(ArchitectureFixture architecture)
    {
        _architecture = architecture;
    }

    [Fact]
    public void Fixtures_expose_two_modules_and_the_kernel_as_units()
    {
        Assert.Equal(["Alpha", "Beta"], ThemisArchitectureRules.ModuleNames(_architecture.Fixtures));
        Assert.Equal(["Themis.Kernel", "Themis.Modules.Alpha", "Themis.Modules.Beta"], ThemisArchitectureRules.UnitNamespaces(_architecture.Fixtures));
    }

    [Fact]
    public void ARCH_01_catches_a_type_outside_its_assembly_namespace()
    {
        IEnumerable<IArchRule> rules = ThemisArchitectureRules.NamespacesFollowAssemblyNames(_architecture.Fixtures).Select(pair => pair.Rule);
        AssertCatches(rules, "Elsewhere.StrayType");
    }

    [Fact]
    public void ARCH_02_catches_kernel_code_that_uses_a_module() =>
        AssertCatches([ThemisArchitectureRules.KernelDependsOnNoModuleOrHost], "Themis.Kernel.Domain.KernelUsingModule");

    [Fact]
    public void ARCH_03_catches_module_code_that_uses_the_host() =>
        AssertCatches([ThemisArchitectureRules.NothingDependsOnHost], "Themis.Modules.Beta.Application.UsesHost");

    [Fact]
    public void ARCH_04_catches_a_module_that_uses_kernel_internals()
    {
        IEnumerable<IArchRule> rules = ThemisArchitectureRules.ModulesUseOnlyContracts(_architecture.Fixtures).Select(pair => pair.Rule);
        AssertCatches(rules, "Themis.Modules.Alpha.Domain.UsesKernelInternals");
    }

    [Fact]
    public void ARCH_04_catches_a_module_that_uses_another_modules_internals()
    {
        IEnumerable<IArchRule> rules = ThemisArchitectureRules.ModulesUseOnlyContracts(_architecture.Fixtures).Select(pair => pair.Rule);
        AssertCatches(rules, "Themis.Modules.Alpha.Application.UsesOtherModuleInternals");
    }

    [Fact]
    public void ARCH_04_allows_a_module_to_use_another_modules_contracts()
    {
        IEnumerable<IArchRule> rules = ThemisArchitectureRules.ModulesUseOnlyContracts(_architecture.Fixtures).Select(pair => pair.Rule);
        AssertDoesNotCatch(rules, "Themis.Modules.Alpha.Application.UsesOtherModuleContracts");
    }

    [Fact]
    public void ARCH_05_catches_contracts_that_use_the_domain()
    {
        IEnumerable<IArchRule> rules = ThemisArchitectureRules.LayersDependDownwards(_architecture.Fixtures).Select(pair => pair.Rule);
        AssertCatches(rules, "Themis.Kernel.Contracts.ContractUsingDomain");
    }

    [Fact]
    public void ARCH_05_catches_an_application_layer_that_uses_infrastructure()
    {
        IEnumerable<IArchRule> rules = ThemisArchitectureRules.LayersDependDownwards(_architecture.Fixtures).Select(pair => pair.Rule);
        AssertCatches(rules, "Themis.Modules.Beta.Application.UsesOwnInfrastructure");
    }

    [Fact]
    public void ARCH_05_allows_an_application_layer_that_uses_its_own_domain()
    {
        IEnumerable<IArchRule> rules = ThemisArchitectureRules.LayersDependDownwards(_architecture.Fixtures).Select(pair => pair.Rule);
        AssertDoesNotCatch(rules, "Themis.Modules.Beta.Application.UsesOwnDomain");
    }

    [Fact]
    public void ARCH_06_catches_a_domain_type_that_uses_a_web_framework() =>
        AssertCatches([ThemisArchitectureRules.DomainAndContractsAreFrameworkFree], "Themis.Kernel.Domain.UsesWebFramework");

    [Fact]
    public void ARCH_07_catches_a_public_type_outside_contracts() =>
        AssertCatches([ThemisArchitectureRules.PublicTypesOnlyInContracts], "Themis.Kernel.Application.PublicLeak");

    [Fact]
    public void ARCH_07_allows_a_public_type_in_contracts() =>
        AssertDoesNotCatch([ThemisArchitectureRules.PublicTypesOnlyInContracts], "Themis.Modules.Beta.Contracts.BetaCommand");

    private void AssertCatches(IEnumerable<IArchRule> rules, string offendingType)
    {
        IReadOnlyList<string> violations = rules.SelectMany(rule => ArchitectureFixture.Violations(rule, _architecture.Fixtures)).ToList();
        Assert.True(violations.Any(description => description.Contains(offendingType, StringComparison.Ordinal)),
            $"Expected a violation naming {offendingType}. Violations found:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private void AssertDoesNotCatch(IEnumerable<IArchRule> rules, string allowedType)
    {
        IReadOnlyList<string> violations = rules.SelectMany(rule => ArchitectureFixture.Violations(rule, _architecture.Fixtures)).ToList();
        Assert.DoesNotContain(violations, description => description.Contains(allowedType, StringComparison.Ordinal));
    }
}
