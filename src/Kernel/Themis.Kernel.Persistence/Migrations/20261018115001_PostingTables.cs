using Microsoft.EntityFrameworkCore.Migrations;
using Themis.Kernel.Persistence.Privileges;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 1 step 8: the ledgers and caches of the Phase 0 Specification (JournalEntry, JournalLine,
/// InventoryEntry, ItemCostState, StockBalance). The entries are append-only without updated or version fields
/// (R-LED-01, R-LED-06); every ledger row stores its company, tied by composite foreign keys to its document, line,
/// item company row and warehouse (Company scope convention, D-43); lot_id exists on the entry and the balance
/// (part of the key, D-18) but a check keeps it NULL until Lot exists; amounts are signed debit positive and never 0
/// (D-15, I-02), a cost needs its journal entry (R-INV-06). audit_event gains command_id (D-51): nullable, with the
/// check that it is present added NOT VALID, so it binds every row written from now on and never touches the
/// events migrations and seeders wrote before; it stays NOT VALID. Grants are the ones declared in
/// <see cref="KernelPrivilegeMatrix"/> (I-14): INSERT and SELECT only on the entries (I-11), SELECT, INSERT and
/// UPDATE on the caches, never DELETE.
/// </summary>
internal sealed partial class PostingTables : Migration
{
    private static readonly string[] IdCompanyIdColumns = ["id", "company_id"];
    private static readonly string[] ItemIdCompanyIdColumns = ["item_id", "company_id"];
    private static readonly string[] JournalEntryIdCompanyIdColumns = ["journal_entry_id", "company_id"];
    private static readonly string[] SourceLineIdCompanyIdColumns = ["source_line_id", "company_id"];
    private static readonly string[] WarehouseIdCompanyIdColumns = ["warehouse_id", "company_id"];
    private static readonly string[] CompanyIdItemIdWarehouseIdStockStatusColumns = ["company_id", "item_id", "warehouse_id", "stock_status"];
    private static readonly string[] CompanyIdItemIdColumns = ["company_id", "item_id"];
    private static readonly string[] SourceDocumentIdCompanyIdColumns = ["source_document_id", "company_id"];
    private static readonly string[] CompanyIdPostingDateColumns = ["company_id", "posting_date"];
    private static readonly string[] CompanyIdEntryNumberColumns = ["company_id", "entry_number"];
    private static readonly string[] JournalEntryIdLineNumberColumns = ["journal_entry_id", "line_number"];
    private static readonly string[] CompanyIdItemIdWarehouseIdLotIdStockStatusColumns = ["company_id", "item_id", "warehouse_id", "lot_id", "stock_status"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "command_id",
            schema: "kernel",
            table: "audit_event",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "journal_entry",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                entry_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                fiscal_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                document_date = table.Column<DateOnly>(type: "date", nullable: false),
                source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                command_id = table.Column<Guid>(type: "uuid", nullable: false),
                description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                reversal_of_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_journal_entry", x => x.id);
                table.UniqueConstraint("ak_journal_entry_id_company_id", x => new { x.id, x.company_id });
                table.ForeignKey(
                    name: "fk_journal_entry_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_entry_fiscal_period",
                    column: x => x.fiscal_period_id,
                    principalSchema: "kernel",
                    principalTable: "fiscal_period",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_entry_reversal_of",
                    column: x => x.reversal_of_entry_id,
                    principalSchema: "kernel",
                    principalTable: "journal_entry",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_entry_source_document_company",
                    columns: x => new { x.source_document_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "document",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inventory_entry",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                item_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                stock_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                movement_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                quantity_base = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                cost_amount = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(28,10)", nullable: false),
                posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                document_date = table.Column<DateOnly>(type: "date", nullable: false),
                source_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                reversal_of_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                command_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inventory_entry", x => x.id);
                table.CheckConstraint("ck_inventory_entry_cost_sign", "cost_amount = 0 OR sign(cost_amount) = sign(quantity_base)");
                table.CheckConstraint("ck_inventory_entry_journal_entry", "cost_amount = 0 OR journal_entry_id IS NOT NULL");
                table.CheckConstraint("ck_inventory_entry_lot_id", "lot_id IS NULL");
                table.CheckConstraint("ck_inventory_entry_movement_type", "movement_type IN ('AdjustmentIn', 'AdjustmentOut', 'Issue', 'ReturnIn', 'StatusOut', 'StatusIn')");
                table.CheckConstraint("ck_inventory_entry_quantity", "quantity_base <> 0");
                table.CheckConstraint("ck_inventory_entry_stock_status", "stock_status IN ('Available', 'Blocked')");
                table.ForeignKey(
                    name: "fk_inventory_entry_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_inventory_entry_item_company",
                    columns: x => new { x.item_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "item_company",
                    principalColumns: ItemIdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_inventory_entry_journal_entry_company",
                    columns: x => new { x.journal_entry_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "journal_entry",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_inventory_entry_reversal_of",
                    column: x => x.reversal_of_entry_id,
                    principalSchema: "kernel",
                    principalTable: "inventory_entry",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_inventory_entry_source_line_company",
                    columns: x => new { x.source_line_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "document_line",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_inventory_entry_warehouse_company",
                    columns: x => new { x.warehouse_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "warehouse",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "journal_line",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                line_number = table.Column<int>(type: "integer", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                account_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                posting_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                amount = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                txn_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                txn_amount = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                item_id = table.Column<Guid>(type: "uuid", nullable: true),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                source_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_journal_line", x => x.id);
                table.CheckConstraint("ck_journal_line_amount", "amount <> 0");
                table.CheckConstraint("ck_journal_line_line_number", "line_number > 0");
                table.CheckConstraint("ck_journal_line_txn_sign", "sign(txn_amount) = sign(amount)");
                table.ForeignKey(
                    name: "fk_journal_line_account",
                    column: x => x.account_id,
                    principalSchema: "kernel",
                    principalTable: "account",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_line_entry_company",
                    columns: x => new { x.journal_entry_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "journal_entry",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_line_item_company",
                    columns: x => new { x.item_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "item_company",
                    principalColumns: ItemIdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_line_posting_rule",
                    column: x => x.posting_rule_id,
                    principalSchema: "kernel",
                    principalTable: "posting_profile_rule",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_line_source_line_company",
                    columns: x => new { x.source_line_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "document_line",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_line_txn_currency",
                    column: x => x.txn_currency,
                    principalSchema: "kernel",
                    principalTable: "currency",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_journal_line_warehouse_company",
                    columns: x => new { x.warehouse_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "warehouse",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "item_cost_state",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                item_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity_base = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                inventory_value = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                average_cost = table.Column<decimal>(type: "numeric(28,10)", nullable: true),
                last_inventory_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_item_cost_state", x => x.id);
                table.CheckConstraint("ck_item_cost_state_quantity", "quantity_base >= 0");
                table.CheckConstraint("ck_item_cost_state_value", "inventory_value >= 0 AND (quantity_base <> 0 OR inventory_value = 0)");
                table.ForeignKey(
                    name: "fk_item_cost_state_item_company",
                    columns: x => new { x.item_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "item_company",
                    principalColumns: ItemIdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_item_cost_state_last_entry",
                    column: x => x.last_inventory_entry_id,
                    principalSchema: "kernel",
                    principalTable: "inventory_entry",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_balance",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                item_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                stock_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                on_hand_base = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                reserved_base = table.Column<decimal>(type: "numeric(20,6)", nullable: false),
                last_inventory_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_balance", x => x.id);
                table.CheckConstraint("ck_stock_balance_lot_id", "lot_id IS NULL");
                table.CheckConstraint("ck_stock_balance_reserved", "reserved_base >= 0");
                table.CheckConstraint("ck_stock_balance_stock_status", "stock_status IN ('Available', 'Blocked')");
                table.ForeignKey(
                    name: "fk_stock_balance_item_company",
                    columns: x => new { x.item_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "item_company",
                    principalColumns: ItemIdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_balance_last_entry",
                    column: x => x.last_inventory_entry_id,
                    principalSchema: "kernel",
                    principalTable: "inventory_entry",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_balance_warehouse_company",
                    columns: x => new { x.warehouse_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "warehouse",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_inventory_entry_item_id_company_id",
            schema: "kernel",
            table: "inventory_entry",
            columns: ItemIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_entry_journal_entry_id_company_id",
            schema: "kernel",
            table: "inventory_entry",
            columns: JournalEntryIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_entry_reversal_of_entry_id",
            schema: "kernel",
            table: "inventory_entry",
            column: "reversal_of_entry_id");

        migrationBuilder.CreateIndex(
            name: "IX_inventory_entry_source_line_id_company_id",
            schema: "kernel",
            table: "inventory_entry",
            columns: SourceLineIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_entry_warehouse_id_company_id",
            schema: "kernel",
            table: "inventory_entry",
            columns: WarehouseIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_inventory_entry_balance_key",
            schema: "kernel",
            table: "inventory_entry",
            columns: CompanyIdItemIdWarehouseIdStockStatusColumns);

        migrationBuilder.CreateIndex(
            name: "ix_inventory_entry_journal_entry",
            schema: "kernel",
            table: "inventory_entry",
            column: "journal_entry_id");

        migrationBuilder.CreateIndex(
            name: "IX_item_cost_state_item_id_company_id",
            schema: "kernel",
            table: "item_cost_state",
            columns: ItemIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_item_cost_state_last_inventory_entry_id",
            schema: "kernel",
            table: "item_cost_state",
            column: "last_inventory_entry_id");

        migrationBuilder.CreateIndex(
            name: "ux_item_cost_state_company_item",
            schema: "kernel",
            table: "item_cost_state",
            columns: CompanyIdItemIdColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_journal_entry_fiscal_period_id",
            schema: "kernel",
            table: "journal_entry",
            column: "fiscal_period_id");

        migrationBuilder.CreateIndex(
            name: "IX_journal_entry_reversal_of_entry_id",
            schema: "kernel",
            table: "journal_entry",
            column: "reversal_of_entry_id");

        migrationBuilder.CreateIndex(
            name: "IX_journal_entry_source_document_id_company_id",
            schema: "kernel",
            table: "journal_entry",
            columns: SourceDocumentIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_journal_entry_company_posting_date",
            schema: "kernel",
            table: "journal_entry",
            columns: CompanyIdPostingDateColumns);

        migrationBuilder.CreateIndex(
            name: "ux_journal_entry_company_number",
            schema: "kernel",
            table: "journal_entry",
            columns: CompanyIdEntryNumberColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_journal_line_item_id_company_id",
            schema: "kernel",
            table: "journal_line",
            columns: ItemIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_journal_line_journal_entry_id_company_id",
            schema: "kernel",
            table: "journal_line",
            columns: JournalEntryIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_journal_line_posting_rule_id",
            schema: "kernel",
            table: "journal_line",
            column: "posting_rule_id");

        migrationBuilder.CreateIndex(
            name: "IX_journal_line_source_line_id_company_id",
            schema: "kernel",
            table: "journal_line",
            columns: SourceLineIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_journal_line_txn_currency",
            schema: "kernel",
            table: "journal_line",
            column: "txn_currency");

        migrationBuilder.CreateIndex(
            name: "IX_journal_line_warehouse_id_company_id",
            schema: "kernel",
            table: "journal_line",
            columns: WarehouseIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_journal_line_account",
            schema: "kernel",
            table: "journal_line",
            column: "account_id");

        migrationBuilder.CreateIndex(
            name: "ux_journal_line_entry_number",
            schema: "kernel",
            table: "journal_line",
            columns: JournalEntryIdLineNumberColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_stock_balance_item_id_company_id",
            schema: "kernel",
            table: "stock_balance",
            columns: ItemIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_stock_balance_last_inventory_entry_id",
            schema: "kernel",
            table: "stock_balance",
            column: "last_inventory_entry_id");

        migrationBuilder.CreateIndex(
            name: "IX_stock_balance_warehouse_id_company_id",
            schema: "kernel",
            table: "stock_balance",
            columns: WarehouseIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_stock_balance_key",
            schema: "kernel",
            table: "stock_balance",
            columns: CompanyIdItemIdWarehouseIdLotIdStockStatusColumns,
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        // D-51: required from now on, untouched before; stays NOT VALID (R-AUD-01).
        migrationBuilder.Sql(LedgerModel.AuditCommandIdCheckSql);

        // Explicit grants per table, declared once in the privilege matrix (I-14).
        foreach (string statement in KernelPrivilegeMatrix.GrantsFor([.. KernelPrivilegeMatrix.LedgerTables]))
        {
            migrationBuilder.Sql(statement);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE kernel.audit_event DROP CONSTRAINT ck_audit_event_command_id;");

        migrationBuilder.DropTable(
            name: "item_cost_state",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "journal_line",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "stock_balance",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "inventory_entry",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "journal_entry",
            schema: "kernel");

        migrationBuilder.DropColumn(
            name: "command_id",
            schema: "kernel",
            table: "audit_event");
    }
}
