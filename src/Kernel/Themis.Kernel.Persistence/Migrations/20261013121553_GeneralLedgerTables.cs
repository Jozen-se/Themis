using Microsoft.EntityFrameworkCore.Migrations;
using Themis.Kernel.Persistence.Privileges;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 1 step 6: the general ledger core of the Phase 0 Specification (Account, PostingProfileRule). The
/// account's number is unique per chart (R-MD-05) and its (id, is_control) pair is an alternate key; the rule's
/// constraints come from the account role catalogue (the role list, R-PP-06 as ck_posting_profile_rule_keys_by_role,
/// and the stored expects_control column whose composite foreign key to the account enforces R-PP-04), plus the enum
/// checks of the fixed keys and the item posting group reference by code. The no-overlap exclusion constraint
/// (R-MD-02, empty keys compared as a value) needs btree_gist, installed by the bootstrap script. Grants are the
/// ones declared in <see cref="KernelPrivilegeMatrix"/> (I-14): SELECT, INSERT and UPDATE for themis_kernel, never
/// DELETE (R-MD-03). Accounts and rules are fixture data, never seeded here (step 6 brief).
/// </summary>
internal sealed partial class GeneralLedgerTables : Migration
{
    private static readonly string[] IdIsControlColumns = ["id", "is_control"];
    private static readonly string[] ChartIdAccountNumberColumns = ["chart_id", "account_number"];
    private static readonly string[] AccountIdExpectsControlColumns = ["account_id", "expects_control"];
    private static readonly string[] CompanyIdAccountRoleColumns = ["company_id", "account_role"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "account",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                chart_id = table.Column<Guid>(type: "uuid", nullable: false),
                account_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                is_control = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("pk_account", x => x.id);
                table.UniqueConstraint("ak_account_id_is_control", x => new { x.id, x.is_control });
                table.CheckConstraint("ck_account_account_type", "account_type IN ('Asset', 'Liability', 'Equity', 'Revenue', 'Expense')");
                table.CheckConstraint("ck_account_status", "status IN ('Active', 'Blocked', 'Inactive')");
                table.ForeignKey(
                    name: "fk_account_chart",
                    column: x => x.chart_id,
                    principalSchema: "kernel",
                    principalTable: "chart_of_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "posting_profile_rule",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                account_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                item_posting_group = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                party_posting_group = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                ship_to_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                tax_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                reason_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                expects_control = table.Column<bool>(type: "boolean", nullable: false, computedColumnSql: "account_role IN ('INVENTORY', 'RECEIVABLES', 'OUTPUT_VAT')", stored: true),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_posting_profile_rule", x => x.id);
                table.CheckConstraint("ck_posting_profile_rule_account_role", "account_role IN ('INVENTORY', 'COGS', 'REVENUE', 'RECEIVABLES', 'OUTPUT_VAT', 'BANK', 'PAYMENT_DIFFERENCE', 'REALISED_FX', 'ROUNDING', 'INVENTORY_ADJUSTMENT', 'VAT_SETTLEMENT', 'OPENING_BALANCE')");
                table.CheckConstraint("ck_posting_profile_rule_keys_by_role", "(account_role = 'INVENTORY' AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'COGS' AND party_posting_group IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'REVENUE' AND party_posting_group IS NULL AND reason_code IS NULL) OR (account_role = 'RECEIVABLES' AND item_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'OUTPUT_VAT' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND reason_code IS NULL) OR (account_role = 'BANK' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'PAYMENT_DIFFERENCE' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'REALISED_FX' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'ROUNDING' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'INVENTORY_ADJUSTMENT' AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL) OR (account_role = 'VAT_SETTLEMENT' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'OPENING_BALANCE' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL)");
                table.CheckConstraint("ck_posting_profile_rule_party_posting_group", "party_posting_group IS NULL OR party_posting_group IN ('Domestic', 'EU', 'NonEU')");
                table.CheckConstraint("ck_posting_profile_rule_reason_code", "reason_code IS NULL OR reason_code IN ('OpeningBalance', 'CountDifference', 'Scrap', 'Other')");
                table.CheckConstraint("ck_posting_profile_rule_ship_to_scope", "ship_to_scope IS NULL OR ship_to_scope IN ('Domestic', 'EU', 'NonEU')");
                table.CheckConstraint("ck_posting_profile_rule_validity", "valid_to IS NULL OR valid_to > valid_from");
                table.ForeignKey(
                    name: "fk_posting_profile_rule_account_control",
                    columns: x => new { x.account_id, x.expects_control },
                    principalSchema: "kernel",
                    principalTable: "account",
                    principalColumns: IdIsControlColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_posting_profile_rule_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_posting_profile_rule_item_posting_group",
                    column: x => x.item_posting_group,
                    principalSchema: "kernel",
                    principalTable: "item_posting_group",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_account_chart_number",
            schema: "kernel",
            table: "account",
            columns: ChartIdAccountNumberColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_posting_profile_rule_account_id_expects_control",
            schema: "kernel",
            table: "posting_profile_rule",
            columns: AccountIdExpectsControlColumns);

        migrationBuilder.CreateIndex(
            name: "IX_posting_profile_rule_item_posting_group",
            schema: "kernel",
            table: "posting_profile_rule",
            column: "item_posting_group");

        migrationBuilder.CreateIndex(
            name: "ix_posting_profile_rule_company_role",
            schema: "kernel",
            table: "posting_profile_rule",
            columns: CompanyIdAccountRoleColumns);

        // R-MD-02: no two rules with the same role and the same key values overlap in time, empty keys compared as a value
        // (Specification, Effective dating). Needs btree_gist for the equality operators inside a GiST index.
        migrationBuilder.Sql("ALTER TABLE kernel.posting_profile_rule ADD CONSTRAINT ex_posting_profile_rule_no_overlap EXCLUDE USING gist (company_id WITH =, account_role WITH =, coalesce(item_posting_group, '') WITH =, coalesce(party_posting_group, '') WITH =, coalesce(ship_to_scope, '') WITH =, coalesce(tax_code_id, '00000000-0000-0000-0000-000000000000'::uuid) WITH =, coalesce(reason_code, '') WITH =, daterange(valid_from, valid_to, '[)') WITH &&);");

        // Explicit grants per table as literal text, fixed once merged (RR-39); the privilege matrix is the expected state the I-14 test checks.
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"account\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"posting_profile_rule\" TO \"themis_kernel\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "posting_profile_rule",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "account",
            schema: "kernel");
    }
}
