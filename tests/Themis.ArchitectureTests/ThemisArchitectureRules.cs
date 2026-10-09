using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Themis.ArchitectureTests;

/// <summary>
/// The module boundary rules of D-26, as code. Each rule has an identifier (ARCH-nn) that failure messages carry.
/// The rules are generic: a new module under <c>Themis.Modules.&lt;Name&gt;</c> is covered without editing this file.
/// Changing a rule here is a reviewed architectural decision.
/// </summary>
internal static class ThemisArchitectureRules
{
    private const string Layers = "(Contracts|Domain|Application|Infrastructure|Api)";
    private const string InternalLayers = "(Domain|Application|Infrastructure|Api)";
    private const string ModuleName = "[A-Za-z0-9]+";

    /// <summary>Namespaces any production code is forbidden to depend on from Domain and Contracts (ARCH-06).</summary>
    private const string FrameworkNamespaces = @"^(Microsoft\.AspNetCore|Microsoft\.EntityFrameworkCore|Npgsql)(\.|$)";

    /// <summary>ARCH-01: every type in a Themis assembly lives in a namespace that starts with the assembly's name.</summary>
    public static IEnumerable<(string Id, IArchRule Rule)> NamespacesFollowAssemblyNames(Architecture architecture)
    {
        foreach (Assembly assembly in ThemisAssemblies(architecture))
        {
            string escaped = Regex.Escape(assembly.Name);
            yield return ($"ARCH-01 ({assembly.Name})", Types().That().ResideInAssembly(assembly).And().DoNotHaveNameStartingWith("<")
                .Should().ResideInNamespaceMatching($"^{escaped}(\\.|$)")
                .Because($"ARCH-01: types in {assembly.Name} must reside in namespace {assembly.Name} or below"));
        }
    }

    /// <summary>ARCH-02: the kernel knows nothing about any module or the host.</summary>
    public static IArchRule KernelDependsOnNoModuleOrHost =>
        Types().That().ResideInNamespaceMatching(@"^Themis\.Kernel(\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Themis\.(Modules|Host)(\.|$)")
            .Because("ARCH-02: the kernel depends on no business module and not on the host (D-26)");

    /// <summary>ARCH-03: nothing depends on the host; it is the composition root and wires everything else.</summary>
    public static IArchRule NothingDependsOnHost =>
        Types().That().DoNotResideInNamespaceMatching(@"^Themis\.Host(\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Themis\.Host(\.|$)")
            .Because("ARCH-03: the host is the composition root; no kernel or module code references it");

    /// <summary>
    /// ARCH-04: a module reaches the kernel only through Themis.Kernel.Contracts and another module only through that
    /// module's Contracts project (D-26, D-39).
    /// </summary>
    public static IEnumerable<(string Id, IArchRule Rule)> ModulesUseOnlyContracts(Architecture architecture)
    {
        foreach (string module in ModuleNames(architecture))
        {
            string own = Regex.Escape(module);
            yield return ($"ARCH-04 ({module}, kernel internals)", Types().That().ResideInNamespaceMatching($@"^Themis\.Modules\.{own}(\.|$)")
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^Themis\.Kernel\.{InternalLayers}(\.|$)")
                .Because($"ARCH-04: module {module} may depend on Themis.Kernel.Contracts only, never on kernel internals"));

            yield return ($"ARCH-04 ({module}, other modules)", Types().That().ResideInNamespaceMatching($@"^Themis\.Modules\.{own}(\.|$)")
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^Themis\.Modules\.(?!{own}(\.|$)){ModuleName}\.{InternalLayers}(\.|$)")
                .Because($"ARCH-04: module {module} may depend on other modules' Contracts projects only"));
        }
    }

    /// <summary>
    /// ARCH-05: layering inside the kernel and inside each module. Contracts depends on no other layer; Domain only on
    /// Contracts; Application on Domain and Contracts; Api on Application and Contracts; Infrastructure never on Api.
    /// </summary>
    public static IEnumerable<(string Id, IArchRule Rule)> LayersDependDownwards(Architecture architecture)
    {
        foreach (string unit in UnitNamespaces(architecture))
        {
            string u = Regex.Escape(unit);
            yield return ($"ARCH-05 ({unit}.Contracts)", LayerRule(u, unit, "Contracts", InternalLayers));
            yield return ($"ARCH-05 ({unit}.Domain)", LayerRule(u, unit, "Domain", "(Application|Infrastructure|Api)"));
            yield return ($"ARCH-05 ({unit}.Application)", LayerRule(u, unit, "Application", "(Infrastructure|Api)"));
            yield return ($"ARCH-05 ({unit}.Api)", LayerRule(u, unit, "Api", "(Domain|Infrastructure)"));
            yield return ($"ARCH-05 ({unit}.Infrastructure)", LayerRule(u, unit, "Infrastructure", "Api"));
        }
    }

    /// <summary>ARCH-06: Domain and Contracts are framework-free (no ASP.NET Core, EF Core or Npgsql).</summary>
    public static IArchRule DomainAndContractsAreFrameworkFree =>
        Types().That().ResideInNamespaceMatching($@"^Themis\.(Kernel|Modules\.{ModuleName})\.(Domain|Contracts)(\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(FrameworkNamespaces)
            .Because("ARCH-06: Domain and Contracts projects reference no web, persistence or database framework");

    /// <summary>ARCH-07: public types exist only in Contracts projects; the host is the one exception.</summary>
    public static IArchRule PublicTypesOnlyInContracts =>
        Types().That().ResideInNamespaceMatching(@"^Themis(\.|$)")
            .And().DoNotResideInNamespaceMatching($@"^Themis\.(Kernel|Modules\.{ModuleName})\.Contracts(\.|$)")
            .And().DoNotResideInNamespaceMatching(@"^Themis\.Host(\.|$)")
            .And().DoNotHaveNameStartingWith("<")
            .Should().NotBePublic()
            .Because("ARCH-07: only Contracts projects expose public types; everything else is internal (D-26)");

    private static TypesShouldConjunctionWithDescription LayerRule(string escapedUnit, string unit, string layer, string forbiddenLayers) =>
        Types().That().ResideInNamespaceMatching($@"^{escapedUnit}\.{layer}(\.|$)")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching($@"^{escapedUnit}\.{forbiddenLayers}(\.|$)")
            .Because($"ARCH-05: {unit}.{layer} must not depend on {unit}.{forbiddenLayers}");

    private static IEnumerable<Assembly> ThemisAssemblies(Architecture architecture) =>
        architecture.Assemblies.Where(assembly => assembly.Name.StartsWith("Themis.", StringComparison.Ordinal)).OrderBy(assembly => assembly.Name, StringComparer.Ordinal);

    /// <summary>Module names found in the architecture, from namespaces of the form Themis.Modules.&lt;Name&gt;.&lt;Layer&gt;.</summary>
    public static IReadOnlyList<string> ModuleNames(Architecture architecture) =>
        architecture.Namespaces
            .Select(ns => Regex.Match(ns.FullName, $@"^Themis\.Modules\.({ModuleName})\.{Layers}(\.|$)"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Root namespaces of the layered units: the kernel plus every module found.</summary>
    public static IReadOnlyList<string> UnitNamespaces(Architecture architecture) =>
        ["Themis.Kernel", .. ModuleNames(architecture).Select(name => $"Themis.Modules.{name}")];
}
