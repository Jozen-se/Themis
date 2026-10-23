namespace Themis.Architecture.Tests;

/// <summary>
/// D-60: session_replication_role = replica silences ordinary triggers, the immutability backstop included. Only the
/// test cleanup seam on PostgresFixture sets it, as the superuser; no file under src names it.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class SessionReplicationRoleTests
{
    private const string Setting = "session_replication_role";

    [Fact]
    public void D_60_no_file_in_src_names_session_replication_role()
    {
        string root = RepositoryRoot();
        string[] files = [.. Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))];

        string[] offenders = [.. files.Where(path => File.ReadAllText(path).Contains(Setting, StringComparison.OrdinalIgnoreCase)).Select(path => Path.GetRelativePath(root, path))];

        Assert.True(files.Length > 100, $"Only {files.Length} files found in src; the scan looks broken.");
        Assert.Empty(offenders);
        // The scan finds the term where it is allowed: the test cleanup seam.
        Assert.Contains(Setting, File.ReadAllText(Path.Combine(root, "tests", "Themis.Integration.Tests", "PostgresFixture.cs")), StringComparison.Ordinal);
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
