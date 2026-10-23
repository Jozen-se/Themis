using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Http;

namespace Themis.Kernel.Pipeline.Security;

/// <summary>
/// 401 first (RR-71, R-SEC-01): a fallback authorization policy requires an authenticated user on every endpoint that
/// does not opt out with AllowAnonymous (the health endpoints, OpenAPI and Scalar), so an anonymous request is
/// answered before any header, body or database is looked at. Without an identity provider a scheme that
/// authenticates nobody is the default, so the fallback still answers 401, never 500. Registered by AddKernelPipeline;
/// UseKernelRequestPipeline places UseAuthentication and UseAuthorization.
/// </summary>
internal static class KernelAuthentication
{
    /// <summary>The scheme that stands in when no identity provider is configured: nobody is authenticated.</summary>
    public const string DenyScheme = "Themis.Deny";

    public static IServiceCollection AddKernelAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, DenyAuthenticationHandler>(DenyScheme, displayName: null, configureOptions: null);
        // The identity capability sets its bearer scheme as the default; only when none is set does the deny scheme answer.
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            if (options.DefaultScheme is null && options.DefaultAuthenticateScheme is null && options.DefaultChallengeScheme is null)
            {
                options.DefaultScheme = DenyScheme;
            }
        });
        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, KernelAuthorizationResultHandler>();
        return services;
    }
}

/// <summary>
/// The one 401 writer (RR-71): the problem body with code authentication_required under R-SEC-01 and the request's
/// correlation id, and <c>WWW-Authenticate: Bearer</c> without error details. Used by the authorization result
/// handler, the deny scheme, CommandEndpoints and QueryEndpoints.
/// </summary>
internal static class Unauthorized
{
    public const string Code = "authentication_required";
    public const string Challenge = "Bearer";

    public static CommandResponse Body(HttpContext http) =>
        ProblemDetailsBody.Http(StatusCodes.Status401Unauthorized, Code, AccessDenied.RuleId, "The request requires a signed-in user.", CorrelationIdMiddleware.Of(http));

    public static async Task WriteAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        CommandResponse response = Body(http);
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Headers.WWWAuthenticate = Challenge;
        http.Response.ContentType = response.ContentType + "; charset=utf-8";
        await http.Response.WriteAsync(response.Body, http.RequestAborted);
    }
}

/// <summary>A challenge from the fallback policy is written by <see cref="Unauthorized"/>, not by the scheme's own challenge, so no token error details leave the host.</summary>
internal sealed class KernelAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(authorizeResult);
        return authorizeResult.Challenged ? Unauthorized.WriteAsync(context) : _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

/// <summary>Authenticates nobody; its challenge is the shared 401.</summary>
internal sealed class DenyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DenyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => Unauthorized.WriteAsync(Context);
}
