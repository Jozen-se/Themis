using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>
/// Gives every request a correlation id (D-25). An inbound <c>X-Correlation-Id</c> header is honoured so that an
/// integration can trace its own call, when it is one value of at most 128 ASCII letters, digits, dots, dashes,
/// underscores and colons; anything else is replaced by a generated id and the replacement logged at warning
/// (RR-53; the command id stays the trusted audit link, D-51). Otherwise the W3C trace id of the request is used.
/// The id is echoed on the response, attached to the current activity and pushed as a logging scope, so every log
/// line of the request carries it, and the command pipeline copies it onto audit events (R-AUD-01).
/// </summary>
internal sealed partial class CorrelationIdMiddleware
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
        CorrelationId correlationId = Resolve(context, out bool replaced);
        if (replaced)
        {
            LogReplaced(_logger, correlationId.Value);
        }

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
        context.Items.TryGetValue(HeaderName, out object? value) && value is CorrelationId id ? id : Resolve(context, out _);

    /// <summary>The caller's id when the header holds exactly one acceptable value; else a fresh one, with <paramref name="replaced"/> set when a header was sent and refused (RR-53).</summary>
    private static CorrelationId Resolve(HttpContext context, out bool replaced)
    {
        replaced = false;
        if (!context.Request.Headers.TryGetValue(HeaderName, out StringValues values))
        {
            return CorrelationIds.Fresh();
        }

        if (values.Count == 1 && CorrelationId.TryFrom(values[0], out CorrelationId sent))
        {
            return sent;
        }

        replaced = true;
        return CorrelationIds.Fresh();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The X-Correlation-Id header was refused (one value of at most 128 ASCII letters, digits, dots, dashes, underscores and colons) and replaced by {CorrelationId} (RR-53).")]
    private static partial void LogReplaced(ILogger logger, string correlationId);
}

internal static class CorrelationIdApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.UseMiddleware<CorrelationIdMiddleware>();
}
