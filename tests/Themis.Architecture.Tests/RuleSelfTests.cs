using ArchUnitNET.Fluent;

namespace Themis.Architecture.Tests;

/// <summary>
/// Each boundary rule is proven against the deliberate violations in Fixtures/. A rule that stops catching its
/// violation fails here, so the guard cannot silently rot. The brief's acceptance line "an illegal reference from the
/// fixture module into a kernel internal project makes the architecture tests fail" is the ARCH-04 case below.
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
    public void Fixtures_expose_two_modules_and_three_kernel_capabilities()
    {
        Assert.Equal(["Themis.Modules.Alpha", "Themis.Modules.Beta"], ThemisArchitectureRules.ModuleRoots(_architecture.Fixtures));
        Assert.Equal(["Documents", "Pipeline", "Posting"], ThemisArchitectureRules.KernelCapabilities(_architecture.Fixtures));
    }

    [Fact]
    public void RR_84_discovery_on_a_model_without_modules_or_kernel_capabilities_throws_instead_of_passing_on_nothing()
    {
        // The xunit assertion library holds no Themis namespace at all.
        ArchUnitNET.Domain.Architecture empty = new ArchUnitNET.Loader.ArchLoader().LoadAssemblies(typeof(Assert).Assembly).Build();

        InvalidOperationException modules = Assert.Throws<InvalidOperationException>(() => ThemisArchitectureRules.ModuleRoots(empty));
        InvalidOperationException capabilities = Assert.Throws<InvalidOperationException>(() => ThemisArchitectureRules.KernelCapabilities(empty));
        InvalidOperationException rules = Assert.Throws<InvalidOperationException>(() => ThemisArchitectureRules.ModulesUseOnlyPrimitivesAndContracts(empty).ToList());

        Assert.All(new[] { modules, capabilities, rules }, refused => Assert.Contains("RR-84", refused.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void ARCH_01_catches_a_type_outside_its_assembly_namespace() =>
        AssertCatches(ThemisArchitectureRules.NamespacesFollowAssemblyNames(_architecture.Fixtures).Select(pair => pair.Rule), "Elsewhere.StrayType");

    [Fact]
    public void ARCH_02_catches_kernel_code_that_uses_a_module() =>
        AssertCatches([ThemisArchitectureRules.KernelDependsOnNoModuleHostOrMigrator], "Themis.Kernel.Documents.KernelUsingModule");

    [Fact]
    public void ARCH_02_catches_kernel_code_that_uses_the_migrator() =>
        AssertCatches([ThemisArchitectureRules.KernelDependsOnNoModuleHostOrMigrator], "Themis.Kernel.Pipeline.UsesMigrator");

    [Fact]
    public void ARCH_03_catches_module_code_that_uses_the_host() =>
        AssertCatches([ThemisArchitectureRules.NothingDependsOnHostOrMigrator], "Themis.Modules.Beta.Application.UsesHost");

    [Fact]
    public void ARCH_04_catches_a_module_that_uses_a_kernel_capability() =>
        AssertCatches(ThemisArchitectureRules.ModulesUseOnlyPrimitivesAndContracts(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Modules.Alpha.Domain.UsesKernelInternals");

    [Fact]
    public void ARCH_04_catches_a_module_that_uses_another_modules_internals() =>
        AssertCatches(ThemisArchitectureRules.ModulesUseOnlyPrimitivesAndContracts(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Modules.Alpha.Application.UsesOtherModuleInternals");

    [Fact]
    public void ARCH_04_allows_a_module_to_use_another_modules_contracts_and_the_kernel_primitives()
    {
        IEnumerable<IArchRule> rules = ThemisArchitectureRules.ModulesUseOnlyPrimitivesAndContracts(_architecture.Fixtures).Select(pair => pair.Rule).ToList();
        AssertDoesNotCatch(rules, "Themis.Modules.Alpha.Application.UsesOtherModuleContracts");
        AssertDoesNotCatch(rules, "Themis.Modules.Alpha.Application.UsesKernelPrimitives");
    }

    [Fact]
    public void ARCH_05_catches_contracts_that_use_the_domain() =>
        AssertCatches(ThemisArchitectureRules.LayersDependDownwards(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Modules.Beta.Contracts.ContractUsingDomain");

    [Fact]
    public void ARCH_05_catches_an_application_layer_that_uses_infrastructure() =>
        AssertCatches(ThemisArchitectureRules.LayersDependDownwards(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Modules.Beta.Application.UsesOwnInfrastructure");

    [Fact]
    public void ARCH_05_allows_an_application_layer_that_uses_its_own_domain() =>
        AssertDoesNotCatch(ThemisArchitectureRules.LayersDependDownwards(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Modules.Beta.Application.UsesOwnDomain");

    [Fact]
    public void ARCH_06_catches_a_primitive_that_uses_a_web_framework() =>
        AssertCatches([ThemisArchitectureRules.SharedTypesAreFrameworkFree], "Themis.Kernel.Primitives.UsesWebFramework");

    [Fact]
    public void ARCH_07_catches_a_public_type_in_a_kernel_capability() =>
        AssertCatches([ThemisArchitectureRules.PublicTypesOnlyInSharedSurface], "Themis.Kernel.Pipeline.PublicLeak");

    [Fact]
    public void ARCH_07_allows_public_types_in_contracts_and_primitives()
    {
        AssertDoesNotCatch([ThemisArchitectureRules.PublicTypesOnlyInSharedSurface], "Themis.Modules.Beta.Contracts.BetaCommand");
        AssertDoesNotCatch([ThemisArchitectureRules.PublicTypesOnlyInSharedSurface], "Themis.Kernel.Primitives.FixturePrimitive");
    }

    [Fact]
    public void ARCH_08_catches_a_capability_that_uses_another_capability() =>
        AssertCatches(ThemisArchitectureRules.KernelCapabilitiesUseOnlyContracts(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Kernel.Posting.UsesOtherCapability");

    [Fact]
    public void ARCH_08_catches_contracts_that_use_a_capability() =>
        AssertCatches(ThemisArchitectureRules.KernelCapabilitiesUseOnlyContracts(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Kernel.Contracts.ContractUsingCapability");

    [Fact]
    public void ARCH_08_catches_primitives_that_use_contracts() =>
        AssertCatches(ThemisArchitectureRules.KernelCapabilitiesUseOnlyContracts(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Kernel.Primitives.UsesContracts");

    [Fact]
    public void ARCH_08_allows_a_capability_to_use_contracts_and_primitives() =>
        AssertDoesNotCatch(ThemisArchitectureRules.KernelCapabilitiesUseOnlyContracts(_architecture.Fixtures).Select(pair => pair.Rule), "Themis.Kernel.Posting.UsesContracts");

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
