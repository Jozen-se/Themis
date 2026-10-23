using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchModel = ArchUnitNET.Domain.Architecture;

namespace Themis.Architecture.Tests;

/// <summary>
/// The reference rules of the brief (Repository layout) and D-26, as code. Each rule has an identifier (ARCH-nn) that
/// failure messages carry. The rules are generic: a new process module under <c>Themis.Modules.&lt;Name&gt;</c> or a
/// new kernel capability under <c>Themis.Kernel.&lt;Name&gt;</c> is covered without editing this file.
/// Changing a rule here is a reviewed architectural decision.
/// </summary>
internal static class ThemisArchitectureRules
{
    private const string Layers = "(?:Contracts|Domain|Application|Infrastructure|Api)";
    private const string InternalLayers = "(?:Domain|Application|Infrastructure|Api)";
    private const string Name = "[A-Za-z0-9]+";

    /// <summary>A process module's root: Themis.Modules.&lt;Name&gt;, or the test-only fixture module.</summary>
    private const string ModuleRoot = @"Themis\.(?:Modules\." + Name + "|FixtureModule)";

    /// <summary>The two kernel assemblies everything may reference.</summary>
    private const string SharedKernel = "(?:Primitives|Contracts)";

    /// <summary>Namespaces Primitives, Contracts and module Domain and Contracts projects may not depend on (ARCH-06).</summary>
    private const string FrameworkNamespaces = @"^(Microsoft\.AspNetCore|Microsoft\.EntityFrameworkCore|Npgsql)(?:\.|$)";

    /// <summary>ARCH-01: every type in a Themis assembly lives in a namespace that starts with the assembly's name.</summary>
    public static IEnumerable<(string Id, IArchRule Rule)> NamespacesFollowAssemblyNames(ArchModel architecture)
    {
        foreach (Assembly assembly in ThemisAssemblies(architecture))
        {
            string escaped = Regex.Escape(assembly.Name);
            yield return ($"ARCH-01 ({assembly.Name})", Types().That().ResideInAssembly(assembly).And().DoNotHaveNameStartingWith("<")
                .Should().ResideInNamespaceMatching($"^{escaped}(\\.|$)")
                .Because($"ARCH-01: types in {assembly.Name} must reside in namespace {assembly.Name} or below").WithoutRequiringPositiveResults());
        }
    }

    /// <summary>ARCH-02: the kernel knows nothing about any module, the fixture module, the host or the migrator.</summary>
    public static IArchRule KernelDependsOnNoModuleHostOrMigrator =>
        Types().That().ResideInNamespaceMatching(@"^Themis\.Kernel(?:\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Themis\.(Modules|FixtureModule|Host|Migrator)(?:\.|$)")
            .Because("ARCH-02: kernel projects never reference a module, the fixture module, the host or the migrator (brief, Reference rules)").WithoutRequiringPositiveResults();

    /// <summary>ARCH-03: nothing depends on the host or the migrator; they are composition roots that wire everything else.</summary>
    public static IArchRule NothingDependsOnHostOrMigrator =>
        Types().That().DoNotResideInNamespaceMatching(@"^Themis\.(Host|Migrator)(?:\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Themis\.(Host|Migrator)(?:\.|$)")
            .Because("ARCH-03: Themis.Host and Themis.Migrator reference everything and contain wiring only; nothing references them").WithoutRequiringPositiveResults();

    /// <summary>
    /// ARCH-04: a module's projects may reference Themis.Kernel.Primitives, Themis.Kernel.Contracts and other modules'
    /// Contracts projects, nothing else outside the module (brief, Reference rules; D-26).
    /// </summary>
    public static IEnumerable<(string Id, IArchRule Rule)> ModulesUseOnlyPrimitivesAndContracts(ArchModel architecture)
    {
        foreach (string module in ModuleRoots(architecture))
        {
            string own = Regex.Escape(module);
            yield return ($"ARCH-04 ({module}, kernel internals)", Types().That().ResideInNamespaceMatching($@"^{own}(?:\.|$)")
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^Themis\.Kernel\.(?!{SharedKernel}(?:\.|$)){Name}(?:\.|$)")
                .Because($"ARCH-04: {module} may depend on Themis.Kernel.Primitives and Themis.Kernel.Contracts only, never on a kernel capability").WithoutRequiringPositiveResults());

            yield return ($"ARCH-04 ({module}, other modules)", Types().That().ResideInNamespaceMatching($@"^{own}(?:\.|$)")
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^(?!{own}(?:\.|$)){ModuleRoot}\.{InternalLayers}(?:\.|$)")
                .Because($"ARCH-04: {module} may depend on other modules' Contracts projects only").WithoutRequiringPositiveResults());
        }
    }

    /// <summary>
    /// ARCH-05: layering inside each module. Contracts depends on no other layer; Domain only on Contracts;
    /// Application on Domain and Contracts; Api on Application and Contracts; Infrastructure never on Api.
    /// </summary>
    public static IEnumerable<(string Id, IArchRule Rule)> LayersDependDownwards(ArchModel architecture)
    {
        foreach (string module in ModuleRoots(architecture))
        {
            string m = Regex.Escape(module);
            yield return ($"ARCH-05 ({module}.Contracts)", LayerRule(m, module, "Contracts", InternalLayers));
            yield return ($"ARCH-05 ({module}.Domain)", LayerRule(m, module, "Domain", "(Application|Infrastructure|Api)"));
            yield return ($"ARCH-05 ({module}.Application)", LayerRule(m, module, "Application", "(Infrastructure|Api)"));
            yield return ($"ARCH-05 ({module}.Api)", LayerRule(m, module, "Api", "(Domain|Infrastructure)"));
            yield return ($"ARCH-05 ({module}.Infrastructure)", LayerRule(m, module, "Infrastructure", "Api"));
        }
    }

    /// <summary>ARCH-06: Primitives, Contracts and every module's Domain and Contracts are framework-free.</summary>
    public static IArchRule SharedTypesAreFrameworkFree =>
        Types().That().ResideInNamespaceMatching($@"^(Themis\.Kernel\.{SharedKernel}|{ModuleRoot}\.(Domain|Contracts))(?:\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(FrameworkNamespaces)
            .Because("ARCH-06: Primitives, Contracts, and module Domain and Contracts projects reference no web, persistence or database framework").WithoutRequiringPositiveResults();

    /// <summary>ARCH-07: public types exist only in Primitives, Contracts and module Contracts; host and migrator excepted.</summary>
    public static IArchRule PublicTypesOnlyInSharedSurface =>
        Types().That().ResideInNamespaceMatching(@"^Themis(?:\.|$)")
            .And().DoNotResideInNamespaceMatching($@"^(Themis\.Kernel\.{SharedKernel}|{ModuleRoot}\.Contracts)(?:\.|$)")
            .And().DoNotResideInNamespaceMatching(@"^Themis\.(Host|Migrator)(?:\.|$)")
            .And().DoNotHaveNameStartingWith("<")
            .Should().NotBePublic()
            .Because("ARCH-07: only Primitives, Contracts and module Contracts projects expose public types; everything else is internal (D-26)").WithoutRequiringPositiveResults();

    /// <summary>
    /// ARCH-08: kernel capabilities call each other through Themis.Kernel.Contracts only; Contracts depends on no
    /// capability; Primitives depends on nothing in Themis (brief, Repository layout).
    /// </summary>
    public static IEnumerable<(string Id, IArchRule Rule)> KernelCapabilitiesUseOnlyContracts(ArchModel architecture)
    {
        yield return ("ARCH-08 (Primitives)", Types().That().ResideInNamespaceMatching(@"^Themis\.Kernel\.Primitives(?:\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Themis\.(?!Kernel\.Primitives(?:\.|$))")
            .Because("ARCH-08: Themis.Kernel.Primitives depends on nothing else in Themis").WithoutRequiringPositiveResults());

        yield return ("ARCH-08 (Contracts)", Types().That().ResideInNamespaceMatching(@"^Themis\.Kernel\.Contracts(?:\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Themis\.(?!Kernel\.(Primitives|Contracts)(?:\.|$))")
            .Because("ARCH-08: Themis.Kernel.Contracts depends on Themis.Kernel.Primitives only").WithoutRequiringPositiveResults());

        foreach (string capability in KernelCapabilities(architecture))
        {
            string own = Regex.Escape(capability);
            yield return ($"ARCH-08 (Themis.Kernel.{capability})", Types().That().ResideInNamespaceMatching($@"^Themis\.Kernel\.{own}(?:\.|$)")
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^Themis\.Kernel\.(?!({SharedKernel}|{own})(?:\.|$)){Name}(?:\.|$)")
                .Because($"ARCH-08: Themis.Kernel.{capability} reaches other kernel capabilities through Themis.Kernel.Contracts only").WithoutRequiringPositiveResults());
        }
    }

    private static ArchRule<IType> LayerRule(string escapedModule, string module, string layer, string forbiddenLayers) =>
        Types().That().ResideInNamespaceMatching($@"^{escapedModule}\.{layer}(?:\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^{escapedModule}\.{forbiddenLayers}(?:\.|$)")
            .Because($"ARCH-05: {module}.{layer} must not depend on {module}.{forbiddenLayers}").WithoutRequiringPositiveResults();

    private static IEnumerable<Assembly> ThemisAssemblies(ArchModel architecture) =>
        architecture.Assemblies.Where(assembly => assembly.Name.StartsWith("Themis.", StringComparison.Ordinal)).OrderBy(assembly => assembly.Name, StringComparer.Ordinal);

    /// <summary>
    /// Module roots found in the architecture: Themis.Modules.&lt;Name&gt; and Themis.FixtureModule, from their layer
    /// namespaces. Throws when none is found (RR-84): a rule over an empty set of modules would pass on nothing.
    /// </summary>
    public static IReadOnlyList<string> ModuleRoots(ArchModel architecture) =>
        NotEmpty("module roots", architecture.Namespaces
            .Select(ns => Regex.Match(ns.FullName, $@"^({ModuleRoot})\.{Layers}(?:\.|$)"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList());

    /// <summary>
    /// Kernel capability names found in the architecture: every Themis.Kernel.&lt;Name&gt; except Primitives and
    /// Contracts. Throws when none is found (RR-84), like <see cref="ModuleRoots"/>.
    /// </summary>
    public static IReadOnlyList<string> KernelCapabilities(ArchModel architecture) =>
        NotEmpty("kernel capabilities", architecture.Namespaces
            .Select(ns => Regex.Match(ns.FullName, $@"^Themis\.Kernel\.(?!{SharedKernel}(?:\.|$))({Name})(?:\.|$)"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList());

    private static List<string> NotEmpty(string what, List<string> found) =>
        found.Count > 0 ? found : throw new InvalidOperationException($"The architecture holds no {what}; a rule over an empty set would pass on nothing (RR-84).");
}
