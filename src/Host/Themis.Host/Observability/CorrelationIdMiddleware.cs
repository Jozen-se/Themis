using System.Diagnostics;
using Microsoft.Extensions.Primitives;

namespace Themis.Host.Observability;

/// <summary>
/// Gives every request a correlation id (D-25). An inbound <c>X-Correlation-Id</c> header is honoured so that an
/// integration can trace its own call; otherwise the W3C trace id of the request is used. The id is echoed on the
/// response, attached to the current activity and pushed as a logging scope, so every log line of the request
/// carries it. The command pipeline will later copy it onto audit events (R-AUD-01).
/// </summary>
internal sealed class CorrelationIdMiddleware
{
    internal const string HeaderName = "X-Correlation-Id";
    internal const string ActivityTagName = "themis.correlation_id";
    private const int MaxLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = ResolveCorrelationId(context);

        context.Items[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag(ActivityTagName, correlationId);

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out StringValues values))
        {
            string? candidate = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= MaxLength && candidate.All(IsSafeCharacter))
            {
                return candidate;
            }
        }

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }

    private static bool IsSafeCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':';
}

internal static class CorrelationIdApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.UseMiddleware<CorrelationIdMiddleware>();
}
