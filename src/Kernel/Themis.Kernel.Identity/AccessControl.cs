using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Identity;

/// <summary>
/// Access control on the request's unit of work as themis_kernel (R-DB-01): the subject's User row becomes the
/// principal, and the permission is checked against the role assignments that carry it, in the request's scope,
/// on the company's business date (R-SEC-01, R-TIME-01, D-47). Every rejection is a 403 with one of the codes in
/// <see cref="AccessDenied"/>. The system user is refused like an unknown subject: it owns seeded rows and never
/// acts through the API.
/// </summary>
internal sealed class AccessControl : IAccessControl
{
    // RR-62: both methods hand the connection back as themis_app on successful return.
    public Task<Result<Principal>> ResolvePrincipalAsync(IUnitOfWork unitOfWork, AuthenticatedSubject subject, CancellationToken cancellationToken) =>
        ResolvePrincipalCoreAsync(unitOfWork, subject, cancellationToken).ThenResetRoleAsync(unitOfWork, cancellationToken);

    public Task<Result> AuthorizeAsync(IUnitOfWork unitOfWork, Principal principal, string permissionCode, RequestScope scope, DateTimeOffset now, CancellationToken cancellationToken) =>
        AuthorizeCoreAsync(unitOfWork, principal, permissionCode, scope, now, cancellationToken).ThenResetRoleAsync(unitOfWork, cancellationToken);

    private static async Task<Result<Principal>> ResolvePrincipalCoreAsync(IUnitOfWork unitOfWork, AuthenticatedSubject subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(subject);
        IReadOnlyList<(Guid Id, string DisplayName, bool IsServiceAccount, string Status)> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            "SELECT id, display_name, is_service_account, status FROM kernel.user_account WHERE external_subject = @subject",
            new Dictionary<string, object?> { ["subject"] = subject.Subject },
            reader => (reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2), reader.GetString(3)),
            cancellationToken);

        // D-12: no user is created on login; a subject the ERP does not know is refused. The system user is never a caller.
        if (rows.Count == 0 || rows[0].Id == WellKnownUsers.System.Value)
        {
            return Error.Forbidden(AccessDenied.SubjectUnknown, "The signed-in subject is not a user of this installation.", AccessDenied.RuleId);
        }

        (Guid id, string displayName, bool isServiceAccount, string status) = rows[0];
        if (!string.Equals(status, nameof(UserStatus.Active), StringComparison.Ordinal))
        {
            return Error.Forbidden(AccessDenied.UserDisabled, "The user is disabled.", AccessDenied.RuleId);
        }

        return new Principal(new UserId(id), displayName, isServiceAccount);
    }

    private static async Task<Result> AuthorizeCoreAsync(IUnitOfWork unitOfWork, Principal principal, string permissionCode, RequestScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(principal);
        PermissionCodes.Validated(permissionCode);

        // R-SEC-01: every assignment of the user whose role grants the permission, with the company's time zone
        // for the validity check on the business date (R-TIME-01).
        IReadOnlyList<PermissionAssignment> assignments = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            """
            SELECT a.company_id, a.site_id, a.valid_from, a.valid_to, c.time_zone
            FROM kernel.role_assignment a
            JOIN kernel.role_permission rp ON rp.role_id = a.role_id AND rp.permission_code = @permission
            JOIN kernel.company c ON c.id = a.company_id
            WHERE a.user_id = @user
            """,
            new Dictionary<string, object?> { ["permission"] = permissionCode, ["user"] = principal.UserId.Value },
            reader => new PermissionAssignment(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                new BusinessDate(reader.GetFieldValue<DateOnly>(2)),
                reader.IsDBNull(3) ? null : new BusinessDate(reader.GetFieldValue<DateOnly>(3)),
                reader.GetString(4)),
            cancellationToken);

        return AssignmentRules.Evaluate(assignments, scope, now) switch
        {
            AuthorizationOutcome.Allowed => Result.Success(),
            AuthorizationOutcome.PermissionMissing => Error.Forbidden(AccessDenied.PermissionMissing, $"The user lacks permission {permissionCode}.", AccessDenied.RuleId),
            _ => Error.Forbidden(AccessDenied.OutOfScope, $"The user holds {permissionCode}, but not for {scope}.", AccessDenied.RuleId),
        };
    }
}
