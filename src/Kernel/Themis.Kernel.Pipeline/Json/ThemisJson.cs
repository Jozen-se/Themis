using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Themis.Kernel.Pipeline.Json;

/// <summary>
/// The one JSON configuration of the API: web defaults (camelCase, case-insensitive), enums as strings, decimals
/// as strings through <see cref="DecimalJsonConverter"/> (D-37) and the string-valued primitives (codes, business
/// dates) as strings. Integers such as versions stay JSON numbers.
/// </summary>
internal static class ThemisJson
{
    public static JsonSerializerOptions Options { get; } = Create(JsonIgnoreCondition.Never);

    /// <summary>Problem details omit absent members (ruleId when no rule failed) instead of writing null.</summary>
    public static JsonSerializerOptions ProblemOptions { get; } = Create(JsonIgnoreCondition.WhenWritingNull);

    /// <summary>
    /// Applies the API's number and enum rules to another options instance, such as the host's HTTP JSON options,
    /// so the OpenAPI description and minimal API responses follow the same contract (D-37): decimals as strings,
    /// integers strictly as numbers, enums as strings.
    /// </summary>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new DecimalJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        foreach (JsonConverter converter in PrimitiveJsonConverters.All)
        {
            options.Converters.Add(converter);
        }
    }

    private static JsonSerializerOptions Create(JsonIgnoreCondition ignoreCondition)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = ignoreCondition };
        Apply(options);
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
                // RR-61: a number outside the decimal range (for example 1e400) is a JsonException, so the request is a 400.
                return reader.TryGetDecimal(out decimal number)
                    ? number
                    : throw new JsonException($"The number {System.Text.Encoding.UTF8.GetString(reader.ValueSpan)} is outside the decimal range. Decimals are sent as strings such as \"12.50\" or as plain JSON numbers (D-37).");
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
