using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 2 step 2: the settings tables (Phase 0 Specification, Settings entities; D-58). kernel.setting_definition
/// is the catalogue Themis.Migrator writes from code on every run and the kernel role only reads; kernel.setting_value
/// is insert-only, one row per SET or CLEAR, with a check tying the company, site and warehouse to the level, composite
/// foreign keys keeping a site or warehouse inside its company (Conventions, Company scope; the warehouse already
/// carries the (id, company_id) key the document header's default warehouse needs) and the slot's version unique with
/// NULLS NOT DISTINCT (R-API-02). Grants are the ones declared in <see cref="Privileges.KernelPrivilegeMatrix"/>: SELECT
/// on the definitions, SELECT and INSERT on the values, no UPDATE or DELETE for any role (R-SET-01, I-14). Every
/// statement is literal text, fixed once merged (RR-39).
/// </summary>
internal sealed partial class SettingsTables : Migration
{
    private static readonly string[] IdCompanyIdColumns = ["id", "company_id"];
    private static readonly string[] SiteIdCompanyIdColumns = ["site_id", "company_id"];
    private static readonly string[] WarehouseIdCompanyIdColumns = ["warehouse_id", "company_id"];
    private static readonly string[] SlotVersionColumns = ["definition_key", "level", "company_id", "site_id", "warehouse_id", "version"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "setting_definition",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                value_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                default_value = table.Column<string>(type: "text", nullable: true),
                min = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                max = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                allowed_values = table.Column<string>(type: "text", nullable: true),
                levels = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                history_bearing = table.Column<bool>(type: "boolean", nullable: false),
                locked_reason = table.Column<string>(type: "text", nullable: true),
                backing = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                column_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                phase = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                planned_levels = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                rule_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                decision_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                description = table.Column<string>(type: "text", nullable: false),
                retired = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_setting_definition", x => x.id);
                table.UniqueConstraint("ak_setting_definition_key", x => x.key);
                table.CheckConstraint("ck_setting_definition_backing", "backing IN ('Setting', 'Column', 'Data', 'Planned')");
                table.CheckConstraint("ck_setting_definition_value_type", "value_type IN ('Integer', 'Decimal', 'Boolean', 'Code', 'Duration')");
            });

        migrationBuilder.CreateTable(
            name: "setting_value",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                definition_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: true),
                site_id = table.Column<Guid>(type: "uuid", nullable: true),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                value = table.Column<string>(type: "text", nullable: true),
                valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                command_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_setting_value", x => x.id);
                table.CheckConstraint("ck_setting_value_kind", "kind IN ('Set', 'Cleared')");
                table.CheckConstraint("ck_setting_value_level", "level IN ('Installation', 'Company', 'Site', 'Warehouse')");
                table.CheckConstraint("ck_setting_value_target", "(level = 'Installation' AND company_id IS NULL AND site_id IS NULL AND warehouse_id IS NULL) OR (level = 'Company' AND company_id IS NOT NULL AND site_id IS NULL AND warehouse_id IS NULL) OR (level = 'Site' AND company_id IS NOT NULL AND site_id IS NOT NULL AND warehouse_id IS NULL) OR (level = 'Warehouse' AND company_id IS NOT NULL AND site_id IS NULL AND warehouse_id IS NOT NULL)");
                table.CheckConstraint("ck_setting_value_value", "(kind = 'Set' AND value IS NOT NULL) OR (kind = 'Cleared' AND value IS NULL)");
                table.CheckConstraint("ck_setting_value_version", "version >= 1");
                table.ForeignKey(
                    name: "fk_setting_value_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_setting_value_definition",
                    column: x => x.definition_key,
                    principalSchema: "kernel",
                    principalTable: "setting_definition",
                    principalColumn: "key",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_setting_value_recorded_by",
                    column: x => x.recorded_by,
                    principalSchema: "kernel",
                    principalTable: "user_account",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_setting_value_site_company",
                    columns: x => new { x.site_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "site",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_setting_value_warehouse_company",
                    columns: x => new { x.warehouse_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "warehouse",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_setting_definition_key",
            schema: "kernel",
            table: "setting_definition",
            column: "key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_setting_value_company_id",
            schema: "kernel",
            table: "setting_value",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_setting_value_recorded_by",
            schema: "kernel",
            table: "setting_value",
            column: "recorded_by");

        migrationBuilder.CreateIndex(
            name: "IX_setting_value_site_id_company_id",
            schema: "kernel",
            table: "setting_value",
            columns: SiteIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_setting_value_warehouse_id_company_id",
            schema: "kernel",
            table: "setting_value",
            columns: WarehouseIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_setting_value_slot_version",
            schema: "kernel",
            table: "setting_value",
            columns: SlotVersionColumns,
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        // The grants declared in KernelPrivilegeMatrix for the tables this migration creates, as literal text (RR-39, I-14).
        migrationBuilder.Sql("GRANT SELECT ON \"kernel\".\"setting_definition\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT ON \"kernel\".\"setting_value\" TO \"themis_kernel\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "setting_value",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "setting_definition",
            schema: "kernel");
    }
}
