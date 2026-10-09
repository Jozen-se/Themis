using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchModel = ArchUnitNET.Domain.Architecture;

namespace Themis.Architecture.Tests;

/// <summary>
/// Loads two architectures once per test run: the production code (every Themis.*.dll next to the test assembly,
/// test assemblies excluded) and the violation fixtures compiled into this test assembly.
/// </summary>
public sealed class ArchitectureFixture
{
    public ArchitectureFixture()
    {
        string directory = AppContext.BaseDirectory;
        System.Reflection.Assembly[] production = Directory.GetFiles(directory, "Themis.*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(System.Reflection.Assembly.LoadFrom)
            .ToArray();

        Production = new ArchLoader().LoadAssemblies(production).Build();
        Fixtures = new ArchLoader().LoadAssemblies(typeof(ArchitectureFixture).Assembly).Build();
        ProductionAssemblyNames = production.Select(assembly => assembly.GetName().Name!).ToList();
    }

    public ArchModel Production { get; }

    public ArchModel Fixtures { get; }

    public IReadOnlyList<string> ProductionAssemblyNames { get; }

    /// <summary>Failed evaluation results. A rule whose predicate selects no type passes: an empty layer is not a violation.</summary>
    public static IReadOnlyList<string> Violations(IArchRule rule, ArchModel architecture) =>
        rule.Evaluate(architecture).Where(result => !result.Passed).Select(result => result.Description).ToList();
}
