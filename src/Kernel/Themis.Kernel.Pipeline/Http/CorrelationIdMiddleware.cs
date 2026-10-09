using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>
/// Gives every request a correlation id (D-25). An inbound <c>X-Correlation-Id</c> header is honoured so that an
/// integration can trace its own call; otherwise the W3C trace id of the request is used. The id is echoed on the
/// response, attached to the current activity and pushed as a logging scope, so every log line of the request
/// carries it, and the command pipeline copies it onto audit events (R-AUD-01).
/// </summary>
internal sealed class CorrelationIdMiddleware
{
    internal const string HeaderName = "X-Correlation-Id";
    internal const string ActivityTagName = "themis.correlation_id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        CorrelationId correlationId = Resolve(context);

        context.Items[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId.Value;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag(ActivityTagName, correlationId.Value);

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId.Value }))
        {
            await _next(context);
        }
    }

    /// <summary>The request's correlation id as the middleware resolved it, or a fresh one when the middleware did not run.</summary>
    public static CorrelationId Of(HttpContext context) =>
        context.Items.TryGetValue(HeaderName, out object? value) && value is CorrelationId id ? id : Resolve(context);

    private static CorrelationId Resolve(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out StringValues values) && CorrelationId.TryFrom(values.FirstOrDefault(), out CorrelationId sent))
        {
            return sent;
        }

        return CorrelationIds.Fresh();
    }
}

internal static class CorrelationIdApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.UseMiddleware<CorrelationIdMiddleware>();
}
