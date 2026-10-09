using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.FixtureModule.Api;

/// <summary>
/// Test-only identity: the user comes from the <c>X-Test-User</c> header (a GUID) and the permissions from
/// <c>X-Test-Permissions</c> (comma-separated TYPE.COMMAND codes, or <c>*</c> for all). Lets the integration tests
/// drive the pipeline's authorisation (R-SEC-01) without the identity capability, which arrives in a later step.
/// Never registered by the host.
/// </summary>
internal sealed class HeaderPrincipal : ICurrentPrincipal
{
    public const string UserHeader = "X-Test-User";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderPrincipal(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Principal? Current
    {
        get
        {
            HttpContext? http = _httpContextAccessor.HttpContext;
            if (http is null || !http.Request.Headers.TryGetValue(UserHeader, out StringValues values) || !Guid.TryParse(values.FirstOrDefault(), out Guid userId))
            {
                return null;
            }

            return new Principal(new UserId(userId), "Test user " + userId.ToString("N")[..8], IsServiceAccount: false);
        }
    }
}

internal sealed class HeaderPermissionEvaluator : IPermissionEvaluator
{
    public const string PermissionsHeader = "X-Test-Permissions";
    public const string All = "*";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderPermissionEvaluator(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ValueTask<bool> HasPermissionAsync(Principal principal, string permissionCode, CancellationToken cancellationToken)
    {
        HttpContext? http = _httpContextAccessor.HttpContext;
        if (http is null || !http.Request.Headers.TryGetValue(PermissionsHeader, out StringValues values))
        {
            return ValueTask.FromResult(false);
        }

        string[] granted = (values.FirstOrDefault() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ValueTask.FromResult(granted.Contains(All, StringComparer.Ordinal) || granted.Contains(permissionCode, StringComparer.Ordinal));
    }
}
