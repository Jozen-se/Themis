using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>
/// Maps a query to <c>GET {pattern}</c> (P-04). Route values and query string parameters become the query's JSON
/// members (route values win), the query is bound with the API's JSON options and handed to the dispatcher.
/// </summary>
internal static class QueryEndpoints
{
    /// <summary>Marks a query endpoint for the OpenAPI operation transformer.</summary>
    internal sealed record QueryMetadata(string Permission);

    public static IEndpointConventionBuilder MapQuery<TQuery, TResult>(this IEndpointRouteBuilder routes, string pattern)
        where TQuery : IQuery<TResult>
    {
        string permission = QueryDispatcher.PermissionOf(typeof(TQuery));
        return routes.MapGet(pattern, (HttpContext http, CancellationToken cancellationToken) => HandleAsync<TQuery, TResult>(http, cancellationToken))
            .WithName(permission)
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
        CorrelationId correlationId = CorrelationIdMiddleware.Of(http);
        CommandResponse response = await BindAndDispatchAsync<TQuery, TResult>(http, correlationId, cancellationToken);

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
