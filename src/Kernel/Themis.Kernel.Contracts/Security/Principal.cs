using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Security;

/// <summary>Who runs a command or query: a user or a service account, resolved from the OpenID Connect subject (D-12).</summary>
public sealed record Principal(UserId UserId, string DisplayName, bool IsServiceAccount);

/// <summary>
/// The authenticated subject of the current request: the token's <c>sub</c> claim, the only claim Themis maps
/// (Build Brief, Authorisation). Roles, email and every other claim are ignored; permissions and scope are held in
/// the ERP (D-12, R-SEC-01).
/// </summary>
public sealed record AuthenticatedSubject(string Subject);

/// <summary>The subject of the current request, or null when the request carries no valid token (401).</summary>
public interface ICurrentSubject
{
    AuthenticatedSubject? Current { get; }
}

/// <summary>
/// Access control as the pipeline applies it, on the command's own unit of work (R-DB-01), before any business
/// statement and again on every retry (D-41):
/// <list type="number">
/// <item>The subject becomes a principal through its User row. An unknown subject or a disabled user is refused with
/// 403 (codes <c>subject_unknown</c>, <c>user_disabled</c>); no user is created on login (D-12).</item>
/// <item>The principal must hold the permission through a role assigned in the request's scope and valid on the
/// company's business date (R-SEC-01, R-TIME-01), else 403 (<c>permission_missing</c>, <c>out_of_scope</c>).</item>
/// </list>
/// </summary>
public interface IAccessControl
{
    Task<Result<Principal>> ResolvePrincipalAsync(IUnitOfWork unitOfWork, AuthenticatedSubject subject, CancellationToken cancellationToken);

    Task<Result> AuthorizeAsync(IUnitOfWork unitOfWork, Principal principal, string permissionCode, RequestScope scope, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>The stable codes of the 403 rejections, all under R-SEC-01 (Build Brief, Authorisation).</summary>
public static class AccessDenied
{
    public const string RuleId = "R-SEC-01";

    /// <summary>A valid token whose subject has no User row.</summary>
    public const string SubjectUnknown = "subject_unknown";

    /// <summary>The subject's User row is Disabled.</summary>
    public const string UserDisabled = "user_disabled";

    /// <summary>No valid assignment of the principal carries the permission.</summary>
    public const string PermissionMissing = "permission_missing";

    /// <summary>The permission is held, but not for the company or site the request names.</summary>
    public const string OutOfScope = "out_of_scope";

    /// <summary>The command or query named no scope: refused, never allowed by default (D-47).</summary>
    public const string ScopeUndeclared = "scope_undeclared";
}
