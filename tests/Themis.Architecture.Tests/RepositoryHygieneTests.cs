using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Themis.Architecture.Tests;

/// <summary>
/// Repository-level fixes of the code review of 2026-10-18 that live outside the compiled code: the zero-test guard in
/// the workflows (RR-41) and its category map (RR-84), the run path in the compose comment (RR-49) and the GitHub
/// Actions pinned by commit (RR-50).
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class RepositoryHygieneTests
{
    [GeneratedRegex(@"--results-directory ""?(TestResults/[A-Za-z-]+)/\$project""?")]
    private static partial Regex ResultsDirectory();

    [GeneratedRegex(@"--results-directory")]
    private static partial Regex AnyResultsDirectory();

    [GeneratedRegex(@"require-tests\.sh (\w+) (\S+)")]
    private static partial Regex GuardStep();

    [GeneratedRegex(@"^\s+(\w+)\) echo ""([^""]+)"" ;;\s*$", RegexOptions.Multiline)]
    private static partial Regex MapLine();

    [GeneratedRegex(@"Trait\(""Category"", ""(\w+)""\)")]
    private static partial Regex CategoryTrait();

    [GeneratedRegex(@"""tests\\(?:[^""\\]+\\)*([^""\\]+\.Tests)\.csproj""")]
    private static partial Regex SolutionTestProject();

    [GeneratedRegex(@"^\s*uses:\s*(\S+)@([0-9a-f]+)\s*#\s*v\d+\.\d+\.\d+\s*$", RegexOptions.Multiline)]
    private static partial Regex PinnedAction();

    [GeneratedRegex(@"^\s*uses:", RegexOptions.Multiline)]
    private static partial Regex AnyAction();

    [GeneratedRegex(@"dotnet run --project ([^\s`]+)")]
    private static partial Regex RunProject();

    [Fact]
    public void RR_41_RR_84_every_test_step_of_a_workflow_writes_per_project_results_that_the_zero_test_guard_checks_for_its_category()
    {
        string root = RepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, ".github", "scripts", "require-tests.sh")), "The zero-test guard script is missing.");
        int steps = 0;
        foreach (string workflow in Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"))
        {
            string text = File.ReadAllText(workflow);
            Dictionary<string, string> guarded = GuardStep().Matches(text).ToDictionary(match => match.Groups[2].Value, match => match.Groups[1].Value, StringComparer.Ordinal);
            MatchCollection results = ResultsDirectory().Matches(text);
            // Every --results-directory is a per-project one: TestResults/<category>/$project (RR-84).
            Assert.Equal(AnyResultsDirectory().Count(text), results.Count);
            foreach (Match result in results)
            {
                steps++;
                string directory = result.Groups[1].Value;
                Assert.True(guarded.TryGetValue(directory, out string? category), $"{Path.GetFileName(workflow)} writes test results under {directory} without a require-tests.sh step on that root (RR-41).");
                Assert.Contains($"require-tests.sh --projects {category})", text, StringComparison.Ordinal);
                Assert.Contains($"--filter-trait \"Category={category}\"", text, StringComparison.Ordinal);
            }
        }

        Assert.True(steps >= 5, $"Only {steps} test steps found in the workflows; the scan looks broken.");
    }

    [Fact]
    public void RR_84_every_test_project_of_the_solution_and_every_category_in_use_is_in_the_guard_map()
    {
        string root = RepositoryRoot();
        Dictionary<string, string[]> map = GuardMap(root);
        Assert.Equal(["Architecture", "Integration", "Invariant", "Unit"], map.Keys.Order(StringComparer.Ordinal));

        string[] projects = [.. SolutionTestProject().Matches(File.ReadAllText(Path.Combine(root, "Themis.sln"))).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal)];
        Assert.Equal(["Themis.Architecture.Tests", "Themis.Integration.Tests", "Themis.Invariants.Tests", "Themis.Kernel.Tests"], projects);

        List<string> missing = [];
        foreach (string project in projects)
        {
            string[] categories = [.. Directory.EnumerateFiles(Path.Combine(root, "tests", project), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
                .SelectMany(path => CategoryTrait().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
                .Distinct(StringComparer.Ordinal)];
            if (categories.Length == 0)
            {
                missing.Add($"{project} uses no Category value");
            }

            foreach (string category in categories)
            {
                if (!map.TryGetValue(category, out string[]? mapped) || !mapped.Contains(project, StringComparer.Ordinal))
                {
                    missing.Add($"{category} in {project}");
                }
            }
        }

        Assert.True(missing.Count == 0, $"Pairs that would run nowhere; add them to the map of require-tests.sh (RR-84): {string.Join(", ", missing)}");
    }

    [Fact]
    public void RR_84_the_guard_fails_a_pair_that_executed_no_test_and_passes_when_every_pair_did()
    {
        string root = RepositoryRoot();
        string results = Path.Combine(Path.GetTempPath(), "themis-guard-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteTrx(Path.Combine(results, "Themis.Integration.Tests"), executed: 3);
            WriteTrx(Path.Combine(results, "Themis.Invariants.Tests"), executed: 0);
            Assert.Equal(1, RunGuard(root, "Invariant", results));

            WriteTrx(Path.Combine(results, "Themis.Invariants.Tests"), executed: 2);
            Assert.Equal(0, RunGuard(root, "Invariant", results));

            Directory.Delete(Path.Combine(results, "Themis.Invariants.Tests"), recursive: true);
            Assert.Equal(1, RunGuard(root, "Invariant", results));
            Assert.Equal(1, RunGuard(root, "NoSuchCategory", results));
        }
        finally
        {
            if (Directory.Exists(results))
            {
                Directory.Delete(results, recursive: true);
            }
        }
    }

    /// <summary>The category map of require-tests.sh: each "Category) echo "projects" ;;" line.</summary>
    private static Dictionary<string, string[]> GuardMap(string root) =>
        MapLine().Matches(File.ReadAllText(Path.Combine(root, ".github", "scripts", "require-tests.sh")))
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static void WriteTrx(string directory, int executed)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "results.trx"), $"<TestRun><ResultSummary><Counters total=\"{executed}\" executed=\"{executed}\" passed=\"{executed}\" /></ResultSummary></TestRun>");
    }

    private static int RunGuard(string root, string category, string results)
    {
        ProcessStartInfo start = new("bash") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        start.ArgumentList.Add(Path.Combine(root, ".github", "scripts", "require-tests.sh"));
        start.ArgumentList.Add(category);
        start.ArgumentList.Add(results);
        using Process process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
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
