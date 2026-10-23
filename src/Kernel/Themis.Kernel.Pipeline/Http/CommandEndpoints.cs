using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Pipeline.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>
/// Maps a command to <c>POST {pattern}</c> (brief, API conventions). The endpoint reads the <c>Idempotency-Key</c>
/// header (R-API-01), the <c>If-Match</c> header for versioned commands (R-API-02) and the request body, merges the
/// route values and the version into the body (route and header win over the body: a body member that names a route
/// value in any letter case must agree with it, else 400 route_body_mismatch, and every member naming the version is
/// dropped, RR-73), binds the command and hands it to the dispatcher. Every outcome, replay included, is written as
/// the dispatcher produced it (R-API-03).
/// </summary>
internal static class CommandEndpoints
{
    internal const string IdempotencyKeyHeader = "Idempotency-Key";
    internal const string IfMatchHeader = "If-Match";
    internal const string ReplayedHeader = "Idempotency-Replayed";
    internal const string ExpectedVersionProperty = "expectedVersion";
    internal const string RouteBodyMismatch = "route_body_mismatch";

    public static IEndpointConventionBuilder MapCommand<TCommand, TResult>(this IEndpointRouteBuilder routes, string pattern)
        where TCommand : ICommand<TResult>
    {
        CommandMetadata metadata = CommandMetadata.For(typeof(TCommand));
        // D-47: a command that names no scope cannot be mapped; the host fails to start instead of allowing it.
        ScopeSources.RequireDeclared(typeof(TCommand), routes.ServiceProvider);
        // D-58: every command stamps its idempotency record from the settings; a host without the settings reader fails to start, not with a 500 later.
        RequireSettingsReader(routes.ServiceProvider);
        // Endpoint names are global and a permission can be shared (the fixture module's commands stand in with the real ones' permissions), so the command type names the endpoint.
        return routes.MapPost(pattern, (HttpContext http, CancellationToken cancellationToken) => HandleAsync<TCommand, TResult>(http, metadata, cancellationToken))
            .WithName(typeof(TCommand).Name)
            .WithSummary(metadata.Name)
            .WithMetadata(metadata)
            .Accepts<TCommand>(CommandResponse.JsonContentType)
            .Produces<TResult>(StatusCodes.Status200OK, CommandResponse.JsonContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>D-58: a host that maps commands registers the settings reader, which stamps every idempotency record; checked at mapping time, like the scope (D-47).</summary>
    private static void RequireSettingsReader(IServiceProvider services)
    {
        if (!ScopeSources.IsRegistered(typeof(ISettingsReader), services))
        {
            throw new InvalidOperationException("A command is mapped but no ISettingsReader is registered: call AddKernelSettings() in the composition root; the pipeline reads idempotency.retention_days from it (D-58).");
        }
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
        // RR-71: no subject, no work; the fallback policy answers first, this guards a host that maps the endpoint without it.
        if (http.RequestServices.GetRequiredService<ICurrentSubject>().Current is null)
        {
            await Unauthorized.WriteAsync(http);
            return;
        }

        CorrelationId correlationId = CorrelationIdMiddleware.Of(http);
        CommandResponse response = await BuildAndDispatchAsync<TCommand, TResult>(http, metadata, correlationId, cancellationToken);

        if (response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            // RR-71: the dispatcher's own 401 goes out through the shared writer, so it looks like the fallback's.
            await Unauthorized.WriteAsync(http);
            return;
        }

        http.Response.StatusCode = response.StatusCode;
        http.Response.ContentType = response.ContentType + "; charset=utf-8";
        if (response.Replayed)
        {
            http.Response.Headers[ReplayedHeader] = "true";
        }

        if (response.Version is { } version)
        {
            // R-API-02: the new version goes back as a strong entity tag, ready for the next If-Match.
            http.Response.Headers.ETag = string.Create(CultureInfo.InvariantCulture, $"\"{version}\"");
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

        // RR-73: the binder matches members without regard to letter case, so a body member that names a route value in
        // any case must agree with it, or the request is refused; an agreeing one is replaced by the route value.
        foreach ((string key, object? value) in http.Request.RouteValues)
        {
            if (value is not string text)
            {
                continue;
            }

            foreach (string member in MembersNamed(body, key))
            {
                if (!SameValue(body[member], text))
                {
                    return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, RouteBodyMismatch, null, $"The body member '{member}' differs from the route value '{key}'; the route names the record.", correlationId);
                }

                body.Remove(member);
            }

            body[JsonNamingPolicy.CamelCase.ConvertName(key)] = text;
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

            // RR-73: every body member that names the version, in any letter case, goes; the header alone carries it.
            foreach (string member in MembersNamed(body, ExpectedVersionProperty))
            {
                body.Remove(member);
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

    /// <summary>The body members whose name equals <paramref name="name"/> without regard to letter case (RR-73).</summary>
    private static string[] MembersNamed(JsonObject body, string name) =>
        [.. body.Select(member => member.Key).Where(member => string.Equals(member, name, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Whether a body member carries the route value (RR-73): two ids are compared as Guid values, so the same id in
    /// upper case agrees; any other value must be the same string.
    /// </summary>
    internal static bool SameValue(JsonNode? member, string routeValue)
    {
        if (member is not JsonValue value || !value.TryGetValue(out string? text))
        {
            return false;
        }

        if (Guid.TryParse(routeValue, out Guid routeId) && Guid.TryParse(text, out Guid memberId))
        {
            return routeId == memberId;
        }

        return string.Equals(text, routeValue, StringComparison.Ordinal);
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
