using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence.Entities.Identity;

/// <summary>
/// Phase 0 Specification, User: the OpenID Connect subject it maps to (unique; no passwords are stored), display
/// name and email (PD), service account flag, status Active or Disabled. Table user_account, because "user" is a
/// reserved word in PostgreSQL. Never deleted: a user is Disabled (R-MD-03).
/// </summary>
internal sealed class UserAccount : MasterDataRow
{
    public required string ExternalSubject { get; set; }

    public required string DisplayName { get; set; }

    public string? Email { get; set; }

    public required bool IsServiceAccount { get; set; }

    public required string Status { get; set; }
}

/// <summary>Phase 0 Specification, Permission: system-defined, code equals the command name; TYPE.READ for queries (D-47).</summary>
internal sealed class Permission : MasterDataRow
{
    public required string Code { get; set; }

    public required string RiskLevel { get; set; }
}

/// <summary>Phase 0 Specification, Role: a named set of permissions, assigned per company (R-SEC-01).</summary>
internal sealed class Role : MasterDataRow
{
    public required string Code { get; set; }

    public required string Name { get; set; }
}

/// <summary>Phase 0 Specification, RolePermission: one permission of a role.</summary>
internal sealed class RolePermission : MasterDataRow
{
    public required Guid RoleId { get; set; }

    public required string PermissionCode { get; set; }
}

/// <summary>
/// Phase 0 Specification, RoleAssignment: a role held by a user in one company, optionally narrowed to one site
/// (scope, R-SEC-01), valid from valid_from inclusive to valid_to exclusive on the company's business date
/// (R-TIME-01). Ends by valid_to, never by deletion.
/// </summary>
internal sealed class RoleAssignment : MasterDataRow
{
    public required Guid UserId { get; set; }

    public required Guid RoleId { get; set; }

    public required Guid CompanyId { get; set; }

    public Guid? SiteId { get; set; }

    public required DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public required Guid AssignedBy { get; set; }
}

/// <summary>Phase 0 Specification, SodConflict: two permissions one user may not hold in the same company (R-SEC-02); codes stored in order.</summary>
internal sealed class SodConflict : MasterDataRow
{
    public required string PermissionA { get; set; }

    public required string PermissionB { get; set; }
}
