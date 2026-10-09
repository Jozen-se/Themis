using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Themis.Kernel.Pipeline.OpenApi;

/// <summary>
/// Declares every decimal in the API description as a string with format <c>decimal</c>, matching what
/// <see cref="Json.DecimalJsonConverter"/> writes (D-37). Clients generated from the description therefore never
/// bind money or quantities to floating point (R-MON-01).
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

        return Task.CompletedTask;
    }
}
