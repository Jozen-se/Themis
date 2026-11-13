using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Pipeline.Http;

namespace Themis.Kernel.Pipeline.OpenApi;

/// <summary>
/// Keeps <c>expectedVersion</c> out of the request schema of a versioned command: the version travels in the
/// <c>If-Match</c> header and the endpoint overwrites anything the body supplies (R-API-02). Keeps <c>scope</c> out of
/// the request schema of a scoped command too: the scope is computed from the command's own data and never read from
/// the body.
/// </summary>
internal sealed class CommandSchemaTransformer : IOpenApiSchemaTransformer
{
    internal const string ScopeProperty = "scope";

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (typeof(IVersionedCommand).IsAssignableFrom(context.JsonTypeInfo.Type))
        {
            schema.Properties?.Remove(CommandEndpoints.ExpectedVersionProperty);
            schema.Required?.Remove(CommandEndpoints.ExpectedVersionProperty);
        }

        if (typeof(IScopedRequest).IsAssignableFrom(context.JsonTypeInfo.Type))
        {
            schema.Properties?.Remove(ScopeProperty);
            schema.Required?.Remove(ScopeProperty);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Describes the route parameters of every command and query operation, and on commands the headers:
/// <c>Idempotency-Key</c> on all (R-API-01), <c>If-Match</c> on versioned commands (R-API-02), and the <c>ETag</c>
/// and <c>Idempotency-Replayed</c> response headers of a success.
/// </summary>
internal sealed class CommandOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        CommandMetadata? metadata = context.Description.ActionDescriptor.EndpointMetadata.OfType<CommandMetadata>().FirstOrDefault();
        bool isQuery = context.Description.ActionDescriptor.EndpointMetadata.OfType<QueryEndpoints.QueryMetadata>().Any();
        if (metadata is null && !isQuery)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        foreach (string routeParameter in RouteParameters(context.Description.RelativePath))
        {
            operation.Parameters.Add(new OpenApiParameter { Name = routeParameter, In = ParameterLocation.Path, Required = true, Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
        }

        if (metadata is null)
        {
            return Task.CompletedTask;
        }

        operation.Summary ??= metadata.Name;
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

    /// <summary>The {name} tokens of a route pattern, in order, each without its constraint.</summary>
    internal static IEnumerable<string> RouteParameters(string? relativePath)
    {
        if (relativePath is null)
        {
            yield break;
        }

        int start = relativePath.IndexOf('{', StringComparison.Ordinal);
        while (start >= 0)
        {
            int end = relativePath.IndexOf('}', start);
            if (end < 0)
            {
                yield break;
            }

            string token = relativePath[(start + 1)..end];
            int constraint = token.IndexOf(':', StringComparison.Ordinal);
            yield return constraint >= 0 ? token[..constraint] : token;
            start = relativePath.IndexOf('{', end);
        }
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

/// <summary>
/// Describes each command's request body as the endpoint reads it: a body member that names a route value is not part of
/// the body, because the route wins over the body (RR-73), so it is removed from the body schema, and a body left with
/// no member is optional. It runs on the document, not per schema or operation, because only the paths know their route
/// bindings.
/// </summary>
internal sealed class CommandRequestBodyTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        IDictionary<string, IOpenApiSchema>? schemas = document.Components?.Schemas;
        if (document.Paths is null || schemas is null)
        {
            return Task.CompletedTask;
        }

        foreach ((string path, IOpenApiPathItem item) in document.Paths)
        {
            if (item.Operations is null
                || !item.Operations.TryGetValue(HttpMethod.Post, out OpenApiOperation? operation)
                || operation.RequestBody is not OpenApiRequestBody body
                || body.Content is null
                || !body.Content.TryGetValue("application/json", out OpenApiMediaType? media)
                || media.Schema is not OpenApiSchemaReference reference
                || reference.Reference.Id is not { } id
                || !schemas.TryGetValue(id, out IOpenApiSchema? target)
                || target is not OpenApiSchema schema)
            {
                continue;
            }

            HashSet<string> route = new(CommandOperationTransformer.RouteParameters(path), StringComparer.OrdinalIgnoreCase);
            if (schema.Properties is not null)
            {
                foreach (string property in schema.Properties.Keys.Where(route.Contains).ToList())
                {
                    schema.Properties.Remove(property);
                    schema.Required?.Remove(property);
                }
            }

            if (schema.Properties is null || schema.Properties.Count == 0)
            {
                body.Required = false;
            }
        }

        return Task.CompletedTask;
    }
}
