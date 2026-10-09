using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Themis.Kernel.Pipeline.OpenApi;

/// <summary>
/// Declares every decimal in the API description as a string with format <c>decimal</c>, matching what
/// <see cref="Json.DecimalJsonConverter"/> writes (D-37), and every string-valued primitive (codes, business dates,
/// ids) as a string. Clients generated from the description therefore never bind money or quantities to floating
/// point (R-MON-01) and see codes as the strings they are.
/// </summary>
internal sealed class DecimalSchemaTransformer : IOpenApiSchemaTransformer
{
    public const string Format = "decimal";
    public const string Pattern = @"^-?\d+(\.\d+)?$";

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        Type type = context.JsonTypeInfo.Type;
        if (type == typeof(decimal))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = Format;
            schema.Pattern = Pattern;
        }
        else if (type == typeof(decimal?))
        {
            schema.Type = JsonSchemaType.String | JsonSchemaType.Null;
            schema.Format = Format;
            schema.Pattern = Pattern;
        }
        else if (Json.PrimitiveJsonConverters.Types.Contains(type))
        {
            // Codes, business dates and ids are JSON strings (PrimitiveJsonConverters), not objects.
            schema.Type = JsonSchemaType.String;
            schema.Properties = null;
            schema.Required = null;
            if (type == typeof(Primitives.BusinessDate))
            {
                schema.Format = "date";
            }
        }
        else if (Nullable.GetUnderlyingType(type) is { } underlying && Json.PrimitiveJsonConverters.Types.Contains(underlying))
        {
            schema.Type = JsonSchemaType.String | JsonSchemaType.Null;
            schema.Properties = null;
            schema.Required = null;
        }

        return Task.CompletedTask;
    }
}
