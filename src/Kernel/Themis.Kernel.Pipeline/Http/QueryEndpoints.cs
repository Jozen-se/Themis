using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Pipeline.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>
/// Maps a query to <c>GET {pattern}</c> (P-04). Route values and query string parameters become the query's JSON
/// members (route values win; a repeated query string key is a 400, RR-56), the query is bound with the API's JSON
/// options and handed to the dispatcher.
/// </summary>
internal static class QueryEndpoints
{
    /// <summary>Marks a query endpoint for the OpenAPI operation transformer.</summary>
    internal sealed record QueryMetadata(string Permission);

    public static IEndpointConventionBuilder MapQuery<TQuery, TResult>(this IEndpointRouteBuilder routes, string pattern)
        where TQuery : IQuery<TResult>
    {
        string permission = QueryDispatcher.PermissionOf(typeof(TQuery));
        // D-47: a query that names no scope cannot be mapped; the host fails to start instead of allowing it.
        ScopeSources.RequireDeclared(typeof(TQuery), routes.ServiceProvider);
        // Endpoint names are global; several queries share one TYPE.READ permission (D-47), so the query type names the endpoint.
        return routes.MapGet(pattern, (HttpContext http, CancellationToken cancellationToken) => HandleAsync<TQuery, TResult>(http, cancellationToken))
            .WithName(typeof(TQuery).Name)
            .WithSummary(permission)
            .WithMetadata(new QueryMetadata(permission))
            .Produces<TResult>(StatusCodes.Status200OK, CommandResponse.JsonContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task HandleAsync<TQuery, TResult>(HttpContext http, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        // RR-71: no subject, no work; the fallback policy answers first, this guards a host that maps the endpoint without it.
        if (http.RequestServices.GetRequiredService<ICurrentSubject>().Current is null)
        {
            await Unauthorized.WriteAsync(http);
            return;
        }

        CorrelationId correlationId = CorrelationIdMiddleware.Of(http);
        CommandResponse response = await BindAndDispatchAsync<TQuery, TResult>(http, correlationId, cancellationToken);

        if (response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            // RR-71: the dispatcher's own 401 goes out through the shared writer, so it looks like the fallback's.
            await Unauthorized.WriteAsync(http);
            return;
        }

        http.Response.StatusCode = response.StatusCode;
        http.Response.ContentType = response.ContentType + "; charset=utf-8";
        if (response.Version is { } version)
        {
            http.Response.Headers.ETag = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"\"{version}\"");
        }

        await http.Response.WriteAsync(response.Body, cancellationToken);
    }

    private static async Task<CommandResponse> BindAndDispatchAsync<TQuery, TResult>(HttpContext http, CorrelationId correlationId, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        JsonObject members = [];
        foreach ((string key, Microsoft.Extensions.Primitives.StringValues values) in http.Request.Query)
        {
            // RR-56: a key given twice is refused instead of its values being joined silently.
            if (values.Count > 1)
            {
                return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "query_parameter_repeated", null, $"The query string parameter '{key}' appears more than once.", correlationId);
            }

            members[JsonNamingPolicy.CamelCase.ConvertName(key)] = values.ToString();
        }

        foreach ((string key, object? value) in http.Request.RouteValues)
        {
            if (value is string text)
            {
                members[JsonNamingPolicy.CamelCase.ConvertName(key)] = text;
            }
        }

        TQuery query;
        try
        {
            query = members.Deserialize<TQuery>(ThemisJson.Options) ?? throw new JsonException("The query has no members.");
        }
        catch (JsonException exception)
        {
            return ProblemDetailsBody.Http(StatusCodes.Status400BadRequest, "request_invalid", null, exception.Message, correlationId);
        }

        IQueryDispatcher dispatcher = http.RequestServices.GetRequiredService<IQueryDispatcher>();
        return await dispatcher.DispatchAsync<TQuery, TResult>(query, correlationId, cancellationToken);
    }
}
