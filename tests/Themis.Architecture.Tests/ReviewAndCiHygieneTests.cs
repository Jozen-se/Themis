using System.Text.RegularExpressions;

namespace Themis.Architecture.Tests;

/// <summary>
/// Repository-level items of milestone 2 step 2a that live outside the compiled code: pinned runners (RR-104), workflow
/// hygiene, the nightly long run and the security policy (RR-85), documents that cannot drift from their sources
/// (RR-69), and the review files of D-59 (REVIEW.md, CODEOWNERS, the pull request template). The compose job in CI
/// proves the development setup itself (RR-59, RR-65).
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class ReviewAndCiHygieneTests
{
    [GeneratedRegex(@"^\s*runs-on:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex RunsOn();

    [GeneratedRegex(@"^(?<indent>\s*)(?:-\s+)?run:\s*(?<value>.*)$")]
    private static partial Regex RunKey();

    [GeneratedRegex(@"\$\{\{[^}]*\}\}")]
    private static partial Regex Expression();

    [GeneratedRegex(@"head\.ref|head_ref|\.title|\.body|comment|label|\.message|\.name\b", RegexOptions.IgnoreCase)]
    private static partial Regex UntrustedEventText();

    [GeneratedRegex(@"^\s*-\s*cron:\s*""(?<minute>\d+) (?<hour>\d+) \* \* \*""\s*$", RegexOptions.Multiline)]
    private static partial Regex NightlyCron();

    [GeneratedRegex(@"`([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*)`")]
    private static partial Regex BacktickedName();

    [GeneratedRegex(@"PackageVersion Include=""([^""]+)""")]
    private static partial Regex PackageVersion();

    [GeneratedRegex(@"^- (?<title>Themis [^:]+): (?<url>https://claude\.ai/\S+) \(snapshot: (?<snapshot>docs/design/[a-z0-9-]+\.md)", RegexOptions.Multiline)]
    private static partial Regex ClaudeMdSnapshotLine();

    [GeneratedRegex(@"^> \*\*Snapshot\*\* of the live document \*[^*]+\* \((?<url>[^)]+)\), revision \d+", RegexOptions.Multiline)]
    private static partial Regex SnapshotUrl();

    [GeneratedRegex(@"^(\d+)\. ", RegexOptions.Multiline)]
    private static partial Regex NumberedItem();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|mailto:", RegexOptions.IgnoreCase)]
    private static partial Regex ContactAddress();

    [Fact]
    public void RR_104_every_workflow_job_runs_on_ubuntu_24_04_and_none_on_a_moving_label()
    {
        foreach (string workflow in Workflows())
        {
            MatchCollection runners = RunsOn().Matches(File.ReadAllText(workflow));
            Assert.NotEmpty(runners);
            foreach (Match runner in runners)
            {
                Assert.True(runner.Groups[1].Value == "ubuntu-24.04", $"{Path.GetFileName(workflow)} runs on {runner.Groups[1].Value}; every job runs on ubuntu-24.04 (RR-104).");
            }
        }
    }

    [Fact]
    public void RR_85_no_run_step_puts_a_branch_name_or_other_event_text_into_the_shell()
    {
        foreach (string workflow in Workflows())
        {
            foreach (string run in RunBlocks(File.ReadAllLines(workflow)))
            {
                foreach (Match expression in Expression().Matches(run))
                {
                    Assert.False(UntrustedEventText().IsMatch(expression.Value), $"{Path.GetFileName(workflow)} puts {expression.Value} into run:; pass it through an environment variable and quote it (RR-85).");
                }
            }
        }
    }

    [Fact]
    public void RR_85_the_run_block_reader_finds_an_expression_in_a_block_and_in_a_single_line()
    {
        string[] workflow =
        [
            "    steps:",
            "      - name: Push",
            "        run: |",
            "          git push origin \"HEAD:${{ github.event.pull_request.head.ref }}\"",
            "      - name: Dispatch",
            "        run: gh workflow run ci.yml --ref \"${{ github.head_ref }}\"",
        ];

        string[] blocks = [.. RunBlocks(workflow)];
        Assert.Equal(2, blocks.Length);
        Assert.All(blocks, block => Assert.Contains(Expression().Matches(block), expression => UntrustedEventText().IsMatch(expression.Value)));
    }

    [Fact]
    public void RR_85_the_invariant_long_run_is_scheduled_nightly_off_the_full_hour_with_5000_sequences_and_still_starts_by_hand()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "invariants-long-run.yml"));
        Match cron = NightlyCron().Match(text);
        Assert.True(cron.Success, "invariants-long-run.yml has no nightly schedule (RR-85).");
        Assert.NotEqual("0", cron.Groups["minute"].Value);
        Assert.Contains("workflow_dispatch:", text, StringComparison.Ordinal);
        Assert.Contains("THEMIS_PROPERTY_SEQUENCES: ${{ inputs.sequences || '5000' }}", text, StringComparison.Ordinal);
        Assert.Contains("name: Property runs (${{ inputs.sequences || '5000' }} sequences)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RR_85_SECURITY_md_sends_reports_to_private_vulnerability_reporting_and_names_no_contact_address()
    {
        string path = Path.Combine(RepositoryRoot(), "SECURITY.md");
        Assert.True(File.Exists(path), "SECURITY.md is missing at the repository root (RR-85).");
        string text = File.ReadAllText(path);
        Assert.Contains("private vulnerability reporting", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no bug bounty", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("this repository", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(ContactAddress().IsMatch(text), "SECURITY.md names an email address or other contact detail (RR-85).");
    }

    [Fact]
    public void RR_69_every_package_the_README_names_is_in_Directory_Packages_props_and_the_README_names_no_version()
    {
        string root = RepositoryRoot();
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        HashSet<string> central = PackageVersion().Matches(File.ReadAllText(Path.Combine(root, "Directory.Packages.props"))).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        string[] named = [.. ReadmePackages(readme)];
        Assert.True(named.Length >= 10, $"The README's package list looks short: {named.Length} packages.");
        string[] missing = [.. named.Where(package => !central.Contains(package))];
        Assert.True(missing.Length == 0, $"The README names packages that Directory.Packages.props does not hold: {string.Join(", ", missing)} (RR-69).");
        Assert.DoesNotContain("## Pinned versions", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void RR_69_the_README_package_reader_reports_a_package_missing_from_Directory_Packages_props()
    {
        const string readme = "The packages, by purpose:\n\n- Database: `Npgsql` and `Not.A.Real.Package`.\n\nAn architecture test checks it.";
        Assert.Equal(["Npgsql", "Not.A.Real.Package"], ReadmePackages(readme));
    }

    [Fact]
    public void RR_69_every_snapshot_header_uses_the_URL_CLAUDE_md_names_for_its_live_document()
    {
        string root = RepositoryRoot();
        MatchCollection lines = ClaudeMdSnapshotLine().Matches(File.ReadAllText(Path.Combine(root, "CLAUDE.md")));
        Assert.Equal(4, lines.Count);
        foreach (Match line in lines)
        {
            string snapshot = File.ReadAllText(Path.Combine(root, line.Groups["snapshot"].Value.Replace('/', Path.DirectorySeparatorChar)));
            Match header = SnapshotUrl().Match(snapshot);
            Assert.True(header.Success, $"{line.Groups["snapshot"].Value} has no snapshot header.");
            Assert.True(header.Groups["url"].Value == line.Groups["url"].Value, $"{line.Groups["snapshot"].Value} names {header.Groups["url"].Value}; CLAUDE.md names {line.Groups["url"].Value} (RR-69).");
        }
    }

    [Fact]
    public void D_59_REVIEW_md_holds_the_16_checklist_items_with_items_1_to_8_and_14_to_16_Important()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "REVIEW.md"));
        int[] items = [.. NumberedItem().Matches(text).Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];
        Assert.Equal(Enumerable.Range(1, 16), items);
        Assert.Contains("Any failure of items 1 to 8 and 14 to 16 is Important.", text, StringComparison.Ordinal);
        Assert.Contains("RR-91", text, StringComparison.Ordinal);
        Assert.Contains("R-SEC-03", text, StringComparison.Ordinal);
        Assert.Contains("D-60", text, StringComparison.Ordinal);
    }

    [Fact]
    public void D_59_CODEOWNERS_and_the_pull_request_template_claim_no_review_that_does_not_exist()
    {
        string root = RepositoryRoot();
        string codeOwners = File.ReadAllText(Path.Combine(root, ".github", "CODEOWNERS"));
        Assert.Contains("D-59", codeOwners, StringComparison.Ordinal);
        Assert.DoesNotContain("requires a code owner review", codeOwners, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("always get the owner's explicit review", codeOwners, StringComparison.OrdinalIgnoreCase);

        string template = File.ReadAllText(Path.Combine(root, ".github", "pull_request_template.md"));
        Assert.Contains("**Step brief:**", template, StringComparison.Ordinal);
        Assert.Contains("- [ ] CI is green on the head commit (tick only after the checks passed)", template, StringComparison.Ordinal);
        Assert.Contains("(local, not CI)", template, StringComparison.Ordinal);
        Assert.Contains("## Deviations and out-of-scope findings", template, StringComparison.Ordinal);
        Assert.Contains("## Owner checklist", template, StringComparison.Ordinal);
        Assert.Contains("- [ ] The required checks are green on the head commit", template, StringComparison.Ordinal);
        Assert.Contains("- [ ] The reviewer's summary names that head commit", template, StringComparison.Ordinal);
        Assert.Contains("- [ ] Every conversation is resolved", template, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner sign-off", template, StringComparison.Ordinal);
        Assert.DoesNotContain("explicit review", template, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The value of every run: key, a block scalar (| or >) with its indented lines included.</summary>
    private static IEnumerable<string> RunBlocks(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            Match key = RunKey().Match(lines[i]);
            if (!key.Success)
            {
                continue;
            }

            string value = key.Groups["value"].Value.Trim();
            if (value is not ("|" or ">" or "|-" or ">-"))
            {
                yield return value;
                continue;
            }

            int indent = key.Groups["indent"].Value.Length;
            List<string> block = [];
            while (i + 1 < lines.Length && (string.IsNullOrWhiteSpace(lines[i + 1]) || lines[i + 1].Length - lines[i + 1].TrimStart().Length > indent))
            {
                block.Add(lines[++i]);
            }

            yield return string.Join('\n', block);
        }
    }

    /// <summary>The backticked names of the README's package list, from "The packages, by purpose:" to the next paragraph.</summary>
    private static IEnumerable<string> ReadmePackages(string readme)
    {
        int start = readme.IndexOf("The packages, by purpose:", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no package list.");
        int end = readme.IndexOf("\nAn architecture test", start, StringComparison.Ordinal);
        string list = end < 0 ? readme[start..] : readme[start..end];
        return BacktickedName().Matches(list).Select(match => match.Groups[1].Value);
    }

    private static string[] Workflows() => Directory.GetFiles(Path.Combine(RepositoryRoot(), ".github", "workflows"), "*.yml");

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
