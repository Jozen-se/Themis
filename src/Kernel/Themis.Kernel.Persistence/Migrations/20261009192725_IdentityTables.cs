using Microsoft.EntityFrameworkCore.Migrations;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Persistence.Privileges;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 1 step 4: the security entities of the Phase 0 Specification (User as user_account, Permission, Role,
/// RolePermission, RoleAssignment, SodConflict), the system user row that rows written outside commands carry, and
/// the foreign keys from audit_event and idempotency_record to the user, added and validated against the existing
/// rows (R-AUD-01, step 4 brief). Grants are the ones declared in <see cref="KernelPrivilegeMatrix"/> (I-14):
/// SELECT, INSERT and UPDATE for themis_kernel, never DELETE.
/// </summary>
internal sealed partial class IdentityTables : Migration
{
    private static readonly string[] IdCompanyIdColumns = ["id", "company_id"];
    private static readonly string[] SiteIdCompanyIdColumns = ["site_id", "company_id"];
    private static readonly string[] UserIdCompanyIdColumns = ["user_id", "company_id"];
    private static readonly string[] RoleIdPermissionCodeColumns = ["role_id", "permission_code"];
    private static readonly string[] PermissionAPermissionBColumns = ["permission_a", "permission_b"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "permission",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                risk_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_permission", x => x.id);
                table.UniqueConstraint("ak_permission_code", x => x.code);
                table.CheckConstraint("ck_permission_risk_level", "risk_level IN ('Normal', 'Sensitive')");
            });

        migrationBuilder.CreateTable(
            name: "role",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_role", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "sod_conflict",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                permission_a = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                permission_b = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sod_conflict", x => x.id);
                table.CheckConstraint("ck_sod_conflict_order", "permission_a < permission_b");
            });

        migrationBuilder.CreateTable(
            name: "user_account",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                external_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                is_service_account = table.Column<bool>(type: "boolean", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_account", x => x.id);
                table.CheckConstraint("ck_user_account_status", "status IN ('Active', 'Disabled')");
            });

        migrationBuilder.CreateTable(
            name: "role_permission",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                role_id = table.Column<Guid>(type: "uuid", nullable: false),
                permission_code = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_role_permission", x => x.id);
                table.ForeignKey(
                    name: "fk_role_permission_permission",
                    column: x => x.permission_code,
                    principalSchema: "kernel",
                    principalTable: "permission",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_role_permission_role",
                    column: x => x.role_id,
                    principalSchema: "kernel",
                    principalTable: "role",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "role_assignment",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                role_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                site_id = table.Column<Guid>(type: "uuid", nullable: true),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_role_assignment", x => x.id);
                table.CheckConstraint("ck_role_assignment_validity", "valid_to IS NULL OR valid_to > valid_from");
                table.ForeignKey(
                    name: "fk_role_assignment_assigned_by",
                    column: x => x.assigned_by,
                    principalSchema: "kernel",
                    principalTable: "user_account",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_role_assignment_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_role_assignment_role",
                    column: x => x.role_id,
                    principalSchema: "kernel",
                    principalTable: "role",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_role_assignment_site_company",
                    columns: x => new { x.site_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "site",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_role_assignment_user",
                    column: x => x.user_id,
                    principalSchema: "kernel",
                    principalTable: "user_account",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_event_user_id",
            schema: "kernel",
            table: "audit_event",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ux_permission_code",
            schema: "kernel",
            table: "permission",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_role_code",
            schema: "kernel",
            table: "role",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_role_assignment_assigned_by",
            schema: "kernel",
            table: "role_assignment",
            column: "assigned_by");

        migrationBuilder.CreateIndex(
            name: "IX_role_assignment_company_id",
            schema: "kernel",
            table: "role_assignment",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_role_assignment_role_id",
            schema: "kernel",
            table: "role_assignment",
            column: "role_id");

        migrationBuilder.CreateIndex(
            name: "IX_role_assignment_site_id_company_id",
            schema: "kernel",
            table: "role_assignment",
            columns: SiteIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_role_assignment_user_company",
            schema: "kernel",
            table: "role_assignment",
            columns: UserIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_role_permission_permission_code",
            schema: "kernel",
            table: "role_permission",
            column: "permission_code");

        migrationBuilder.CreateIndex(
            name: "ux_role_permission_role_code",
            schema: "kernel",
            table: "role_permission",
            columns: RoleIdPermissionCodeColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_sod_conflict_pair",
            schema: "kernel",
            table: "sod_conflict",
            columns: PermissionAPermissionBColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_user_account_external_subject",
            schema: "kernel",
            table: "user_account",
            column: "external_subject",
            unique: true);

        // The system user owns rows written outside commands: migrations and fixture seeds (step 4 brief, R-AUD-01).
        // It is inserted before the foreign keys below, which validate the existing audit events of step 3.
        string systemId = WellKnownUsers.System.Value.ToString();
        string changes = "{\"externalSubject\": {\"before\": null, \"after\": \"" + WellKnownUsers.SystemSubject + "\"}, \"displayName\": {\"before\": null, \"after\": \"" + WellKnownUsers.SystemDisplayName + "\"}, \"isServiceAccount\": {\"before\": null, \"after\": \"True\"}, \"status\": {\"before\": null, \"after\": \"Active\"}}";
        migrationBuilder.Sql(
            "INSERT INTO kernel.user_account (id, external_subject, display_name, email, is_service_account, status, created_at, created_by, updated_at, updated_by) " +
            "VALUES ('" + systemId + "', '" + WellKnownUsers.SystemSubject + "', '" + WellKnownUsers.SystemDisplayName + "', NULL, true, 'Active', now(), '" + systemId + "', now(), '" + systemId + "') " +
            "ON CONFLICT (id) DO NOTHING;");
        migrationBuilder.Sql(
            "INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) " +
            "SELECT gen_random_uuid(), now(), '" + systemId + "', 'SYSTEM.MIGRATE', 'User', '" + systemId + "', '" + changes + "'::jsonb " +
            "WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '" + systemId + "');");

        migrationBuilder.AddForeignKey(
            name: "fk_audit_event_user",
            schema: "kernel",
            table: "audit_event",
            column: "user_id",
            principalSchema: "kernel",
            principalTable: "user_account",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_idempotency_record_user",
            schema: "kernel",
            table: "idempotency_record",
            column: "user_id",
            principalSchema: "kernel",
            principalTable: "user_account",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        // Explicit grants per table, declared once in the privilege matrix (I-14).
        foreach (string statement in KernelPrivilegeMatrix.GrantsFor([.. KernelPrivilegeMatrix.IdentityTables]))
        {
            migrationBuilder.Sql(statement);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_audit_event_user",
            schema: "kernel",
            table: "audit_event");

        migrationBuilder.DropForeignKey(
            name: "fk_idempotency_record_user",
            schema: "kernel",
            table: "idempotency_record");

        migrationBuilder.DropTable(
            name: "role_assignment",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "role_permission",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "sod_conflict",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "user_account",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "permission",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "role",
            schema: "kernel");

        migrationBuilder.DropIndex(
            name: "IX_audit_event_user_id",
            schema: "kernel",
            table: "audit_event");
    }
}
