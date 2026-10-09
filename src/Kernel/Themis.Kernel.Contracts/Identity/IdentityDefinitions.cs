using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Identity;

/// <summary>User lifecycle (Phase 0 Specification, User). A Disabled user is refused on every request (403 user_disabled).</summary>
public enum UserStatus
{
    Active,
    Disabled,
}

/// <summary>Sensitive permissions are reviewed in access reviews (Phase 0 Specification, Permission).</summary>
public enum PermissionRiskLevel
{
    Normal,
    Sensitive,
}

/// <summary>A system-defined permission: TYPE.COMMAND for a command, TYPE.READ for a resource type's queries (D-47).</summary>
public sealed record PermissionDefinition(string Code, PermissionRiskLevel RiskLevel);

/// <summary>Two permissions one user may not hold in the same company (R-SEC-02). Stored with the codes in order, so a pair exists once.</summary>
public sealed record SodConflictDefinition(string PermissionA, string PermissionB);

/// <summary>
/// The permissions and segregation-of-duties pairs of this build of Themis: system data, seeded by Themis.Migrator
/// on every run and never edited by a command (Phase 0 Specification, Security entities; R-SEC-02).
/// </summary>
public sealed record PermissionCatalogDefinition(IReadOnlyList<PermissionDefinition> Permissions, IReadOnlyList<SodConflictDefinition> Conflicts);

/// <summary>A user as the fixture defines it: the OpenID Connect subject it maps to, display name, email (PD), kind and status.</summary>
public sealed record UserDefinition(Guid Id, string ExternalSubject, string DisplayName, string? Email, bool IsServiceAccount, UserStatus Status);

/// <summary>A role with the permission codes it grants (Phase 0 Specification, Role and RolePermission).</summary>
public sealed record RoleDefinition(Guid Id, Code Code, string Name, IReadOnlyList<string> Permissions);

/// <summary>
/// A role assigned to a user in a company, optionally narrowed to one site, valid from <paramref name="ValidFrom"/>
/// (inclusive) to <paramref name="ValidTo"/> (exclusive) on the company's business date (Phase 0 Specification,
/// RoleAssignment; R-SEC-01, R-TIME-01).
/// </summary>
public sealed record RoleAssignmentDefinition(Guid Id, Guid UserId, Guid RoleId, Guid CompanyId, Guid? SiteId, BusinessDate ValidFrom, BusinessDate? ValidTo);

/// <summary>The identity rows of a fixture as data, framework-free, written by the persistence capability (ARCH-08).</summary>
public sealed record IdentityFixtureDefinition(IReadOnlyList<UserDefinition> Users, IReadOnlyList<RoleDefinition> Roles, IReadOnlyList<RoleAssignmentDefinition> Assignments);
