using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Themis.Kernel.Primitives;

/// <summary>
/// A unit of measure code: a UN/ECE Recommendation 20 code such as H87 or KGM, or an item pack code such as BOX
/// (Phase 0 Specification, UnitOfMeasure and ItemUnit). Same character rules as <see cref="Code"/>.
/// </summary>
public readonly partial record struct UnitCode
{
    private UnitCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    [GeneratedRegex("^[A-Z0-9_-]{1,40}$")]
    private static partial Regex Pattern();

    public static UnitCode From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalised = value.Trim().ToUpperInvariant();
        return TryFrom(normalised, out UnitCode code)
            ? code
            : throw new ArgumentException($"'{value}' is not a valid unit code.", nameof(value));
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out UnitCode code)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            code = new UnitCode(value);
            return true;
        }

        code = default;
        return false;
    }

    public override string ToString() => Value;
}
