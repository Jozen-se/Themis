using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Messaging;

namespace Themis.Architecture.Tests;

/// <summary>
/// RR-07 (milestone 2 step 4 brief, item 7): the command catalogue lives in code, as attributes on each command beside
/// [Command], and this test holds it complete. Every command of the production assemblies declares its rule IDs, the
/// entity types it audits, its event type names (each in EventNames.Registered), its error codes and, for a document
/// command, its transition as the document type registry holds it; every declared rule ID is carried by the name of at
/// least one test method in tests/ (a naming bar, not proof that the test exercises the command). The permission comes
/// from [Command] and If-Match from IVersionedCommand. The same catalogue is rendered to docs/commands.md, committed and
/// compared here, so CI fails on a stale file; THEMIS_COMMANDS_RECORD=1 rewrites it. Commands outside src are excluded by
/// an explicit list, and the test proves the list is exact.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class CommandCatalogueTests : IClassFixture<ArchitectureFixture>
{
    /// <summary>
    /// The assemblies whose commands are not part of the catalogue, by name: the fixture module, a test-only module that
    /// lives under tests/ (D-26). Test probes compiled into the test assemblies are never loaded as production code.
    /// </summary>
    public static readonly IReadOnlyList<string> ExcludedAssemblies = ["Themis.FixtureModule"];

    private const string RecordVariable = "THEMIS_COMMANDS_RECORD";

    private readonly ArchitectureFixture _architecture;

    public CommandCatalogueTests(ArchitectureFixture architecture)
    {
        _architecture = architecture;
    }

    [GeneratedRegex(@"^(R-[A-Z]+-\d{2}|D-\d{2,3}|RR-\d{1,3}|I-\d{2}|P-\d{2})\z")]
    private static partial Regex RuleIdPattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*[a-z0-9]\z")]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"^[A-Z][A-Za-z]*\z")]
    private static partial Regex EntityTypePattern();

    [GeneratedRegex(@"\[(?:Fact|Theory)[^\]]*\]\s*(?:\[[^\]]*\]\s*)*public\s+(?:async\s+)?(?:Task|void|ValueTask)\s+(?<name>\w+)\s*\(")]
    private static partial Regex TestMethod();

    [Fact]
    public void RR_07_every_command_of_the_production_assemblies_has_a_complete_catalogue_entry_with_known_events_its_transition_and_a_citing_test()
    {
        IReadOnlyList<Type> commands = ProductionCommands();
        Assert.True(commands.Count >= 19, $"Only {commands.Count} commands found; the catalogue looks empty.");

        IReadOnlyList<string> problems = Check(commands, TestMethodNames());

        Assert.True(problems.Count == 0, $"The command catalogue is incomplete (RR-07):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Fact]
    public void RR_07_the_committed_docs_commands_md_equals_the_catalogue_rendered_from_code()
    {
        string path = Path.Combine(RepositoryRoot(), "docs", "commands.md");
        string rendered = Render(ProductionCommands());
        if (Environment.GetEnvironmentVariable(RecordVariable) == "1")
        {
            File.WriteAllText(path, rendered);
        }

        Assert.True(File.Exists(path), $"docs/commands.md is missing; write it with {RecordVariable}=1 (RR-07).");
        string committed = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(string.Equals(committed, rendered, StringComparison.Ordinal),
            $"docs/commands.md is stale: regenerate it with {RecordVariable}=1 dotnet test --project tests/Themis.Architecture.Tests --filter-class Themis.Architecture.Tests.CommandCatalogueTests and commit it (RR-07).");
    }

    [Fact]
    public void RR_07_the_exclusion_list_is_explicit_every_excluded_assembly_holds_commands_and_no_other_assembly_with_commands_is_skipped()
    {
        Dictionary<string, int> commandsByAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => _architecture.ProductionAssemblyNames.Contains(assembly.GetName().Name!))
            .ToDictionary(assembly => assembly.GetName().Name!, assembly => assembly.GetTypes().Count(type => type.GetCustomAttribute<CommandAttribute>(inherit: false) is not null), StringComparer.Ordinal);

        // Each excluded assembly is loaded and holds commands, so the list names nothing stale.
        Assert.All(ExcludedAssemblies, name => Assert.True(commandsByAssembly.TryGetValue(name, out int count) && count > 0, $"{name} is excluded but holds no command (RR-07)."));
        // Every other assembly with commands is a src assembly whose commands the catalogue covers.
        HashSet<string> covered = [.. ProductionCommands().Select(type => type.Assembly.GetName().Name!)];
        Assert.All(commandsByAssembly.Where(pair => pair.Value > 0 && !ExcludedAssemblies.Contains(pair.Key)), pair => Assert.Contains(pair.Key, covered));
        Assert.All(covered, name => Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll")) && !ExcludedAssemblies.Contains(name)));
        Assert.DoesNotContain(ProductionCommands(), type => type.Namespace?.StartsWith("Themis.FixtureModule", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RR_07_the_check_fails_on_a_command_without_an_entry_an_unknown_event_a_missing_audit_or_error_declaration_and_a_command_without_a_citing_test()
    {
        HashSet<string> tests = new(StringComparer.Ordinal) { "R_MD_05_a_synthetic_test" };

        Assert.Empty(Check([typeof(CompleteCommand)], tests));
        Assert.Contains(Check([typeof(NoEntryCommand)], tests), problem => problem.Contains("declares no [CommandRules]", StringComparison.Ordinal));
        Assert.Contains(Check([typeof(UnknownEventCommand)], tests), problem => problem.Contains("SYNTHETIC_FROBNICATED is not in EventNames.Registered", StringComparison.Ordinal));
        Assert.Contains(Check([typeof(NoAuditCommand)], tests), problem => problem.Contains("declares no [CommandAudits]", StringComparison.Ordinal));
        Assert.Contains(Check([typeof(NoErrorsCommand)], tests), problem => problem.Contains("declares no [CommandErrors]", StringComparison.Ordinal));
        Assert.Contains(Check([typeof(CompleteCommand)], new HashSet<string>(StringComparer.Ordinal) { "RR_07_a_test_citing_something_else" }), problem => problem.Contains("no test method name cites R-MD-05", StringComparison.Ordinal));
        Assert.Contains(Check([typeof(WrongTransitionCommand)], tests), problem => problem.Contains("transition", StringComparison.Ordinal));
        Assert.Contains(Check([typeof(MissingTransitionCommand)], tests), problem => problem.Contains("declares no [CommandTransition]", StringComparison.Ordinal));
    }

    /// <summary>Every command type of the production assemblies but the excluded ones, ordered by permission code.</summary>
    private IReadOnlyList<Type> ProductionCommands() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => _architecture.ProductionAssemblyNames.Contains(assembly.GetName().Name!) && !ExcludedAssemblies.Contains(assembly.GetName().Name!))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<CommandAttribute>(inherit: false) is not null)
            .OrderBy(type => type.GetCustomAttribute<CommandAttribute>(inherit: false)!.PermissionCode, StringComparer.Ordinal)];

    /// <summary>The problems of a set of command types against the catalogue rules (RR-07); empty when every entry is complete.</summary>
    internal static IReadOnlyList<string> Check(IEnumerable<Type> commands, IReadOnlySet<string> testMethodNames)
    {
        List<string> problems = [];
        foreach (Type type in commands)
        {
            CommandAttribute command = type.GetCustomAttribute<CommandAttribute>(inherit: false) ?? throw new ArgumentException($"{type.FullName} carries no [Command].", nameof(commands));
            string name = $"{command.PermissionCode} ({type.Name})";

            CommandRulesAttribute? rules = type.GetCustomAttribute<CommandRulesAttribute>(inherit: false);
            if (rules is null || rules.RuleIds.Count == 0)
            {
                problems.Add($"{name} declares no [CommandRules].");
            }
            else
            {
                foreach (string ruleId in rules.RuleIds)
                {
                    if (!RuleIdPattern().IsMatch(ruleId))
                    {
                        problems.Add($"{name}: '{ruleId}' is not a rule or decision ID.");
                    }
                    else if (!Cites(testMethodNames, ruleId))
                    {
                        problems.Add($"{name}: no test method name cites {ruleId}.");
                    }
                }
            }

            CommandAuditsAttribute? audits = type.GetCustomAttribute<CommandAuditsAttribute>(inherit: false);
            if (audits is null || audits.EntityTypes.Count == 0)
            {
                problems.Add($"{name} declares no [CommandAudits].");
            }
            else
            {
                problems.AddRange(audits.EntityTypes.Where(entity => !EntityTypePattern().IsMatch(entity)).Select(entity => $"{name}: '{entity}' is not an entity type."));
            }

            CommandEventsAttribute? events = type.GetCustomAttribute<CommandEventsAttribute>(inherit: false);
            if (events is null || events.EventNames.Count == 0)
            {
                problems.Add($"{name} declares no [CommandEvents].");
            }
            else
            {
                problems.AddRange(events.EventNames.Where(eventName => !EventNames.Registered.Contains(eventName)).Select(eventName => $"{name}: {eventName} is not in EventNames.Registered."));
            }

            CommandErrorsAttribute? errors = type.GetCustomAttribute<CommandErrorsAttribute>(inherit: false);
            if (errors is null || errors.Codes.Count == 0)
            {
                problems.Add($"{name} declares no [CommandErrors].");
            }
            else
            {
                problems.AddRange(errors.Codes.Where(code => !ErrorCodePattern().IsMatch(code)).Select(code => $"{name}: '{code}' is not an error code."));
                problems.AddRange(errors.Codes.GroupBy(code => code, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => $"{name}: error code {group.Key} is listed twice."));
            }

            // R-DOC-01: a command on a document type with transitions declares its own, as the registry holds it.
            CommandTransitionAttribute? transition = type.GetCustomAttribute<CommandTransitionAttribute>(inherit: false);
            DocumentTypeDefinition? documentType = DocumentTypes.Find(command.TypeCode);
            if (documentType is { Transitions.Count: > 0 })
            {
                DocumentTransition? registered = documentType.Transitions.FirstOrDefault(t => string.Equals(t.Command, command.Verb, StringComparison.Ordinal));
                if (transition is null)
                {
                    problems.Add($"{name} is a document command and declares no [CommandTransition].");
                }
                else if (registered is null || !string.Equals(transition.DocumentType, documentType.Code, StringComparison.Ordinal)
                    || !string.Equals(transition.From, registered.From.ToString(), StringComparison.Ordinal) || !string.Equals(transition.To, registered.To.ToString(), StringComparison.Ordinal))
                {
                    problems.Add($"{name}: the declared transition {transition.DocumentType} {transition.From} to {transition.To} is not the registry's.");
                }
                else if (events is not null && !events.EventNames.Contains(registered.EventName))
                {
                    problems.Add($"{name}: the transition's event {registered.EventName} is not declared.");
                }
            }
            else if (transition is not null)
            {
                problems.Add($"{name} declares a transition but {command.TypeCode} is not a document type with transitions.");
            }
        }

        return problems;
    }

    /// <summary>Whether a test method name carries the ID as a word: R-MD-05 as R_MD_05, bounded by an underscore or an end.</summary>
    private static bool Cites(IReadOnlySet<string> testMethodNames, string ruleId)
    {
        string token = ruleId.Replace('-', '_');
        return testMethodNames.Any(test => Regex.IsMatch(test, $"(?:^|_){Regex.Escape(token)}(?:_|$)"));
    }

    /// <summary>The names of every [Fact] or [Theory] method in the C# sources under tests/.</summary>
    private static HashSet<string> TestMethodNames()
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match match in TestMethod().Matches(File.ReadAllText(file)))
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return names;
    }

    /// <summary>docs/commands.md: one section per command, ordered by permission code, generated from the catalogue in code.</summary>
    internal static string Render(IEnumerable<Type> commands)
    {
        StringBuilder text = new();
        text.Append("# Command catalogue\n\n")
            .Append("Generated from the catalogue attributes on each command (RR-07; milestone 2 step 4). Do not edit by hand: change the\n")
            .Append("attributes beside `[Command]` and regenerate with\n")
            .Append("`THEMIS_COMMANDS_RECORD=1 dotnet test --project tests/Themis.Architecture.Tests --filter-class Themis.Architecture.Tests.CommandCatalogueTests`.\n")
            .Append("The error codes are the command's own; every command also answers 401, 403, 409 for a concurrent duplicate,\n")
            .Append("422 `idempotency_key_reused` and, with If-Match, 428 from the pipeline (Build Brief, API conventions).\n\n");
        foreach (Type type in commands)
        {
            CommandAttribute command = type.GetCustomAttribute<CommandAttribute>(inherit: false)!;
            text.Append("## ").Append(command.PermissionCode).Append("\n\n")
                .Append("| Field | Value |\n|---|---|\n")
                .Append("| Command type | `").Append(type.Name).Append("` |\n")
                .Append("| Permission | `").Append(command.PermissionCode).Append("` |\n")
                .Append("| If-Match | ").Append(typeof(IVersionedCommand).IsAssignableFrom(type) ? "required" : "no").Append(" |\n")
                .Append("| Rules | ").Append(Join(type.GetCustomAttribute<CommandRulesAttribute>(inherit: false)?.RuleIds)).Append(" |\n")
                .Append("| Audits | ").Append(Join(type.GetCustomAttribute<CommandAuditsAttribute>(inherit: false)?.EntityTypes)).Append(" |\n")
                .Append("| Events | ").Append(Join(type.GetCustomAttribute<CommandEventsAttribute>(inherit: false)?.EventNames, code: true)).Append(" |\n")
                .Append("| Errors | ").Append(Join(type.GetCustomAttribute<CommandErrorsAttribute>(inherit: false)?.Codes, code: true)).Append(" |\n");
            if (type.GetCustomAttribute<CommandTransitionAttribute>(inherit: false) is { } transition)
            {
                text.Append("| Transition | ").Append(transition.DocumentType).Append(": ").Append(transition.From).Append(" to ").Append(transition.To).Append(" |\n");
            }

            text.Append('\n');
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    private static string Join(IReadOnlyList<string>? values, bool code = false) =>
        values is null || values.Count == 0 ? "(none)" : string.Join(", ", values.Select(value => code ? $"`{value}`" : value));

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Themis.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Themis.sln not found above the test output directory.");
    }

    // Synthetic commands for the RR-07 self-test; never registered anywhere.
    [Command("SYNTHETIC.CREATE")]
    [CommandRules("R-MD-05")]
    [CommandAudits("Synthetic")]
    [CommandEvents("ACCOUNT_CREATED")]
    [CommandErrors("synthetic_code")]
    private sealed record CompleteCommand;

    [Command("SYNTHETIC.CREATE")]
    private sealed record NoEntryCommand;

    [Command("SYNTHETIC.CREATE")]
    [CommandRules("R-MD-05")]
    [CommandAudits("Synthetic")]
    [CommandEvents("SYNTHETIC_FROBNICATED")]
    [CommandErrors("synthetic_code")]
    private sealed record UnknownEventCommand;

    [Command("SYNTHETIC.CREATE")]
    [CommandRules("R-MD-05")]
    [CommandEvents("ACCOUNT_CREATED")]
    [CommandErrors("synthetic_code")]
    private sealed record NoAuditCommand;

    [Command("SYNTHETIC.CREATE")]
    [CommandRules("R-MD-05")]
    [CommandAudits("Synthetic")]
    [CommandEvents("ACCOUNT_CREATED")]
    private sealed record NoErrorsCommand;

    [Command("INVENTORY_ADJUSTMENT.DISCARD")]
    [CommandRules("R-MD-05")]
    [CommandAudits("Document")]
    [CommandEvents("INVENTORY_ADJUSTMENT_DISCARDED")]
    [CommandErrors("synthetic_code")]
    [CommandTransition("INVENTORY_ADJUSTMENT", "Draft", "Posted")]
    private sealed record WrongTransitionCommand;

    [Command("INVENTORY_ADJUSTMENT.DISCARD")]
    [CommandRules("R-MD-05")]
    [CommandAudits("Document")]
    [CommandEvents("INVENTORY_ADJUSTMENT_DISCARDED")]
    [CommandErrors("synthetic_code")]
    private sealed record MissingTransitionCommand;
}
