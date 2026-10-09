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

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,39}$")]
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
