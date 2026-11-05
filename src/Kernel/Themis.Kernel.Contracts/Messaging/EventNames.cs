using System.Text.RegularExpressions;

namespace Themis.Kernel.Contracts.Messaging;

/// <summary>
/// Event naming (D-44): TYPE plus the command verb in past tense, upper snake case, schema version 1, for example
/// ITEM_CREATED or INVENTORY_ADJUSTMENT_DISCARDED. Where the document type registry gives a business name for a
/// transition (GOODS_ISSUED, INVENTORY_ADJUSTED), code reads it from the registry instead; master data commands
/// always use the generated name.
/// </summary>
public static partial class EventNames
{
    public const int DefaultSchemaVersion = 1;

    /// <summary>
    /// Every event type name this build publishes (D-44; RR-07): the generated names of the master data commands, listed
    /// here, and the names the document type registry gives its transitions. A command's catalogue entry
    /// (CommandEventsAttribute) names only events from this set; an architecture test holds the two together, so a new
    /// event is added here first.
    /// </summary>
    public static IReadOnlySet<string> Registered => RegisteredNames.Value;

    private static readonly Lazy<IReadOnlySet<string>> RegisteredNames = new(() => new HashSet<string>(
        new[]
        {
            Generated("ITEM", "CREATE"),
            Generated("ITEM_COMPANY", "CREATE"),
            Generated("SETTING", "CHANGE"),
            Generated("CHART_OF_ACCOUNTS", "CREATE"),
            Generated("CHART_OF_ACCOUNTS", "UPDATE"),
            Generated("COMPANY", "CREATE"),
            Generated("COMPANY", "UPDATE"),
            Generated("PARTY", "CREATE"),
            Generated("PARTY", "UPDATE"),
            Generated("SITE", "CREATE"),
            Generated("SITE", "UPDATE"),
            Generated("WAREHOUSE", "CREATE"),
            Generated("WAREHOUSE", "UPDATE"),
            Generated("ACCOUNT", "CREATE"),
            Generated("ACCOUNT", "UPDATE"),
            Generated("ACCOUNT", "BLOCK"),
            Generated("ACCOUNT", "UNBLOCK"),
            Generated("ACCOUNT", "DEACTIVATE"),
            Generated("ACCOUNT", "REACTIVATE"),
        }.Concat(Documents.DocumentTypes.All.SelectMany(type => type.Transitions).Select(transition => transition.EventName)),
        StringComparer.Ordinal));

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,39}\\z")]
    private static partial Regex CodePattern();

    public static string Generated(string typeCode, string verb)
    {
        if (!CodePattern().IsMatch(typeCode) || !CodePattern().IsMatch(verb))
        {
            throw new ArgumentException("Type code and verb are upper snake case codes.");
        }

        return $"{typeCode}_{PastTense(verb)}";
    }

    /// <summary>Past tense of an upper snake case verb: the last word gets D after a final E, otherwise ED.</summary>
    public static string PastTense(string verb)
    {
        int lastWord = verb.LastIndexOf('_') + 1;
        string head = verb[..lastWord];
        string word = verb[lastWord..];
        return word.EndsWith('E') ? head + word + "D" : head + word + "ED";
    }
}
