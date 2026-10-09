using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Themis.Kernel.Pipeline.Json;

/// <summary>
/// The one JSON configuration of the API: web defaults (camelCase, case-insensitive), enums as strings, and decimals
/// as strings through <see cref="DecimalJsonConverter"/> (D-37). Integers such as versions stay JSON numbers.
/// </summary>
internal static class ThemisJson
{
    public static JsonSerializerOptions Options { get; } = Create(JsonIgnoreCondition.Never);

    /// <summary>Problem details omit absent members (ruleId when no rule failed) instead of writing null.</summary>
    public static JsonSerializerOptions ProblemOptions { get; } = Create(JsonIgnoreCondition.WhenWritingNull);

    private static JsonSerializerOptions Create(JsonIgnoreCondition ignoreCondition)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = ignoreCondition,
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.Converters.Add(new DecimalJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>
/// Writes every decimal as a JSON string with its scale intact ("2500000.00") and reads strings and numbers (D-37,
/// R-MON-01). Only decimals are affected: the global number-handling option would turn integers into strings too.
/// Exponent notation is refused on input because it hides the scale a money or quantity value carries.
/// </summary>
internal sealed class DecimalJsonConverter : JsonConverter<decimal>
{
    private const NumberStyles AcceptedStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                string text = reader.GetString() ?? string.Empty;
                return decimal.TryParse(text, AcceptedStyles, CultureInfo.InvariantCulture, out decimal value)
                    ? value
                    : throw new JsonException($"'{text}' is not a decimal number. Decimals are sent as strings such as \"12.50\" or as plain JSON numbers (D-37).");
            default:
                throw new JsonException($"Expected a decimal as a string or a number, found {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
