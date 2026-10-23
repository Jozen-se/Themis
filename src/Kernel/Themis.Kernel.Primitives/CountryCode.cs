using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Themis.Kernel.Primitives;

/// <summary>An ISO 3166-1 alpha-2 country code: two uppercase letters (Phase 0 Specification, Data types).</summary>
public readonly partial record struct CountryCode
{
    private CountryCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    [GeneratedRegex("^[A-Z]{2}\\z")]
    private static partial Regex Pattern();

    public static CountryCode From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalised = value.Trim().ToUpperInvariant();
        return TryFrom(normalised, out CountryCode code)
            ? code
            : throw new ArgumentException($"'{value}' is not an ISO 3166-1 alpha-2 country code.", nameof(value));
    }

    public static bool TryFrom(string? value, [NotNullWhen(true)] out CountryCode code)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            code = new CountryCode(value);
            return true;
        }

        code = default;
        return false;
    }

    public override string ToString() => Value;
}
