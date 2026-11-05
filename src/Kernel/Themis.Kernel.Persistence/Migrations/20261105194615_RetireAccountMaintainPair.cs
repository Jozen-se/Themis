using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 2 step 4, the retired conflict pair (D-69, R-SEC-02; step 4 brief, item 6). The pair ACCOUNT.MAINTAIN with
/// MANUAL_JOURNAL.POST is replaced in the catalogue by ACCOUNT.CREATE, ACCOUNT.UPDATE, ACCOUNT.UNBLOCK and
/// ACCOUNT.REACTIVATE, each with MANUAL_JOURNAL.POST, which Themis.Migrator seeds after the migrations; the catalogue
/// seeder never removes a row, so this migration deletes the old one. ACCOUNT.MAINTAIN was never a permission row, so
/// no role permission or assignment refers to it. The row's audit event is written first, from the row itself, under the
/// system user with command name SYSTEM.MIGRATE (so the shared test cleanup keeps it) and the one command id of this
/// migration (R-AUD-01, D-51); a database that no longer holds the row gets neither statement's effect. Deleting the row
/// drops data, which the owner approved with D-69 and reads under D-59. No schema or grant change. The Down restores the
/// pair with a new id and leaves the audit event, which the D-60 triggers keep. Literal statements, fixed once merged (RR-39).
/// </summary>
internal sealed partial class RetireAccountMaintainPair : Migration
{
    /// <summary>The system user of every seeded row (WellKnownUsers.System), as literal text so the migration never reads code.</summary>
    private const string SystemUserId = "00000000-0000-7000-8000-000000000001";
    private const string CommandId = "00000000-0000-7000-8000-000000000b00";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "INSERT INTO kernel.audit_event (id, command_id, occurred_at, user_id, command_name, entity_type, entity_id, changes) " +
            "SELECT gen_random_uuid(), '" + CommandId + "', now(), '" + SystemUserId + "', 'SYSTEM.MIGRATE', 'SodConflict', c.id, " +
            "jsonb_build_object('permissionA', jsonb_build_object('before', c.permission_a, 'after', NULL), 'permissionB', jsonb_build_object('before', c.permission_b, 'after', NULL)) " +
            "FROM kernel.sod_conflict c WHERE c.permission_a = 'ACCOUNT.MAINTAIN' AND c.permission_b = 'MANUAL_JOURNAL.POST';");
        migrationBuilder.Sql("DELETE FROM kernel.sod_conflict WHERE permission_a = 'ACCOUNT.MAINTAIN' AND permission_b = 'MANUAL_JOURNAL.POST';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "INSERT INTO kernel.sod_conflict (id, permission_a, permission_b, created_at, created_by, updated_at, updated_by) " +
            "SELECT gen_random_uuid(), 'ACCOUNT.MAINTAIN', 'MANUAL_JOURNAL.POST', now(), '" + SystemUserId + "', now(), '" + SystemUserId + "' " +
            "WHERE NOT EXISTS (SELECT 1 FROM kernel.sod_conflict WHERE permission_a = 'ACCOUNT.MAINTAIN' AND permission_b = 'MANUAL_JOURNAL.POST');");
    }
}
