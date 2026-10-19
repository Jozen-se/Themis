using System.Text.RegularExpressions;

namespace Themis.Architecture.Tests;

/// <summary>
/// Repository-level fixes of the code review of 2026-10-18 that live outside the compiled code: the zero-test guard in
/// the workflows (RR-41), the run path in the compose comment (RR-49) and the GitHub Actions pinned by commit (RR-50).
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class RepositoryHygieneTests
{
    [GeneratedRegex(@"--results-directory (\S+)")]
    private static partial Regex ResultsDirectory();

    [GeneratedRegex(@"require-tests\.sh (\S+)")]
    private static partial Regex GuardStep();

    [GeneratedRegex(@"^\s*uses:\s*(\S+)@([0-9a-f]+)\s*#\s*v\d+\.\d+\.\d+\s*$", RegexOptions.Multiline)]
    private static partial Regex PinnedAction();

    [GeneratedRegex(@"^\s*uses:", RegexOptions.Multiline)]
    private static partial Regex AnyAction();

    [GeneratedRegex(@"dotnet run --project ([^\s`]+)")]
    private static partial Regex RunProject();

    [Fact]
    public void RR_41_every_test_step_of_a_workflow_is_followed_by_the_zero_test_guard_on_its_results_directory()
    {
        string root = RepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, ".github", "scripts", "require-tests.sh")), "The zero-test guard script is missing.");
        foreach (string workflow in Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"))
        {
            string text = File.ReadAllText(workflow);
            HashSet<string> guarded = GuardStep().Matches(text).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            foreach (Match results in ResultsDirectory().Matches(text))
            {
                Assert.True(guarded.Contains(results.Groups[1].Value), $"{Path.GetFileName(workflow)} writes test results to {results.Groups[1].Value} without a require-tests.sh step on that directory (RR-41).");
            }
        }

        Assert.Contains("--results-directory", File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void RR_49_every_run_path_in_the_compose_file_names_a_project_that_exists()
    {
        string root = RepositoryRoot();
        MatchCollection runs = RunProject().Matches(File.ReadAllText(Path.Combine(root, "compose.yaml")));
        Assert.NotEmpty(runs);
        foreach (Match run in runs)
        {
            string project = Path.Combine(root, run.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(project) && Directory.GetFiles(project, "*.csproj").Length == 1, $"compose.yaml names {run.Groups[1].Value}, which holds no project (RR-49).");
        }
    }

    [Fact]
    public void RR_50_every_GitHub_Action_is_pinned_by_a_full_commit_SHA_with_its_version_in_a_comment()
    {
        string root = RepositoryRoot();
        foreach (string workflow in Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"))
        {
            string text = File.ReadAllText(workflow);
            int uses = AnyAction().Count(text);
            MatchCollection pinned = PinnedAction().Matches(text);
            Assert.True(uses > 0 && uses == pinned.Count, $"{Path.GetFileName(workflow)} has {uses} uses: lines and {pinned.Count} pinned by a commit SHA with a version comment (RR-50).");
            foreach (Match pin in pinned)
            {
                Assert.True(pin.Groups[2].Value.Length == 40, $"{Path.GetFileName(workflow)} pins {pin.Groups[1].Value} with a short hash (RR-50).");
            }
        }
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
