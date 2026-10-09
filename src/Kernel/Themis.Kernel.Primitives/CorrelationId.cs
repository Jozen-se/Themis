using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Themis.Kernel.Primitives;

/// <summary>
/// The correlation id that follows a request through logs, traces and audit events (D-25, R-AUD-01): up to 128
/// ASCII letters, digits, dash, underscore, dot or colon, so that an integration can pass its own reference.
/// </summary>
public readonly partial record struct CorrelationId
{
    public const int MaxLength = 128;

    private CorrelationId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,128}$")]
    private static partial Regex Pattern();

    public static CorrelationId From(string value) =>
        TryFrom(value, out CorrelationId id) ? id : throw new ArgumentException("Not a valid correlation id.", nameof(value));

    public static bool TryFrom(string? value, [NotNullWhen(true)] out CorrelationId id)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            id = new CorrelationId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value;
}
