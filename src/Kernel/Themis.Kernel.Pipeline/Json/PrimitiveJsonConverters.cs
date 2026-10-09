using System.Text.Json;
using System.Text.Json.Serialization;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Json;

/// <summary>
/// The string-valued primitives travel as plain JSON strings: codes, currency, country and unit codes, business
/// dates (yyyy-MM-dd), correlation ids and idempotency keys. Each one parses through the primitive's own factory, so
/// an invalid value is rejected at binding with the factory's message.
/// </summary>
internal sealed class StringPrimitiveConverter<T> : JsonConverter<T>
{
    private readonly Func<string, T> _parse;
    private readonly Func<T, string> _format;

    public StringPrimitiveConverter(Func<string, T> parse, Func<T, string> format)
    {
        _parse = parse;
        _format = format;
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string for {typeToConvert.Name}, found {reader.TokenType}.");
        }

        try
        {
            return _parse(reader.GetString() ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(_format(value));
}

internal static class PrimitiveJsonConverters
{
    /// <summary>The primitive types that are JSON strings, with their converters; the OpenAPI description declares them as strings too.</summary>
    public static IReadOnlyList<JsonConverter> All { get; } =
    [
        new StringPrimitiveConverter<Code>(Code.From, code => code.Value),
        new StringPrimitiveConverter<CurrencyCode>(CurrencyCode.From, code => code.Value),
        new StringPrimitiveConverter<CountryCode>(CountryCode.From, code => code.Value),
        new StringPrimitiveConverter<UnitCode>(UnitCode.From, code => code.Value),
        new StringPrimitiveConverter<BusinessDate>(BusinessDate.Parse, date => date.ToString()),
        new StringPrimitiveConverter<CorrelationId>(CorrelationId.From, id => id.Value),
        new StringPrimitiveConverter<IdempotencyKey>(IdempotencyKey.From, key => key.Value),
    ];

    public static IReadOnlyList<Type> Types { get; } = [typeof(Code), typeof(CurrencyCode), typeof(CountryCode), typeof(UnitCode), typeof(BusinessDate), typeof(CorrelationId), typeof(IdempotencyKey)];
}
