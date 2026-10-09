using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Themis.Kernel.Primitives;

/// <summary>
/// A business key: 1 to 40 characters from A to Z, 0 to 9, dash and underscore, stored uppercase
/// (Phase 0 Specification, Conventions). Uniqueness within the scope stated on the attribute is R-MD-05.
/// </summary>
public readonly partial record struct Code
{
    public const int MaxLength = 40;

    private Code(string value)
    {
        Value = value;
    }

    public string Value { get; }

    [GeneratedRegex("^[A-Z0-9_-]{1,40}$")]
    private static partial Regex Pattern();

    public static Code From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalised = value.Trim().ToUpperInvariant();
        return TryFrom(normalised, out Code code)
            ? code
            : throw new ArgumentException($"'{value}' is not a valid code: 1 to 40 characters from A to Z, 0 to 9, dash and underscore.", nameof(value));
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out Code code)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            code = new Code(value);
            return true;
        }

        code = default;
        return false;
    }

    public override string ToString() => Value;
}
