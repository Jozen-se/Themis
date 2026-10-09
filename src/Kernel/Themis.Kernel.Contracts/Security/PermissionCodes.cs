using System.Text.RegularExpressions;

namespace Themis.Kernel.Contracts.Security;

/// <summary>
/// The shape of a permission code: TYPE.VERB, one per command (TYPE.COMMAND) plus one TYPE.READ per resource type
/// for queries (Phase 0 Specification, Security entities; D-47). The code equals the command name, so the permission
/// list and the API surface cannot drift apart.
/// </summary>
public static partial class PermissionCodes
{
    public const int MaxLength = 81;
    public const string ReadVerb = "READ";

    [GeneratedRegex("^(?<type>[A-Z][A-Z0-9_]{0,39})\\.(?<verb>[A-Z][A-Z0-9_]{0,39})$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? code) => code is not null && Pattern().IsMatch(code);

    public static string Validated(string code) =>
        IsValid(code) ? code : throw new ArgumentException($"'{code}' is not a TYPE.VERB permission code.", nameof(code));

    /// <summary>The TYPE.READ permission of a resource type, for example COMPANY.READ.</summary>
    public static string Read(string typeCode) => Validated($"{typeCode}.{ReadVerb}");

    public static (string TypeCode, string Verb) Split(string code)
    {
        Match match = Pattern().Match(code);
        return match.Success
            ? (match.Groups["type"].Value, match.Groups["verb"].Value)
            : throw new ArgumentException($"'{code}' is not a TYPE.VERB permission code.", nameof(code));
    }
}
