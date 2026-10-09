using Microsoft.EntityFrameworkCore.Migrations;
using Themis.Kernel.Persistence.Privileges;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// The first migration of the kernel data context: the idempotency, audit and outbox tables the command pipeline
/// writes in the command's transaction (R-LED-07, D-41). Grants are the ones declared in
/// <see cref="KernelPrivilegeMatrix"/>; the I-14 test proves the database carries exactly those.
/// </summary>
internal sealed partial class KernelPipelineTables : Migration
{
    private static readonly string[] AuditEntityIndexColumns = ["entity_type", "entity_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "kernel");

        migrationBuilder.CreateTable(
            name: "audit_event",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                command_name = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                changes = table.Column<string>(type: "jsonb", nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_audit_event", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "idempotency_record",
            schema: "kernel",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                command_name = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                response = table.Column<string>(type: "jsonb", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_idempotency_record", x => new { x.user_id, x.key });
            });

        migrationBuilder.CreateTable(
            name: "outbox_event",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_event", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_event_entity",
            schema: "kernel",
            table: "audit_event",
            columns: AuditEntityIndexColumns);

        migrationBuilder.CreateIndex(
            name: "ix_idempotency_record_expires_at",
            schema: "kernel",
            table: "idempotency_record",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_event_unpublished",
            schema: "kernel",
            table: "outbox_event",
            column: "occurred_at",
            filter: "published_at IS NULL");

        // Explicit grants per table, declared once in the privilege matrix (I-14). No default privileges exist
        // (brief, Database model), so a table without these statements is unreachable for themis_kernel.
        foreach (string statement in KernelPrivilegeMatrix.GrantsFor("idempotency_record", "audit_event", "outbox_event"))
        {
            migrationBuilder.Sql(statement);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_event",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "idempotency_record",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "outbox_event",
            schema: "kernel");
    }
}
