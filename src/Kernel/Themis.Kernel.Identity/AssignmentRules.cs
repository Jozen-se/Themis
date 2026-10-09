using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Identity;

/// <summary>One assignment that carries the permission under test, with its company's time zone.</summary>
internal sealed record PermissionAssignment(Guid CompanyId, Guid? SiteId, BusinessDate ValidFrom, BusinessDate? ValidTo, string CompanyTimeZone);

/// <summary>Why an authorisation fails, mapped to the distinct 403 codes of R-SEC-01.</summary>
internal enum AuthorizationOutcome
{
    Allowed,

    /// <summary>No assignment valid today carries the permission (403 permission_missing).</summary>
    PermissionMissing,

    /// <summary>A valid assignment carries it, but none covers the request's company or site (403 out_of_scope).</summary>
    OutOfScope,
}

/// <summary>
/// The scope and validity rules of R-SEC-01 as a pure function (Build Brief, Authorisation; D-47):
/// <list type="bullet">
/// <item>An assignment is valid on its company's business date (R-TIME-01), from valid_from inclusive to valid_to exclusive.</item>
/// <item>A company scope needs an assignment in that company without a site; a site scope accepts an assignment in
/// that company without a site or limited to that site; a site-limited assignment never satisfies a request
/// without a site.</item>
/// <item>Installation scope accepts a company-wide assignment in any company: the installation has no assignments
/// of its own (RoleAssignment requires a company), and a site-limited one is as narrow for the installation as for
/// its company.</item>
/// </list>
/// </summary>
internal static class AssignmentRules
{
    public static AuthorizationOutcome Evaluate(IReadOnlyList<PermissionAssignment> assignments, RequestScope scope, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        bool anyValid = false;
        foreach (PermissionAssignment assignment in assignments)
        {
            if (!IsValidOn(assignment, now))
            {
                continue;
            }

            anyValid = true;
            if (Covers(assignment, scope))
            {
                return AuthorizationOutcome.Allowed;
            }
        }

        return anyValid ? AuthorizationOutcome.OutOfScope : AuthorizationOutcome.PermissionMissing;
    }

    /// <summary>valid_from inclusive, valid_to exclusive, judged on the business date in the assignment's company (R-TIME-01).</summary>
    public static bool IsValidOn(PermissionAssignment assignment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        BusinessDate today = BusinessDate.InZone(now, assignment.CompanyTimeZone);
        return assignment.ValidFrom <= today && (assignment.ValidTo is not { } end || today < end);
    }

    public static bool Covers(PermissionAssignment assignment, RequestScope scope)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return scope.Kind switch
        {
            // D-47: a site-limited assignment never satisfies a command or query without a site.
            ScopeKind.Installation => assignment.SiteId is null,
            ScopeKind.Company => assignment.SiteId is null && assignment.CompanyId == scope.CompanyId,
            ScopeKind.Site => assignment.CompanyId == scope.CompanyId && (assignment.SiteId is null || assignment.SiteId == scope.SiteId),
            _ => false,
        };
    }
}
