using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Themis.Kernel.Contracts.Commands;

/// <summary>The Idempotency-Key header value, required on every command (R-API-01): 1 to 128 safe ASCII characters.</summary>
public readonly partial record struct IdempotencyKey
{
    public const int MaxLength = 128;

    private IdempotencyKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,128}$")]
    private static partial Regex Pattern();

    public static bool TryFrom(string? value, [NotNullWhen(true)] out IdempotencyKey key)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            key = new IdempotencyKey(value);
            return true;
        }

        key = default;
        return false;
    }

    public static IdempotencyKey From(string value) =>
        TryFrom(value, out IdempotencyKey key) ? key : throw new ArgumentException("Not a valid idempotency key.", nameof(value));

    public override string ToString() => Value;
}
