using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Contracts.Documents;

/// <summary>Phase 0 Specification, DocumentType: when a document of the type receives its number, or that the row only names a number series.</summary>
public enum NumberAssignment
{
    OnCreate,
    OnPost,

    /// <summary>A registry row without documents, transitions, commands or events, so that number series can name it (JOURNAL_ENTRY).</summary>
    NumberingOnly,
}

/// <summary>The system states of a kernel document (Phase 0 Specification, State machines part 2; R-DOC-01).</summary>
public enum DocumentStatus
{
    Draft,
    Posted,
    Discarded,
    Reversed,
}

/// <summary>
/// One row of a document type's state machine (R-DOC-01): the command that moves a document from one status to
/// the next, and the name of the outbox event the transition writes (D-44): the generated TYPE plus the command in
/// past tense, or the business name the registry records for the transition (INVENTORY_ADJUSTED).
/// </summary>
public sealed record DocumentTransition(string Command, DocumentStatus From, DocumentStatus To, string EventName)
{
    public string Command { get; } = Command is { Length: > 0 } && PermissionCodes.IsValid($"X.{Command}") ? Command : throw new ArgumentException($"'{Command}' is not a command verb.", nameof(Command));
}

/// <summary>
/// One row of the document type registry (D-40): the Specification's DocumentType table with the type's state
/// machine. A numbering-only row has no posting flags, no party role and no transitions.
/// </summary>
public sealed record DocumentTypeDefinition(
    string Code,
    NumberAssignment NumberAssigned,
    bool Gapless,
    bool PostsInventory,
    bool PostsGeneralLedger,
    string? PartyRole,
    IReadOnlyList<DocumentTransition> Transitions)
{
    public bool IsNumberingOnly => NumberAssigned == NumberAssignment.NumberingOnly;

    public DocumentTransition? FindTransition(string command, DocumentStatus from) =>
        Transitions.FirstOrDefault(transition => string.Equals(transition.Command, command, StringComparison.Ordinal) && transition.From == from);
}

/// <summary>
/// The document type registry of milestone 1 as a catalogue in code (D-40, step 7 brief): system-owned, written
/// to kernel.document_type and kernel.document_transition by Themis.Migrator on every run; the state machine engine
/// and event naming read the tables. INVENTORY_ADJUSTMENT has its four transitions (POST registered here, its
/// command built in step 8, R-LED-05); JOURNAL_ENTRY is a numbering-only row so that journal entry series can name
/// it. The engine and every registry check skip numbering-only rows. PARTY (milestone 2 step 3, D-64) is the
/// second numbering-only row, so the installation-level party series can name it; its row is inserted by the
/// migration that seeds the series, because the migrator applies migrations before it writes the registry, and the
/// seeder aligns it afterwards like every other row.
/// </summary>
public static class DocumentTypes
{
    public const string InventoryAdjustment = "INVENTORY_ADJUSTMENT";
    public const string JournalEntry = "JOURNAL_ENTRY";
    public const string Party = "PARTY";

    public const string CreateVerb = "CREATE";
    public const string UpdateVerb = "UPDATE";
    public const string DiscardVerb = "DISCARD";
    public const string PostVerb = "POST";

    /// <summary>The business event name of INVENTORY_ADJUSTMENT.POST (D-44); the other transitions use the generated name.</summary>
    public const string InventoryAdjustedEvent = "INVENTORY_ADJUSTED";

    public static IReadOnlyList<DocumentTypeDefinition> All { get; } =
    [
        new(InventoryAdjustment, NumberAssignment.OnPost, Gapless: false, PostsInventory: true, PostsGeneralLedger: true, PartyRole: null,
        [
            new(CreateVerb, DocumentStatus.Draft, DocumentStatus.Draft, EventNames.Generated(InventoryAdjustment, CreateVerb)),
            new(UpdateVerb, DocumentStatus.Draft, DocumentStatus.Draft, EventNames.Generated(InventoryAdjustment, UpdateVerb)),
            new(DiscardVerb, DocumentStatus.Draft, DocumentStatus.Discarded, EventNames.Generated(InventoryAdjustment, DiscardVerb)),
            new(PostVerb, DocumentStatus.Draft, DocumentStatus.Posted, InventoryAdjustedEvent),
        ]),
        new(JournalEntry, NumberAssignment.NumberingOnly, Gapless: true, PostsInventory: false, PostsGeneralLedger: false, PartyRole: null, []),
        // Specification, NumberSeries: party numbers are not legal document numbers, so the PARTY series is not gapless.
        new(Party, NumberAssignment.NumberingOnly, Gapless: false, PostsInventory: false, PostsGeneralLedger: false, PartyRole: null, []),
    ];

    public static DocumentTypeDefinition? Find(string code) => All.FirstOrDefault(type => string.Equals(type.Code, code, StringComparison.Ordinal));

    public static DocumentTypeDefinition Get(string code) => Find(code) ?? throw new ArgumentException($"{code} is not a document type.", nameof(code));

    /// <summary>
    /// The registry rules every row must follow (Specification, DocumentType): codes are upper snake case and unique;
    /// a numbering-only row posts nothing, names no party role and has no transitions; every other row has at
    /// least one transition, with CREATE into Draft, each (command, from) pair once, and every event name a code.
    /// </summary>
    public static IReadOnlyList<string> Violations(IEnumerable<DocumentTypeDefinition> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        List<string> violations = [];
        HashSet<string> codes = new(StringComparer.Ordinal);
        foreach (DocumentTypeDefinition type in registry)
        {
            if (!PermissionCodes.IsValid($"{type.Code}.X"))
            {
                violations.Add($"{type.Code}: not an upper snake case code.");
            }

            if (!codes.Add(type.Code))
            {
                violations.Add($"{type.Code}: listed twice.");
            }

            if (type.IsNumberingOnly)
            {
                if (type.PostsInventory || type.PostsGeneralLedger || type.PartyRole is not null || type.Transitions.Count > 0)
                {
                    violations.Add($"{type.Code}: a numbering-only row posts nothing, names no party role and has no transitions.");
                }

                continue;
            }

            if (type.Transitions.Count == 0 || !type.Transitions.Any(t => t.Command == CreateVerb && t.To == DocumentStatus.Draft))
            {
                violations.Add($"{type.Code}: a document type has a CREATE transition into Draft.");
            }

            if (type.Transitions.Select(t => (t.Command, t.From)).Distinct().Count() != type.Transitions.Count)
            {
                violations.Add($"{type.Code}: a command and a from-status appear once.");
            }

            if (type.Transitions.Any(t => !PermissionCodes.IsValid($"{t.EventName}.X")))
            {
                violations.Add($"{type.Code}: every event name is an upper snake case code.");
            }
        }

        return violations;
    }
}
