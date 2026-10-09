using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>
/// Maps a command to <c>POST {pattern}</c> (brief, API conventions). The endpoint reads the <c>Idempotency-Key</c>
/// header (R-API-01), the <c>If-Match</c> header for versioned commands (R-API-02) and the request body, merges the
/// route values and the version into the body (route and header win over the body), binds the command and hands it
/// to the dispatcher. Every outcome, replay included, is written as the dispatcher produced it (R-API-03).
/// </summary>
internal static class CommandEndpoints
{
    internal const string IdempotencyKeyHeader = "Idempotency-Key";
    internal const string IfMatchHeader = "If-Match";
    internal const string ReplayedHeader = "Idempotency-Replayed";
    internal const string ExpectedVersionProperty = "expectedVersion";

    public static IEndpointConventionBuilder MapCommand<TCommand, TResult>(this IEndpointRouteBuilder routes, string pattern)
        where TCommand : ICommand<TResult>
    {
        CommandMetadata metadata = CommandMetadata.For(typeof(TCommand));
        return routes.MapPost(pattern, (HttpContext http, CancellationToken cancellationToken) => HandleAsync<TCommand, TResult>(http, metadata, cancellationToken))
            .WithName(metadata.Name)
            .Accepts<TCommand>(CommandResponse.JsonContentType)
            .Produces<TResult>(StatusCodes.Status200OK, CommandResponse.JsonContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>Maps every route a module declares (ICommandRoute) through <see cref="MapCommand{TCommand, TResult}"/>.</summary>
    public static IEndpointRouteBuilder MapCommandRoutes(this IEndpointRouteBuilder routes, IEnumerable<ICommandRoute> commandRoutes)
    {
        RouteMapper mapper = new(routes);
        foreach (ICommandRoute route in commandRoutes)
        {
            route.Map(mapper);
        }

        return routes;
    }

    private static async Task HandleAsync<TCommand, TResult>(HttpContext http, CommandMetadata metadata, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        CorrelationId correlationId = CorrelationIdMiddleware.Of(http);
        CommandResponse response = await BuildAndDispatchAsync<TCommand, TResult>(http, metadata, correlationId, cancellationToken);

        http.Response.StatusCode = response.StatusCode;
        http.Response.ContentType = response.ContentType + "; charset=utf-8";
        if (response.Replayed)
        {
            http.Response.Headers[ReplayedHeader] = "true";
        }

        await http.Response.WriteAsync(response.Body, cancellationToken);
    }

    private static async Task<CommandResponse> BuildAndDispatchAsync<TCommand, TResult>(HttpContext http, CommandMetadata metadata, CorrelationId correlationId, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        // R-API-01: the header is required; the dispatcher also refuses a missing key for non-HTTP callers.
        if (!TryHeader(http, IdempotencyKeyHeader, out string? keyText))
        {
            return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "idempotency_key_required", "R-API-01", $"The {IdempotencyKeyHeader} header is required on every command.", correlationId);
        }

        if (!IdempotencyKey.TryFrom(keyText, out IdempotencyKey idempotencyKey))
        {
            return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "idempotency_key_invalid", "R-API-01", $"The {IdempotencyKeyHeader} header holds 1 to {IdempotencyKey.MaxLength} characters from A-Z, a-z, 0-9, '.', '_', ':' and '-'.", correlationId);
        }

        JsonObject body;
        try
        {
            body = await ReadBodyAsync(http.Request, cancellationToken);
        }
        catch (JsonException exception)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "request_body_invalid", null, exception.Message, correlationId);
        }

        foreach ((string key, object? value) in http.Request.RouteValues)
        {
            if (value is string text)
            {
                body[JsonNamingPolicy.CamelCase.ConvertName(key)] = text;
            }
        }

        if (metadata.IsVersioned)
        {
            // R-API-02: the header carries the version; the body never does.
            if (!TryHeader(http, IfMatchHeader, out string? ifMatch))
            {
                return ProblemDetailsBody.Http(StatusCodes.Status428PreconditionRequired, "if_match_required", "R-API-02", $"The {IfMatchHeader} header with the record version is required on a command that changes an existing record.", correlationId);
            }

            if (!TryParseVersion(ifMatch, out long expectedVersion))
            {
                return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "if_match_invalid", "R-API-02", $"The {IfMatchHeader} header carries the record version as an entity tag, for example \"7\".", correlationId);
            }

            body[ExpectedVersionProperty] = expectedVersion;
        }

        TCommand command;
        try
        {
            command = body.Deserialize<TCommand>(ThemisJson.Options)
                ?? throw new JsonException("The request body is empty.");
        }
        catch (JsonException exception)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "request_body_invalid", null, exception.Message, correlationId);
        }

        ICommandDispatcher dispatcher = http.RequestServices.GetRequiredService<ICommandDispatcher>();
        return await dispatcher.DispatchAsync<TCommand, TResult>(command, new CommandEnvelope(idempotencyKey, correlationId), cancellationToken);
    }

    private static async Task<JsonObject> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength == 0 || (!request.HasJsonContentType() && request.ContentLength is null))
        {
            return [];
        }

        if (!request.HasJsonContentType())
        {
            throw new JsonException($"The request body must be {CommandResponse.JsonContentType}.");
        }

        JsonNode? node = await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken);
        return node switch
        {
            null => [],
            JsonObject json => json,
            _ => throw new JsonException("The request body must be a JSON object."),
        };
    }

    private static bool TryHeader(HttpContext http, string name, out string? value)
    {
        value = http.Request.Headers.TryGetValue(name, out StringValues values) ? values.FirstOrDefault() : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>Accepts the version as a strong or weak entity tag ("7", W/"7") or bare (7).</summary>
    internal static bool TryParseVersion(string? ifMatch, out long version)
    {
        version = 0;
        if (ifMatch is null)
        {
            return false;
        }

        ReadOnlySpan<char> text = ifMatch.AsSpan().Trim();
        if (text.StartsWith("W/", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1];
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= 0;
    }

    private sealed class RouteMapper : ICommandRouteMapper
    {
        private readonly IEndpointRouteBuilder _routes;

        public RouteMapper(IEndpointRouteBuilder routes)
        {
            _routes = routes;
        }

        public void Map<TCommand, TResult>(string pattern)
            where TCommand : ICommand<TResult> => _routes.MapCommand<TCommand, TResult>(pattern);
    }
}
