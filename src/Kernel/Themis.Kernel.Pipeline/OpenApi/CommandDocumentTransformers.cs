using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Pipeline.Http;

namespace Themis.Kernel.Pipeline.OpenApi;

/// <summary>
/// Keeps <c>expectedVersion</c> out of the request schema of a versioned command: the version travels in the
/// <c>If-Match</c> header and the endpoint overwrites anything the body supplies (R-API-02).
/// </summary>
internal sealed class CommandSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (typeof(IVersionedCommand).IsAssignableFrom(context.JsonTypeInfo.Type))
        {
            schema.Properties?.Remove(CommandEndpoints.ExpectedVersionProperty);
            schema.Required?.Remove(CommandEndpoints.ExpectedVersionProperty);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Describes the command headers on every command operation: <c>Idempotency-Key</c> on all (R-API-01),
/// <c>If-Match</c> on versioned commands (R-API-02), and the <c>ETag</c> and <c>Idempotency-Replayed</c> response
/// headers of a success.
/// </summary>
internal sealed class CommandOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        CommandMetadata? metadata = context.Description.ActionDescriptor.EndpointMetadata.OfType<CommandMetadata>().FirstOrDefault();
        if (metadata is null)
        {
            return Task.CompletedTask;
        }

        operation.Summary ??= metadata.Name;
        operation.Parameters ??= [];
        operation.Parameters.Add(Header(CommandEndpoints.IdempotencyKeyHeader, required: true, "Unique per request; the same key replays the first result (R-API-01)."));
        if (metadata.IsVersioned)
        {
            operation.Parameters.Add(Header(CommandEndpoints.IfMatchHeader, required: true, "The record version from the last ETag; a stale version is rejected with 412 (R-API-02)."));
        }

        if (operation.Responses is not null && operation.Responses.TryGetValue(StatusCodes.Status200OK.ToString(System.Globalization.CultureInfo.InvariantCulture), out IOpenApiResponse? success) && success is OpenApiResponse response)
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
            response.Headers["ETag"] = new OpenApiHeader { Description = "The record's new version (R-API-02).", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
            response.Headers[CommandEndpoints.ReplayedHeader] = new OpenApiHeader { Description = "Present and true when the response is the stored result of an earlier request with the same key (R-API-01).", Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean } };
        }

        return Task.CompletedTask;
    }

    private static OpenApiParameter Header(string name, bool required, string description) => new()
    {
        Name = name,
        In = ParameterLocation.Header,
        Required = required,
        Description = description,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
    };
}
