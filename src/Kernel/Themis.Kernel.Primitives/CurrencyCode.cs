using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Themis.Kernel.Primitives;

/// <summary>An ISO 4217 currency code: three uppercase letters. Every amount carries one (R-MON-01).</summary>
public readonly partial record struct CurrencyCode
{
    private CurrencyCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex Pattern();

    public static CurrencyCode From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalised = value.Trim().ToUpperInvariant();
        return TryFrom(normalised, out CurrencyCode code)
            ? code
            : throw new ArgumentException($"'{value}' is not an ISO 4217 currency code.", nameof(value));
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out CurrencyCode code)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            code = new CurrencyCode(value);
            return true;
        }

        code = default;
        return false;
    }

    public override string ToString() => Value;
}
