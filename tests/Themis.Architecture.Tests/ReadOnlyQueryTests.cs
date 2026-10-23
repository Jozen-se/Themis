namespace Themis.Architecture.Tests;

/// <summary>RR-81: in src only the query dispatcher opens a read-only unit of work; the contract and its implementation declare it.</summary>
[Trait("Category", "Architecture")]
public sealed class ReadOnlyQueryTests
{
    [Fact]
    public void RR_81_only_the_query_dispatcher_calls_BeginReadOnlyAsync()
    {
        string root = RepositoryRoot();
        string[] callers = [.. Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .Where(path => File.ReadAllText(path).Contains("BeginReadOnlyAsync(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];

        Assert.Equal(
            [
                "src/Kernel/Themis.Kernel.Contracts/Persistence/IUnitOfWork.cs",
                "src/Kernel/Themis.Kernel.Persistence/UnitOfWork/UnitOfWorkFactory.cs",
                "src/Kernel/Themis.Kernel.Pipeline/Dispatch/QueryDispatcher.cs",
            ],
            callers);
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
