using Microsoft.EntityFrameworkCore.Migrations;
using Themis.Kernel.Persistence.Privileges;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 1 step 7: the document framework of the Phase 0 Specification (D-39, D-40). The registry tables
/// document_type (with the numbering-only check) and document_transition, written by Themis.Migrator from the
/// catalogue in code and read-only for the kernel role; document, document_line and the INVENTORY_ADJUSTMENT
/// extension with their company scope enforced by composite foreign keys (the warehouse, the line's document, the
/// line's item through its ItemCompany row, D-43, for which the step 5 unique index becomes an alternate key);
/// number_series with the Specification's rules as constraints (fiscal year exactly when PerFiscalYear and of the
/// same company, one series per company, type and fiscal year with NULLS NOT DISTINCT, gapless where the registry
/// requires it, JOURNAL_ENTRY always gapless and PerFiscalYear). The outbox gains the event envelope of D-50
/// (aggregate type, company, command name, correlation id); the three NOT NULL columns have no default, so the
/// migration first checks that kernel.outbox_event is empty and stops with a message telling the owner to recreate
/// the database otherwise (D-50: development and test databases may be recreated). Grants are the ones declared in
/// <see cref="KernelPrivilegeMatrix"/> (I-14): never DELETE (R-DOC-06, R-MD-03).
/// </summary>
internal sealed partial class DocumentFrameworkTables : Migration
{
    /// <summary>The D-50 envelope columns are NOT NULL without a default: an outbox row written before this migration cannot be completed, so the owner recreates the database (step 7 brief).</summary>
    public const string OutboxGuardSql =
        "DO $$ BEGIN IF EXISTS (SELECT 1 FROM kernel.outbox_event) THEN " +
        "RAISE EXCEPTION 'kernel.outbox_event holds rows written before the D-50 event envelope. Recreate the database before running Themis.Migrator (step 7 brief).'; " +
        "END IF; END $$;";

    private static readonly string[] ItemIdCompanyIdColumns = ["item_id", "company_id"];
    private static readonly string[] IdCompanyIdColumns = ["id", "company_id"];
    private static readonly string[] CodeGaplessColumns = ["code", "gapless"];
    private static readonly string[] WarehouseIdCompanyIdColumns = ["warehouse_id", "company_id"];
    private static readonly string[] CompanyIdDocumentTypeStatusColumns = ["company_id", "document_type", "status"];
    private static readonly string[] CompanyIdDocumentTypeDocumentNumberColumns = ["company_id", "document_type", "document_number"];
    private static readonly string[] DocumentIdCompanyIdColumns = ["document_id", "company_id"];
    private static readonly string[] DocumentIdLineNumberColumns = ["document_id", "line_number"];
    private static readonly string[] DocumentTypeCommandFromStatusColumns = ["document_type", "command", "from_status"];
    private static readonly string[] DocumentTypeTypeRequiresGaplessColumns = ["document_type", "type_requires_gapless"];
    private static readonly string[] FiscalYearIdCompanyIdColumns = ["fiscal_year_id", "company_id"];
    private static readonly string[] CompanyIdDocumentTypeFiscalYearIdColumns = ["company_id", "document_type", "fiscal_year_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // D-50: the envelope columns below are NOT NULL without a default; an outbox row from before this migration stops it.
        migrationBuilder.Sql(OutboxGuardSql);

        migrationBuilder.DropIndex(
            name: "ux_item_company_item_company",
            schema: "kernel",
            table: "item_company");

        migrationBuilder.AddColumn<string>(
            name: "aggregate_type",
            schema: "kernel",
            table: "outbox_event",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false);

        migrationBuilder.AddColumn<string>(
            name: "command_name",
            schema: "kernel",
            table: "outbox_event",
            type: "character varying(81)",
            maxLength: 81,
            nullable: false);

        migrationBuilder.AddColumn<Guid>(
            name: "company_id",
            schema: "kernel",
            table: "outbox_event",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "correlation_id",
            schema: "kernel",
            table: "outbox_event",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false);

        migrationBuilder.AddUniqueConstraint(
            name: "ak_item_company_item_company",
            schema: "kernel",
            table: "item_company",
            columns: ItemIdCompanyIdColumns);

        migrationBuilder.CreateTable(
            name: "document_type",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                number_assigned = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                gapless = table.Column<bool>(type: "boolean", nullable: false),
                posts_inventory = table.Column<bool>(type: "boolean", nullable: false),
                posts_general_ledger = table.Column<bool>(type: "boolean", nullable: false),
                party_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_document_type", x => x.id);
                table.UniqueConstraint("ak_document_type_code", x => x.code);
                table.UniqueConstraint("ak_document_type_code_gapless", x => new { x.code, x.gapless });
                table.CheckConstraint("ck_document_type_number_assigned", "number_assigned IN ('OnCreate', 'OnPost', 'NumberingOnly')");
                table.CheckConstraint("ck_document_type_numbering_only", "number_assigned <> 'NumberingOnly' OR (posts_inventory = false AND posts_general_ledger = false AND party_role IS NULL)");
            });

        migrationBuilder.CreateTable(
            name: "document",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                draft_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                document_date = table.Column<DateOnly>(type: "date", nullable: false),
                posting_date = table.Column<DateOnly>(type: "date", nullable: true),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_document", x => x.id);
                table.UniqueConstraint("ak_document_id_company_id", x => new { x.id, x.company_id });
                table.CheckConstraint("ck_document_status", "status IN ('Draft', 'Posted', 'Discarded', 'Reversed')");
                table.ForeignKey(
                    name: "fk_document_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_document_currency",
                    column: x => x.currency,
                    principalSchema: "kernel",
                    principalTable: "currency",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_document_type",
                    column: x => x.document_type,
                    principalSchema: "kernel",
                    principalTable: "document_type",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_document_warehouse_company",
                    columns: x => new { x.warehouse_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "warehouse",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "document_transition",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                command = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                event_name = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_document_transition", x => x.id);
                table.CheckConstraint("ck_document_transition_from_status", "from_status IN ('Draft', 'Posted', 'Discarded', 'Reversed')");
                table.CheckConstraint("ck_document_transition_to_status", "to_status IN ('Draft', 'Posted', 'Discarded', 'Reversed')");
                table.ForeignKey(
                    name: "fk_document_transition_type",
                    column: x => x.document_type,
                    principalSchema: "kernel",
                    principalTable: "document_type",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "number_series",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                fiscal_year_id = table.Column<Guid>(type: "uuid", nullable: true),
                prefix = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                padding = table.Column<int>(type: "integer", nullable: false),
                next_number = table.Column<long>(type: "bigint", nullable: false),
                gapless = table.Column<bool>(type: "boolean", nullable: false),
                type_requires_gapless = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_number_series", x => x.id);
                table.CheckConstraint("ck_number_series_fiscal_year", "(scope = 'PerFiscalYear') = (fiscal_year_id IS NOT NULL)");
                table.CheckConstraint("ck_number_series_gapless", "type_requires_gapless = false OR gapless = true");
                table.CheckConstraint("ck_number_series_journal_entry", "document_type <> 'JOURNAL_ENTRY' OR (gapless = true AND scope = 'PerFiscalYear')");
                table.CheckConstraint("ck_number_series_next_number", "next_number >= 1");
                table.CheckConstraint("ck_number_series_padding", "padding BETWEEN 0 AND 20");
                table.CheckConstraint("ck_number_series_scope", "scope IN ('Continuous', 'PerFiscalYear')");
                table.ForeignKey(
                    name: "fk_number_series_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_number_series_fiscal_year_company",
                    columns: x => new { x.fiscal_year_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "fiscal_year",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_number_series_type_gapless",
                    columns: x => new { x.document_type, x.type_requires_gapless },
                    principalSchema: "kernel",
                    principalTable: "document_type",
                    principalColumns: CodeGaplessColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "document_line",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_id = table.Column<Guid>(type: "uuid", nullable: false),
                line_number = table.Column<int>(type: "integer", nullable: false),
                line_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                item_id = table.Column<Guid>(type: "uuid", nullable: true),
                description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                unit_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                quantity_base = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                stock_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                entered_unit_cost = table.Column<decimal>(type: "numeric(28,10)", nullable: true),
                unit_cost = table.Column<decimal>(type: "numeric(28,10)", nullable: true),
                cost_amount = table.Column<decimal>(type: "numeric(20,4)", nullable: true),
                line_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_document_line", x => x.id);
                table.UniqueConstraint("ak_document_line_id_company_id", x => new { x.id, x.company_id });
                table.CheckConstraint("ck_document_line_entered_unit_cost", "entered_unit_cost IS NULL OR entered_unit_cost >= 0");
                table.CheckConstraint("ck_document_line_item_line", "line_type <> 'Item' OR (item_id IS NOT NULL AND quantity IS NOT NULL AND unit_code IS NOT NULL AND quantity_base IS NOT NULL AND warehouse_id IS NOT NULL AND stock_status IS NOT NULL)");
                table.CheckConstraint("ck_document_line_line_number", "line_number > 0");
                table.CheckConstraint("ck_document_line_line_status", "line_status IN ('Open', 'Closed', 'Cancelled')");
                table.CheckConstraint("ck_document_line_line_type", "line_type IN ('Item', 'Charge', 'Account', 'Allocation', 'Text')");
                table.CheckConstraint("ck_document_line_quantity", "quantity IS NULL OR quantity <> 0");
                table.CheckConstraint("ck_document_line_stock_status", "stock_status IS NULL OR stock_status IN ('Available', 'Blocked')");
                table.ForeignKey(
                    name: "fk_document_line_document_company",
                    columns: x => new { x.document_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "document",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_document_line_item_company",
                    columns: x => new { x.item_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "item_company",
                    principalColumns: ItemIdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_document_line_unit",
                    column: x => x.unit_code,
                    principalSchema: "kernel",
                    principalTable: "unit_of_measure",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_document_line_warehouse_company",
                    columns: x => new { x.warehouse_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "warehouse",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inventory_adjustment",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                reason_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inventory_adjustment", x => x.id);
                table.CheckConstraint("ck_inventory_adjustment_reason_code", "reason_code IS NULL OR reason_code IN ('OpeningBalance', 'CountDifference', 'Scrap', 'Other')");
                table.ForeignKey(
                    name: "fk_inventory_adjustment_document_company",
                    columns: x => new { x.id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "document",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_event_company_id",
            schema: "kernel",
            table: "outbox_event",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_document_currency",
            schema: "kernel",
            table: "document",
            column: "currency");

        migrationBuilder.CreateIndex(
            name: "IX_document_document_type",
            schema: "kernel",
            table: "document",
            column: "document_type");

        migrationBuilder.CreateIndex(
            name: "IX_document_warehouse_id_company_id",
            schema: "kernel",
            table: "document",
            columns: WarehouseIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_document_company_type_status",
            schema: "kernel",
            table: "document",
            columns: CompanyIdDocumentTypeStatusColumns);

        migrationBuilder.CreateIndex(
            name: "ux_document_company_type_number",
            schema: "kernel",
            table: "document",
            columns: CompanyIdDocumentTypeDocumentNumberColumns,
            unique: true,
            filter: "document_number IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_document_line_document_id_company_id",
            schema: "kernel",
            table: "document_line",
            columns: DocumentIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_document_line_item_id_company_id",
            schema: "kernel",
            table: "document_line",
            columns: ItemIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_document_line_unit_code",
            schema: "kernel",
            table: "document_line",
            column: "unit_code");

        migrationBuilder.CreateIndex(
            name: "IX_document_line_warehouse_id_company_id",
            schema: "kernel",
            table: "document_line",
            columns: WarehouseIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_document_line_document_number",
            schema: "kernel",
            table: "document_line",
            columns: DocumentIdLineNumberColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_document_transition_type_command_from",
            schema: "kernel",
            table: "document_transition",
            columns: DocumentTypeCommandFromStatusColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_adjustment_id_company_id",
            schema: "kernel",
            table: "inventory_adjustment",
            columns: IdCompanyIdColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_number_series_document_type_type_requires_gapless",
            schema: "kernel",
            table: "number_series",
            columns: DocumentTypeTypeRequiresGaplessColumns);

        migrationBuilder.CreateIndex(
            name: "IX_number_series_fiscal_year_id_company_id",
            schema: "kernel",
            table: "number_series",
            columns: FiscalYearIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_number_series_company_type_year",
            schema: "kernel",
            table: "number_series",
            columns: CompanyIdDocumentTypeFiscalYearIdColumns,
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.AddForeignKey(
            name: "fk_outbox_event_company",
            schema: "kernel",
            table: "outbox_event",
            column: "company_id",
            principalSchema: "kernel",
            principalTable: "company",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        // Explicit grants per table as literal text, fixed once merged (RR-39); the privilege matrix is the expected state the I-14 test checks.
        migrationBuilder.Sql("GRANT SELECT ON \"kernel\".\"document_type\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT ON \"kernel\".\"document_transition\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"document\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"document_line\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"inventory_adjustment\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"number_series\" TO \"themis_kernel\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_outbox_event_company",
            schema: "kernel",
            table: "outbox_event");

        migrationBuilder.DropTable(
            name: "document_line",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "document_transition",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "inventory_adjustment",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "number_series",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "document",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "document_type",
            schema: "kernel");

        migrationBuilder.DropIndex(
            name: "IX_outbox_event_company_id",
            schema: "kernel",
            table: "outbox_event");

        migrationBuilder.DropUniqueConstraint(
            name: "ak_item_company_item_company",
            schema: "kernel",
            table: "item_company");

        migrationBuilder.DropColumn(
            name: "aggregate_type",
            schema: "kernel",
            table: "outbox_event");

        migrationBuilder.DropColumn(
            name: "command_name",
            schema: "kernel",
            table: "outbox_event");

        migrationBuilder.DropColumn(
            name: "company_id",
            schema: "kernel",
            table: "outbox_event");

        migrationBuilder.DropColumn(
            name: "correlation_id",
            schema: "kernel",
            table: "outbox_event");

        migrationBuilder.CreateIndex(
            name: "ux_item_company_item_company",
            schema: "kernel",
            table: "item_company",
            columns: ItemIdCompanyIdColumns,
            unique: true);
    }
}
