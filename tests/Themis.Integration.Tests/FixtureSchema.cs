using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence.Privileges;

namespace Themis.Integration.Tests;

/// <summary>
/// The fixture module's table, created the way a module migration will create it: as themis_migrator, with the
/// grants of a declared privilege matrix rendered by the grant helper (I-14). The matrix is the one
/// <see cref="PrivilegeMatrixTests"/> adds to the kernel's declared set. The same script seeds the test-only
/// permission FIXTURE.TOUCH with its role and the clerk's assignment (owner's decision on step 7, point 3), and the
/// test-only FIXTURE.READ of the write-attempting query seam (RR-81) on the same role, so the
/// fixture's touch command needs no real permission; the rows are test scaffolding outside the production catalogue
/// and the seeders, idempotent and without audit events.
/// </summary>
internal static class FixtureSchema
{
    public static Guid TouchPermissionId { get; } = Guid.Parse("00000000-0000-7000-8000-0000000f0001");
    public static Guid TouchRoleId { get; } = Guid.Parse("00000000-0000-7000-8000-0000000f0002");
    public static Guid TouchRolePermissionId { get; } = Guid.Parse("00000000-0000-7000-8000-0000000f0003");
    public static Guid ClerkTouchAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-0000000f0004");
    public static Guid ReadPermissionId { get; } = Guid.Parse("00000000-0000-7000-8000-0000000f0005");
    public static Guid ReadRolePermissionId { get; } = Guid.Parse("00000000-0000-7000-8000-0000000f0006");

    public static IReadOnlyList<TableGrant> Declared { get; } =
    [
        TableGrant.Mutable(FixtureContracts.SchemaName, "counter", FixtureContracts.RoleName),
    ];

    public static string CreateSql =>
        $"""
        CREATE TABLE IF NOT EXISTS {FixtureContracts.SchemaName}.counter (
            id uuid PRIMARY KEY,
            company_id uuid NOT NULL,
            site_id uuid NULL,
            value bigint NOT NULL,
            version bigint NOT NULL,
            sample numeric(20, 4) NOT NULL,
            posted boolean NOT NULL DEFAULT false
        );
        {string.Join("\n", Declared.SelectMany(GrantSql.Statements))}
        {TouchPermissionSql}
        """;

    /// <summary>The test-only permission, role and the clerk's company-wide assignment; every statement is idempotent.</summary>
    private static string TouchPermissionSql
    {
        get
        {
            string system = WellKnownUsers.System.Value.ToString();
            string audit = $"now(), '{system}', now(), '{system}'";
            return $"""
                INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by)
                VALUES ('{TouchPermissionId}', '{FixtureContracts.TouchPermission}', 'Normal', {audit}) ON CONFLICT (code) DO NOTHING;
                INSERT INTO kernel.role (id, code, name, created_at, created_by, updated_at, updated_by)
                VALUES ('{TouchRoleId}', '{FixtureContracts.TouchRoleCode}', 'Fixture tester (test only)', {audit}) ON CONFLICT (code) DO NOTHING;
                INSERT INTO kernel.role_permission (id, role_id, permission_code, created_at, created_by, updated_at, updated_by)
                VALUES ('{TouchRolePermissionId}', '{TouchRoleId}', '{FixtureContracts.TouchPermission}', {audit}) ON CONFLICT (role_id, permission_code) DO NOTHING;
                INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by)
                VALUES ('{ReadPermissionId}', '{FixtureContracts.ReadPermission}', 'Normal', {audit}) ON CONFLICT (code) DO NOTHING;
                INSERT INTO kernel.role_permission (id, role_id, permission_code, created_at, created_by, updated_at, updated_by)
                VALUES ('{ReadRolePermissionId}', '{TouchRoleId}', '{FixtureContracts.ReadPermission}', {audit}) ON CONFLICT (role_id, permission_code) DO NOTHING;
                INSERT INTO kernel.role_assignment (id, user_id, role_id, company_id, site_id, valid_from, valid_to, assigned_by, created_at, created_by, updated_at, updated_by)
                VALUES ('{ClerkTouchAssignmentId}', '{Milestone1IdentityFixture.ClerkId}', '{TouchRoleId}', '{Milestone1OrganisationFixture.CompanyId}', NULL, '2026-01-01', NULL, '{system}', {audit}) ON CONFLICT (id) DO NOTHING;
                """;
        }
    }
}
