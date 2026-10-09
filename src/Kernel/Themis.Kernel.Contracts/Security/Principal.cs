using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Security;

/// <summary>Who runs a command: a user or a service account mapped from the OpenID Connect subject (D-12).</summary>
public sealed record Principal(UserId UserId, string DisplayName, bool IsServiceAccount);

/// <summary>The principal of the current request, or null when the request is unauthenticated.</summary>
public interface ICurrentPrincipal
{
    Principal? Current { get; }
}

/// <summary>
/// Answers whether a principal holds a permission (R-SEC-01). Scope by company and site arrives with the identity
/// capability; the pipeline asks before any transaction starts and again on every retry (D-41).
/// </summary>
public interface IPermissionEvaluator
{
    ValueTask<bool> HasPermissionAsync(Principal principal, string permissionCode, CancellationToken cancellationToken);
}
