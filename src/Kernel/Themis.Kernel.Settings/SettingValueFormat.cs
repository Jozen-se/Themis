using System.Globalization;
using System.Text.RegularExpressions;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Settings;

/// <summary>
/// The text form of a setting value per value type (Phase 0 Specification, SettingDefinition.value_type) and its
/// validation against the definition's min, max and allowed values: a violation is a 400 (never R-SET-01). Integers and
/// decimals are invariant-culture text, decimals without an exponent (R-MON-01); booleans are true or false; codes
/// are case-sensitive tokens without spaces; durations are .NET constant format, for example 00:05:00.
/// </summary>
internal static partial class SettingValueFormat
{
    public const string ValueInvalidCode = "setting_value_invalid";
    public const int CodeMaxLength = 200;

    [GeneratedRegex("^[A-Za-z0-9_.,|-]{1,200}$")]
    private static partial Regex CodeToken();

    /// <summary>The value in its canonical text, or the 400 that refuses it.</summary>
    public static Result<string> Normalise(SettingDefinition definition, string? value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValueInvalidCode, $"Setting {definition.Key} needs a value.", details: Details(definition));
        }

        string text = value.Trim();
        switch (definition.ValueType)
        {
            case SettingValueType.Integer:
                if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
                {
                    return Error.Validation(ValueInvalidCode, $"Setting {definition.Key} takes an integer; '{text}' is not one.", details: Details(definition));
                }

                return Bound(definition, integer, l => long.Parse(l, CultureInfo.InvariantCulture), integer.ToString(CultureInfo.InvariantCulture));
            case SettingValueType.Decimal:
                if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
                {
                    return Error.Validation(ValueInvalidCode, $"Setting {definition.Key} takes a decimal without an exponent; '{text}' is not one.", details: Details(definition));
                }

                return Bound(definition, number, d => decimal.Parse(d, CultureInfo.InvariantCulture), number.ToString(CultureInfo.InvariantCulture));
            case SettingValueType.Boolean:
                return text is "true" or "false"
                    ? text
                    : Error.Validation(ValueInvalidCode, $"Setting {definition.Key} takes true or false; '{text}' is neither.", details: Details(definition));
            case SettingValueType.Code:
                // A code value is a case-sensitive token, compared exactly with the allowed values where the definition lists them.
                if (!CodeToken().IsMatch(text))
                {
                    return Error.Validation(ValueInvalidCode, $"Setting {definition.Key} takes a code (letters, digits, '_', '-', '.', ',' and '|', no spaces, at most {CodeMaxLength} characters); '{text}' is not one.", details: Details(definition));
                }

                if (definition.AllowedValues is { Count: > 0 } allowed && !allowed.Contains(text, StringComparer.Ordinal))
                {
                    return Error.Validation(ValueInvalidCode, $"Setting {definition.Key} takes one of {string.Join(", ", allowed)}; '{text}' is not among them.", details: Details(definition));
                }

                return text;
            case SettingValueType.Duration:
                if (!TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out TimeSpan duration) || duration < TimeSpan.Zero)
                {
                    return Error.Validation(ValueInvalidCode, $"Setting {definition.Key} takes a duration such as 00:05:00; '{text}' is not one.", details: Details(definition));
                }

                return Bound(definition, duration, t => TimeSpan.ParseExact(t, "c", CultureInfo.InvariantCulture), duration.ToString("c", CultureInfo.InvariantCulture));
            default:
                throw new InvalidOperationException($"Unknown value type {definition.ValueType}.");
        }
    }

    /// <summary>True when the stored text is a valid value of the definition today; the migrator reports values a tightened range left behind.</summary>
    public static bool IsValid(SettingDefinition definition, string? value) => Normalise(definition, value).IsSuccess;

    private static Result<string> Bound<T>(SettingDefinition definition, T value, Func<string, T> parse, string canonical)
        where T : struct, IComparable<T>
    {
        if (definition.Min is { } min && value.CompareTo(parse(min)) < 0)
        {
            return Error.Validation(ValueInvalidCode, $"Setting {definition.Key} is at least {min}; {canonical} is below.", details: Details(definition));
        }

        if (definition.Max is { } max && value.CompareTo(parse(max)) > 0)
        {
            return Error.Validation(ValueInvalidCode, $"Setting {definition.Key} is at most {max}; {canonical} is above.", details: Details(definition));
        }

        return canonical;
    }

    private static Dictionary<string, string> Details(SettingDefinition definition)
    {
        Dictionary<string, string> details = new(StringComparer.Ordinal) { ["field"] = "value", ["key"] = definition.Key, ["valueType"] = definition.ValueType.ToString() };
        if (definition.Min is { } min)
        {
            details["min"] = min;
        }

        if (definition.Max is { } max)
        {
            details["max"] = max;
        }

        if (definition.AllowedValues is { Count: > 0 } allowed)
        {
            details["allowedValues"] = string.Join(",", allowed);
        }

        return details;
    }
}
