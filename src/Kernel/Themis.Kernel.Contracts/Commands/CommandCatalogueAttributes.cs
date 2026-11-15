namespace Themis.Kernel.Contracts.Commands;

/// <summary>
/// The command catalogue in code (RR-07; milestone 2 step 4 brief, item 7): beside its [Command] attribute, which names
/// the permission, every command declares the rule and decision IDs it implements, the entity types it audits, the
/// event type names it publishes, the error codes it answers with and, for a document command, its transition. If-Match
/// comes from <see cref="IVersionedCommand"/>. An architecture test holds every command to a complete declaration, every
/// event name to <see cref="Messaging.EventNames.Registered"/>, every transition to the document type registry and every
/// rule ID to a test method whose name cites it (a naming bar, not proof that the test exercises the command); the same
/// test generates docs/commands.md, which is committed and compared in CI. The error codes are those of the command
/// itself (its validator, its handler, its scope resolver and the constraints mapped for it), not the answers every
/// command shares from the pipeline (401, 403, 409 for a concurrent duplicate, 422 idempotency_key_reused, 428).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandRulesAttribute : Attribute
{
    public CommandRulesAttribute(params string[] ruleIds)
    {
        RuleIds = ruleIds ?? [];
    }

    /// <summary>The rule and decision IDs the command implements, for example R-MD-05 or D-69.</summary>
    public IReadOnlyList<string> RuleIds { get; }
}

/// <summary>The entity types the command writes audit events for (R-AUD-01), as the audit event's entity_type: Company, Account.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandAuditsAttribute : Attribute
{
    public CommandAuditsAttribute(params string[] entityTypes)
    {
        EntityTypes = entityTypes ?? [];
    }

    public IReadOnlyList<string> EntityTypes { get; }
}

/// <summary>The event type names the command publishes (D-44), each one of <see cref="Messaging.EventNames.Registered"/>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandEventsAttribute : Attribute
{
    public CommandEventsAttribute(params string[] eventNames)
    {
        EventNames = eventNames ?? [];
    }

    public IReadOnlyList<string> EventNames { get; }
}

/// <summary>The error codes the command itself answers with (R-API-03), lower snake case: account_number_duplicate.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandErrorsAttribute : Attribute
{
    public CommandErrorsAttribute(params string[] codes)
    {
        Codes = codes ?? [];
    }

    public IReadOnlyList<string> Codes { get; }
}

/// <summary>
/// The transition of a document command (R-DOC-01): the document type and the states from and to, as the document type
/// registry holds them; the architecture test compares the two.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandTransitionAttribute : Attribute
{
    public CommandTransitionAttribute(string documentType, string from, string to)
    {
        DocumentType = documentType;
        From = from;
        To = to;
    }

    public string DocumentType { get; }

    public string From { get; }

    public string To { get; }
}
