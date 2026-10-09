using System.Text.RegularExpressions;

namespace Themis.Architecture.Tests;

/// <summary>
/// Keeps code and the design snapshots in step (RR-08, accepted 2026-10-09): every rule, principle, decision,
/// invariant or posting event ID cited in code, tests or database scripts must exist in a snapshot under
/// docs/design, and every snapshot must carry the revision of the live document it was taken from.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class DesignReferenceTests
{
    private static readonly string[] SourceRoots = ["src", "tests", "db", "eng"];
    private static readonly string[] SourceExtensions = [".cs", ".sql", ".txt", ".csproj", ".props"];

    [GeneratedRegex(@"\b(?:R-[A-Z]{2,4}-\d{2}|[PDIE]-\d{2})\b")]
    private static partial Regex DesignId();

    [GeneratedRegex(@"^> \*\*Snapshot\*\* of the live document .+, revision \d+, taken \d{4}-\d{2}-\d{2}\.$", RegexOptions.Multiline)]
    private static partial Regex SnapshotHeader();

    [Fact]
    public void Every_design_id_cited_in_code_exists_in_a_snapshot()
    {
        string root = RepositoryRoot();
        HashSet<string> documented = Directory.GetFiles(Path.Combine(root, "docs", "design"), "*.md")
            .SelectMany(path => DesignId().Matches(File.ReadAllText(path)).Select(match => match.Value))
            .ToHashSet(StringComparer.Ordinal);

        List<string> unknown = [];
        foreach (string file in SourceFiles(root))
        {
            foreach (Match match in DesignId().Matches(File.ReadAllText(file)))
            {
                if (!documented.Contains(match.Value))
                {
                    unknown.Add($"{match.Value} in {Path.GetRelativePath(root, file)}");
                }
            }
        }

        Assert.True(documented.Count > 100, "The snapshots under docs/design look empty.");
        Assert.True(unknown.Count == 0, $"IDs cited in code but absent from docs/design:{Environment.NewLine}{string.Join(Environment.NewLine, unknown.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}");
    }

    [Fact]
    public void Every_snapshot_carries_the_revision_of_its_live_document()
    {
        string root = RepositoryRoot();
        string[] snapshots = Directory.GetFiles(Path.Combine(root, "docs", "design"), "*.md");

        Assert.Equal(3, snapshots.Length);
        foreach (string snapshot in snapshots)
        {
            Assert.True(SnapshotHeader().IsMatch(File.ReadAllText(snapshot)), $"{Path.GetFileName(snapshot)} has no revision-stamped snapshot header.");
        }
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        SourceRoots.Select(folder => Path.Combine(root, folder)).Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"));

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
