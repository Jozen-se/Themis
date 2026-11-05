using System.Text.RegularExpressions;

namespace Themis.Architecture.Tests;

/// <summary>
/// Repository-level items of milestone 2 step 2a that live outside the compiled code: pinned runners (RR-104), workflow
/// hygiene, the nightly long run and the security policy (RR-85), documents that cannot drift from their sources
/// (RR-69), and the review files of D-59 (REVIEW.md, CODEOWNERS, the pull request template). The compose job in CI
/// proves the development setup itself (RR-59, RR-65). Milestone 2 step 4: the OpenAPI breaking-change check (RR-10).
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

    [GeneratedRegex(@"^.*\bgh workflow run (?<file>\S+).*$", RegexOptions.Multiline)]
    private static partial Regex WorkflowDispatchCall();

    [GeneratedRegex(@"^  workflow_dispatch:", RegexOptions.Multiline)]
    private static partial Regex WorkflowDispatchTrigger();

    [GeneratedRegex(@"^(?<fence>`{3,})markdown$")]
    private static partial Regex MarkdownFence();

    [GeneratedRegex(@"^  (?<job>[A-Za-z0-9_-]+):\s*$")]
    private static partial Regex JobKey();

    [GeneratedRegex(@"^\s*ports:\s*$", RegexOptions.Multiline)]
    private static partial Regex PortsKey();

    [GeneratedRegex(@"^\s*-\s*""?(?<binding>[^""\s]+)""?\s*$")]
    private static partial Regex ListEntry();

    [GeneratedRegex(@"^  pull_request:\s*\n    types: \[(?<types>[^\]]*)\]\s*$", RegexOptions.Multiline)]
    private static partial Regex PullRequestTypes();

    [GeneratedRegex(@"^\s*permissions:", RegexOptions.Multiline)]
    private static partial Regex PermissionsKey();

    [GeneratedRegex(@"^  OASDIFF_VERSION: ""(?<version>[^""]+)""\s*$", RegexOptions.Multiline)]
    private static partial Regex OasdiffVersion();

    [Fact]
    public void RR_79_every_port_the_compose_stack_publishes_is_bound_to_127_0_0_1_only()
    {
        string[] bindings = [.. PublishedPorts(File.ReadAllLines(Path.Combine(RepositoryRoot(), "compose.yaml")))];

        Assert.True(bindings.Length >= 2, $"compose.yaml publishes {bindings.Length} ports; Postgres and Keycloak are expected.");
        Assert.All(bindings, binding => Assert.True(binding.StartsWith("127.0.0.1:", StringComparison.Ordinal), $"compose.yaml publishes {binding} beyond the loopback interface (RR-79)."));
    }

    [Fact]
    public void RR_79_the_port_reader_reports_a_binding_on_all_interfaces()
    {
        string[] compose =
        [
            "services:",
            "  postgres:",
            "    ports:",
            "      - \"127.0.0.1:5432:5432\"",
            "  keycloak:",
            "    ports:",
            "      - \"8080:8080\"",
            "    volumes:",
            "      - keycloak-data:/opt/keycloak/data",
        ];

        Assert.Equal(["127.0.0.1:5432:5432", "8080:8080"], PublishedPorts(compose));
    }

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
    public void RR_134_every_workflow_the_lock_files_workflow_dispatches_declares_workflow_dispatch_and_is_dispatched_on_the_full_branch_ref()
    {
        // RR-134: "DCO sign-off", "Build and test" and "License scan" reach a lock-file head only through these dispatches.
        // A workflow without the trigger answers HTTP 422 at run time, with this pull request's CI still green; a short ref
        // could resolve to a tag of the same name and attach the check runs to another commit.
        string directory = Path.Combine(RepositoryRoot(), ".github", "workflows");
        MatchCollection calls = WorkflowDispatchCall().Matches(File.ReadAllText(Path.Combine(directory, "dependabot-lock-files.yml")));
        // Every call is collected first, whatever its arguments, so a dispatch without --ref cannot escape the check.
        Assert.Equal(["ci.yml", "dco.yml"], calls.Select(call => call.Groups["file"].Value).Order(StringComparer.Ordinal));
        foreach (Match call in calls)
        {
            string file = call.Groups["file"].Value;
            Assert.True(WorkflowDispatchTrigger().IsMatch(File.ReadAllText(Path.Combine(directory, file))), $"{file} is dispatched by dependabot-lock-files.yml but declares no workflow_dispatch trigger (RR-134).");
            Assert.Contains("--ref \"refs/heads/${HEAD_REF}\"", call.Value, StringComparison.Ordinal);
        }
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
        // D-59 as amended 2026-10-24: review is tiered. D-68 (note of 2026-11-05, RR-139): the planner's conformance result
        // names the head commit of every pull request, and full tier also needs the reviewer's summary of it.
        Assert.Contains("- **Review tier (D-59):**", template, StringComparison.Ordinal);
        Assert.Contains("- [ ] Full tier: the reviewer's summary names that head commit", template, StringComparison.Ordinal);
        Assert.Contains("- [ ] Every pull request: the planner's conformance result names that head commit", template, StringComparison.Ordinal);
        Assert.DoesNotContain("Light tier:", template, StringComparison.Ordinal);
        // RR-139: a rule gap or a proposed change goes to the planner, who brings it to the owner (sessions, D-68); other
        // contributors open a Rule gap issue (owner-approved wording, 2026-11-05).
        Assert.Contains("goes to the planner, who brings it to the owner (sessions, D-68); other contributors open a Rule gap issue", template, StringComparison.Ordinal);

        // RR-139: CONTRIBUTING.md carries the same route, links the issue form, and takes D-59's wording of what the
        // owner reads himself.
        string contributing = string.Join(' ', File.ReadAllLines(Path.Combine(root, "CONTRIBUTING.md")).Select(line => line.Trim()));
        Assert.Contains("goes to the planner, who brings it to the owner (sessions, D-68); other contributors open a Rule gap issue", contributing, StringComparison.Ordinal);
        Assert.Contains(".github/ISSUE_TEMPLATE/rule-gap.yml", contributing, StringComparison.Ordinal);
        Assert.Contains("the owner reads ledger, inventory, posting profile and migration changes himself (D-59)", contributing, StringComparison.Ordinal);
        Assert.DoesNotContain("explicit review", contributing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("- [ ] Every conversation is resolved", template, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner sign-off", template, StringComparison.Ordinal);
        Assert.DoesNotContain("explicit review", template, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RR_141_CLAUDE_md_equals_the_CLAUDE_md_block_of_the_Build_Brief_snapshot()
    {
        // RR-141: CLAUDE.md is the Build Brief's CLAUDE.md section, copied; a docs pull request refreshes both together.
        string root = RepositoryRoot();
        string[] brief = File.ReadAllText(Path.Combine(root, "docs", "design", "themis-build-brief.md")).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int section = Array.IndexOf(brief, "## CLAUDE.md for the repository");
        Assert.True(section >= 0, "The Build Brief snapshot has no \"## CLAUDE.md for the repository\" section (RR-141).");
        // The converter makes the fence longer than any backtick run inside the block, so the block ends at the first
        // line that repeats its opening fence exactly.
        int open = Array.FindIndex(brief, section, line => MarkdownFence().IsMatch(line));
        string fence = open < 0 ? "" : MarkdownFence().Match(brief[open]).Groups["fence"].Value;
        int close = open < 0 ? -1 : Array.IndexOf(brief, fence, open + 1);
        Assert.True(open > section && close > open, "The Build Brief's CLAUDE.md section holds no ```markdown block (RR-141).");
        string block = string.Join('\n', brief[(open + 1)..close]);

        string claude = File.ReadAllText(Path.Combine(root, "CLAUDE.md")).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        Assert.True(block == claude, "CLAUDE.md differs from the CLAUDE.md block of docs/design/themis-build-brief.md; refresh them together from the released Build Brief (RR-141).");
    }

    [Fact]
    public void RR_141_CI_runs_the_snapshot_converter_fixture_test()
    {
        string root = RepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "eng", "snapshots", "snapshot.py")), "eng/snapshots/snapshot.py is missing (RR-141).");
        Assert.True(File.Exists(Path.Combine(root, "eng", "snapshots", "test_snapshot.py")), "eng/snapshots/test_snapshot.py is missing (RR-141).");
        // The step must be a live run value (not a comment), in the build-and-test job whose "Build and test" check is
        // required, with no continue-on-error on the step or the job.
        string[] ci = File.ReadAllLines(Path.Combine(root, ".github", "workflows", "ci.yml"));
        const string command = "python3 -m unittest discover --start-directory eng/snapshots --pattern 'test_*.py' --verbose";
        Assert.Contains(command, RunBlocks(ci));
        int run = Array.FindIndex(ci, line => RunKey().Match(line) is { Success: true } key && key.Groups["value"].Value.Trim() == command);
        int job = Array.FindLastIndex(ci, run, line => JobKey().IsMatch(line));
        Assert.True(job >= 0 && JobKey().Match(ci[job]).Groups["job"].Value == "build-and-test", "The snapshot converter test does not run in the build-and-test job (RR-141).");
        int stepStart = Array.FindLastIndex(ci, run, line => line.StartsWith("      - ", StringComparison.Ordinal));
        int stepEnd = Array.FindIndex(ci, run + 1, line => line.StartsWith("      - ", StringComparison.Ordinal) || JobKey().IsMatch(line));
        string[] step = ci[stepStart..(stepEnd < 0 ? ci.Length : stepEnd)];
        Assert.DoesNotContain(step, line => line.TrimStart().StartsWith("continue-on-error", StringComparison.Ordinal));
        Assert.DoesNotContain(step, line => line.TrimStart().StartsWith("if:", StringComparison.Ordinal));
        int jobEnd = Array.FindIndex(ci, job + 1, line => JobKey().IsMatch(line));
        Assert.DoesNotContain(ci[job..(jobEnd < 0 ? ci.Length : jobEnd)], line => line.StartsWith("    continue-on-error", StringComparison.Ordinal) || line.StartsWith("    if:", StringComparison.Ordinal));
    }

    [Fact]
    public void RR_10_the_OpenAPI_check_runs_on_pull_requests_reruns_on_label_changes_and_holds_read_only_permissions()
    {
        string text = File.ReadAllText(OpenApiWorkflow());
        Match types = PullRequestTypes().Match(text);
        Assert.True(types.Success, "openapi.yml does not run on pull_request with a types list (RR-10).");
        string[] events = [.. types.Groups["types"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];
        Assert.Equal(["labeled", "opened", "reopened", "synchronize", "unlabeled"], events);
        Assert.DoesNotContain("pull_request_target", text, StringComparison.Ordinal);

        // GITHUB_TOKEN only, read-only: one permissions block, contents: read, no write anywhere, no other secret.
        Assert.Equal(1, PermissionsKey().Count(text));
        Assert.Contains("permissions:\n  contents: read\n", text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain(": write", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RR_10_oasdiff_is_pinned_by_version_and_module_checksum_and_its_licence_is_recorded_in_eng_licenses()
    {
        string root = RepositoryRoot();
        string text = File.ReadAllText(OpenApiWorkflow());
        Match version = OasdiffVersion().Match(text);
        Assert.True(version.Success, "openapi.yml pins no OASDIFF_VERSION (RR-10).");
        Assert.Matches(@"^\d+\.\d+\.\d+$", version.Groups["version"].Value);
        Assert.Matches(@"(?m)^  OASDIFF_MODULE_SUM: ""h1:[A-Za-z0-9+/]{43}=""\s*$", text);
        Assert.Matches(@"(?m)^  OASDIFF_GO_TOOLCHAIN: ""go\d+\.\d+\.\d+""\s*$", text);

        string[] run = [.. RunBlocks(File.ReadAllLines(OpenApiWorkflow()))];
        string install = Assert.Single(run, block => block.Contains("go install", StringComparison.Ordinal));
        // The module checksum is compared before anything is built, the checksum database is the public one, and the toolchain is pinned.
        Assert.Contains("go mod download -json \"github.com/oasdiff/oasdiff@v${OASDIFF_VERSION}\"", install, StringComparison.Ordinal);
        Assert.Contains("if [ \"$sum\" != \"${OASDIFF_MODULE_SUM}\" ]; then", install, StringComparison.Ordinal);
        Assert.Contains("GOSUMDB=sum.golang.org", install, StringComparison.Ordinal);
        Assert.Contains("GOTOOLCHAIN=\"${OASDIFF_GO_TOOLCHAIN}\"", install, StringComparison.Ordinal);
        Assert.True(install.IndexOf("!= \"${OASDIFF_MODULE_SUM}\"", StringComparison.Ordinal) < install.IndexOf("go install", StringComparison.Ordinal), "openapi.yml builds oasdiff before it checks the module checksum (RR-10).");
        Assert.DoesNotContain("GOFLAGS=-insecure", install, StringComparison.Ordinal);

        // D-38: not a NuGet package, so the licence is confirmed by hand and recorded with the pinned version.
        string record = File.ReadAllText(Path.Combine(root, "eng", "licenses", "oasdiff.md"));
        Assert.Contains($"| Version | {version.Groups["version"].Value} |", record, StringComparison.Ordinal);
        Assert.Contains("| Licence | Apache-2.0 |", record, StringComparison.Ordinal);
        Assert.Contains("| Confirmed | ", record, StringComparison.Ordinal);
        Assert.Contains("\"Apache-2.0\"", File.ReadAllText(Path.Combine(root, "eng", "licenses", "allowed-licenses.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void RR_10_the_OpenAPI_job_runs_the_self_test_and_then_the_check_with_the_label_from_the_event_and_neither_can_be_skipped()
    {
        string root = RepositoryRoot();
        string[] lines = File.ReadAllLines(OpenApiWorkflow());
        string[] run = [.. RunBlocks(lines)];
        int selfTest = Array.IndexOf(run, ".github/scripts/openapi-check-selftest.sh");
        int check = Array.IndexOf(run, ".github/scripts/openapi-check.sh \"${BASE_SHA}\" openapi/v1.json \"${API_BREAKING_LABEL}\"");
        Assert.True(selfTest >= 0 && check > selfTest, "openapi.yml does not run the self-test and then the check (RR-10).");
        Assert.True(File.Exists(Path.Combine(root, ".github", "scripts", "openapi-check.sh")), "openapi-check.sh is missing (RR-10).");
        Assert.True(File.Exists(Path.Combine(root, ".github", "scripts", "openapi-check-selftest.sh")), "openapi-check-selftest.sh is missing (RR-10).");
        Assert.True(File.Exists(Path.Combine(root, "openapi", "v1.json")), "openapi/v1.json is missing (RR-10).");

        // RR-85: the label and the base reach the shell through environment variables.
        Assert.Contains(lines, line => line.Trim() == "API_BREAKING_LABEL: ${{ contains(github.event.pull_request.labels.*.name, 'api-breaking') }}");
        Assert.Contains(lines, line => line.Trim() == "BASE_SHA: ${{ github.event.pull_request.base.sha }}");
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("continue-on-error", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("if:", StringComparison.Ordinal));
    }

    /// <summary>The entries of every ports: list of a compose file.</summary>
    private static IEnumerable<string> PublishedPorts(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (!PortsKey().IsMatch(lines[i]))
            {
                continue;
            }

            int indent = lines[i].Length - lines[i].TrimStart().Length;
            while (i + 1 < lines.Length && lines[i + 1].Length - lines[i + 1].TrimStart().Length > indent && ListEntry().Match(lines[i + 1]) is { Success: true } entry)
            {
                i++;
                yield return entry.Groups["binding"].Value;
            }
        }
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

    private static string OpenApiWorkflow() => Path.Combine(RepositoryRoot(), ".github", "workflows", "openapi.yml");

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
