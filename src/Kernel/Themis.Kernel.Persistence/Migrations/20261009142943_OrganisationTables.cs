using Microsoft.EntityFrameworkCore.Migrations;
using Themis.Kernel.Persistence.Privileges;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 1 step 3: the organisation entities of the Phase 0 Specification (Installation, Party,
/// ChartOfAccounts, Company, Site, Warehouse, FiscalYear, FiscalPeriod) with the company scope convention enforced
/// by composite foreign keys. Grants are the ones declared in <see cref="KernelPrivilegeMatrix"/> (I-14): SELECT,
/// INSERT and UPDATE for themis_kernel, never DELETE (R-MD-03).
/// </summary>
internal sealed partial class OrganisationTables : Migration
{
    private static readonly string[] IdCompanyIdColumns = ["id", "company_id"];
    private static readonly string[] FiscalYearIdCompanyIdColumns = ["fiscal_year_id", "company_id"];
    private static readonly string[] CompanyIdStartDateColumns = ["company_id", "start_date"];
    private static readonly string[] FiscalYearIdPeriodNumberColumns = ["fiscal_year_id", "period_number"];
    private static readonly string[] CompanyIdCodeColumns = ["company_id", "code"];
    private static readonly string[] SiteIdCompanyIdColumns = ["site_id", "company_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "chart_of_accounts",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_chart_of_accounts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "installation",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                default_locale = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_installation", x => x.id);
                table.CheckConstraint("ck_installation_status", "status IN ('Active', 'Suspended')");
            });

        migrationBuilder.CreateTable(
            name: "party",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                party_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                party_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                organisation_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                vat_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                language = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                duplicate_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                anonymised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_party", x => x.id);
                table.CheckConstraint("ck_party_party_type", "party_type IN ('Organisation', 'Person')");
                table.CheckConstraint("ck_party_status", "status IN ('Active', 'Blocked', 'Inactive')");
            });

        migrationBuilder.CreateTable(
            name: "company",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                party_id = table.Column<Guid>(type: "uuid", nullable: false),
                legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                organisation_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                vat_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                fiscal_year_start_month = table.Column<int>(type: "integer", nullable: false),
                chart_of_accounts_id = table.Column<Guid>(type: "uuid", nullable: false),
                rounding_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                tax_rounding_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                payment_tolerance = table.Column<decimal>(type: "numeric(20,4)", nullable: true),
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
                table.PrimaryKey("pk_company", x => x.id);
                table.CheckConstraint("ck_company_fiscal_year_start_month", "fiscal_year_start_month BETWEEN 1 AND 12");
                table.CheckConstraint("ck_company_rounding_mode", "rounding_mode IN ('HalfAwayFromZero', 'HalfEven')");
                table.CheckConstraint("ck_company_status", "status IN ('Active', 'Inactive')");
                table.CheckConstraint("ck_company_tax_rounding_level", "tax_rounding_level IN ('Line', 'Document')");
                table.ForeignKey(
                    name: "fk_company_chart_of_accounts",
                    column: x => x.chart_of_accounts_id,
                    principalSchema: "kernel",
                    principalTable: "chart_of_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_company_party",
                    column: x => x.party_id,
                    principalSchema: "kernel",
                    principalTable: "party",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "fiscal_year",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                start_date = table.Column<DateOnly>(type: "date", nullable: false),
                end_date = table.Column<DateOnly>(type: "date", nullable: false),
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
                table.PrimaryKey("pk_fiscal_year", x => x.id);
                table.UniqueConstraint("ak_fiscal_year_id_company_id", x => new { x.id, x.company_id });
                table.CheckConstraint("ck_fiscal_year_dates", "start_date <= end_date");
                table.CheckConstraint("ck_fiscal_year_status", "status IN ('Open', 'Closed')");
                table.ForeignKey(
                    name: "fk_fiscal_year_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "site",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                address_id = table.Column<Guid>(type: "uuid", nullable: true),
                time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_site", x => x.id);
                table.UniqueConstraint("ak_site_id_company_id", x => new { x.id, x.company_id });
                table.ForeignKey(
                    name: "fk_site_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "fiscal_period",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                fiscal_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                period_number = table.Column<int>(type: "integer", nullable: false),
                start_date = table.Column<DateOnly>(type: "date", nullable: false),
                end_date = table.Column<DateOnly>(type: "date", nullable: false),
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
                table.PrimaryKey("pk_fiscal_period", x => x.id);
                table.CheckConstraint("ck_fiscal_period_dates", "start_date <= end_date");
                table.CheckConstraint("ck_fiscal_period_number", "period_number BETWEEN 1 AND 18");
                table.CheckConstraint("ck_fiscal_period_status", "status IN ('Open', 'SoftClosed', 'Closed')");
                table.ForeignKey(
                    name: "fk_fiscal_period_year_company",
                    columns: x => new { x.fiscal_year_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "fiscal_year",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "warehouse",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                site_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                is_default = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("pk_warehouse", x => x.id);
                table.CheckConstraint("ck_warehouse_status", "status IN ('Active', 'Blocked', 'Inactive')");
                table.ForeignKey(
                    name: "fk_warehouse_site_company",
                    columns: x => new { x.site_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "site",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_chart_of_accounts_code",
            schema: "kernel",
            table: "chart_of_accounts",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_company_chart_of_accounts_id",
            schema: "kernel",
            table: "company",
            column: "chart_of_accounts_id");

        migrationBuilder.CreateIndex(
            name: "IX_company_party_id",
            schema: "kernel",
            table: "company",
            column: "party_id");

        migrationBuilder.CreateIndex(
            name: "ux_company_code",
            schema: "kernel",
            table: "company",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_period_fiscal_year_id_company_id",
            schema: "kernel",
            table: "fiscal_period",
            columns: FiscalYearIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_fiscal_period_company_start",
            schema: "kernel",
            table: "fiscal_period",
            columns: CompanyIdStartDateColumns);

        migrationBuilder.CreateIndex(
            name: "ux_fiscal_period_year_number",
            schema: "kernel",
            table: "fiscal_period",
            columns: FiscalYearIdPeriodNumberColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_fiscal_year_company_code",
            schema: "kernel",
            table: "fiscal_year",
            columns: CompanyIdCodeColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_installation_code",
            schema: "kernel",
            table: "installation",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_party_duplicate_key",
            schema: "kernel",
            table: "party",
            column: "duplicate_key");

        migrationBuilder.CreateIndex(
            name: "ux_party_party_number",
            schema: "kernel",
            table: "party",
            column: "party_number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_site_company_code",
            schema: "kernel",
            table: "site",
            columns: CompanyIdCodeColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_warehouse_site_id_company_id",
            schema: "kernel",
            table: "warehouse",
            columns: SiteIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "ux_warehouse_company_code",
            schema: "kernel",
            table: "warehouse",
            columns: CompanyIdCodeColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_warehouse_site_default",
            schema: "kernel",
            table: "warehouse",
            column: "site_id",
            unique: true,
            filter: "is_default");

        // Explicit grants per table as literal text, fixed once merged (RR-39); the privilege matrix is the expected state the I-14 test checks.
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"installation\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"party\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"chart_of_accounts\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"company\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"site\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"warehouse\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"fiscal_year\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"fiscal_period\" TO \"themis_kernel\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "fiscal_period",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "installation",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "warehouse",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "fiscal_year",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "site",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "company",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "chart_of_accounts",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "party",
            schema: "kernel");
    }
}
