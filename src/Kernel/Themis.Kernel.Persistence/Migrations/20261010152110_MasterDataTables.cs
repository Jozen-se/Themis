using Microsoft.EntityFrameworkCore.Migrations;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Persistence.Privileges;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 1 step 5: the master data entities of the Phase 0 Specification (UnitOfMeasure, Currency,
/// ItemPostingGroup, Item, ItemCompany), the reference rows seeded in every installation with an audit event each
/// under the system user (Specification, Reference data; R-AUD-01), and the references by code added and validated
/// after the seed: Item.base_unit to UnitOfMeasure and Company.base_currency to Currency (the existing SE01 row
/// validates against SEK). Grants are the ones declared in <see cref="KernelPrivilegeMatrix"/> (I-14): SELECT,
/// INSERT and UPDATE for themis_kernel, never DELETE (R-MD-03).
/// </summary>
internal sealed partial class MasterDataTables : Migration
{
    private static readonly string[] IdCompanyIdColumns = ["id", "company_id"];
    private static readonly string[] DefaultWarehouseIdCompanyIdColumns = ["default_warehouse_id", "company_id"];
    private static readonly string[] ItemIdCompanyIdColumns = ["item_id", "company_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_warehouse_id_company_id",
            schema: "kernel",
            table: "warehouse",
            columns: IdCompanyIdColumns);

        migrationBuilder.CreateTable(
            name: "currency",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                minor_units = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_currency", x => x.id);
                table.UniqueConstraint("ak_currency_code", x => x.code);
                table.CheckConstraint("ck_currency_minor_units", "minor_units BETWEEN 0 AND 3");
            });

        migrationBuilder.CreateTable(
            name: "item_posting_group",
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
                table.PrimaryKey("pk_item_posting_group", x => x.id);
                table.UniqueConstraint("ak_item_posting_group_code", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "unit_of_measure",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                dimension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                decimals = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_unit_of_measure", x => x.id);
                table.UniqueConstraint("ak_unit_of_measure_code", x => x.code);
                table.CheckConstraint("ck_unit_of_measure_decimals", "decimals BETWEEN 0 AND 6");
                table.CheckConstraint("ck_unit_of_measure_dimension", "dimension IN ('Count', 'Mass', 'Length', 'Volume', 'Time')");
            });

        migrationBuilder.CreateTable(
            name: "item",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                item_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                item_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                base_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                lot_tracking = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                attribute_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                net_weight_kg = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                sales_multiple = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                min_order_qty = table.Column<decimal>(type: "numeric(20,6)", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_item", x => x.id);
                table.CheckConstraint("ck_item_item_type", "item_type IN ('Stocked', 'NonStocked', 'Service')");
                table.CheckConstraint("ck_item_lot_tracking", "lot_tracking IN ('None', 'Required')");
                table.CheckConstraint("ck_item_min_order_qty", "min_order_qty IS NULL OR min_order_qty > 0");
                table.CheckConstraint("ck_item_net_weight_kg", "net_weight_kg IS NULL OR net_weight_kg >= 0");
                table.CheckConstraint("ck_item_sales_multiple", "sales_multiple IS NULL OR sales_multiple > 0");
                table.ForeignKey(
                    name: "fk_item_base_unit",
                    column: x => x.base_unit,
                    principalSchema: "kernel",
                    principalTable: "unit_of_measure",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "item_company",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                item_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                item_posting_group = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                tax_class = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                costing_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                sales_allowed = table.Column<bool>(type: "boolean", nullable: false),
                purchase_allowed = table.Column<bool>(type: "boolean", nullable: false),
                default_warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                default_sales_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                default_purchase_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                extension = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_item_company", x => x.id);
                table.CheckConstraint("ck_item_company_costing_method", "costing_method IN ('MovingAverage')");
                table.CheckConstraint("ck_item_company_status", "status IN ('Active', 'Blocked', 'Inactive')");
                table.CheckConstraint("ck_item_company_tax_class", "tax_class IN ('Standard', 'Reduced', 'Zero', 'Exempt')");
                table.ForeignKey(
                    name: "fk_item_company_company",
                    column: x => x.company_id,
                    principalSchema: "kernel",
                    principalTable: "company",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_item_company_default_warehouse_company",
                    columns: x => new { x.default_warehouse_id, x.company_id },
                    principalSchema: "kernel",
                    principalTable: "warehouse",
                    principalColumns: IdCompanyIdColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_item_company_item",
                    column: x => x.item_id,
                    principalSchema: "kernel",
                    principalTable: "item",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_item_company_item_posting_group",
                    column: x => x.item_posting_group,
                    principalSchema: "kernel",
                    principalTable: "item_posting_group",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_company_base_currency",
            schema: "kernel",
            table: "company",
            column: "base_currency");

        migrationBuilder.CreateIndex(
            name: "IX_item_base_unit",
            schema: "kernel",
            table: "item",
            column: "base_unit");

        migrationBuilder.CreateIndex(
            name: "ux_item_item_number",
            schema: "kernel",
            table: "item",
            column: "item_number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_item_company_company_id",
            schema: "kernel",
            table: "item_company",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_item_company_default_warehouse_id_company_id",
            schema: "kernel",
            table: "item_company",
            columns: DefaultWarehouseIdCompanyIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_item_company_item_posting_group",
            schema: "kernel",
            table: "item_company",
            column: "item_posting_group");

        migrationBuilder.CreateIndex(
            name: "ux_item_company_item_company",
            schema: "kernel",
            table: "item_company",
            columns: ItemIdCompanyIdColumns,
            unique: true);

        // Reference data (Specification): seeded in every installation, each row with an audit event under the system
        // user (R-AUD-01), before any foreign key that points at these tables is added.
        string systemId = WellKnownUsers.System.Value.ToString();
        foreach ((string code, string name, string dimension, int decimals) in Units)
        {
            string id = ReferenceRowId("unit_of_measure", code);
            migrationBuilder.Sql(
                "INSERT INTO kernel.unit_of_measure (id, code, name, dimension, decimals, created_at, created_by, updated_at, updated_by) " +
                "VALUES ('" + id + "', '" + code + "', '" + name + "', '" + dimension + "', " + decimals.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", now(), '" + systemId + "', now(), '" + systemId + "') " +
                "ON CONFLICT (id) DO NOTHING;");
            migrationBuilder.Sql(AuditSql(systemId, "UnitOfMeasure", id,
                "{\"code\": {\"before\": null, \"after\": \"" + code + "\"}, \"name\": {\"before\": null, \"after\": \"" + name + "\"}, \"dimension\": {\"before\": null, \"after\": \"" + dimension + "\"}, \"decimals\": {\"before\": null, \"after\": \"" + decimals.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}}"));
        }

        foreach ((string code, string name, int minorUnits) in Currencies)
        {
            string id = ReferenceRowId("currency", code);
            migrationBuilder.Sql(
                "INSERT INTO kernel.currency (id, code, name, minor_units, created_at, created_by, updated_at, updated_by) " +
                "VALUES ('" + id + "', '" + code + "', '" + name + "', " + minorUnits.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", now(), '" + systemId + "', now(), '" + systemId + "') " +
                "ON CONFLICT (id) DO NOTHING;");
            migrationBuilder.Sql(AuditSql(systemId, "Currency", id,
                "{\"code\": {\"before\": null, \"after\": \"" + code + "\"}, \"name\": {\"before\": null, \"after\": \"" + name + "\"}, \"minorUnits\": {\"before\": null, \"after\": \"" + minorUnits.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}}"));
        }

        migrationBuilder.AddForeignKey(
            name: "fk_company_base_currency",
            schema: "kernel",
            table: "company",
            column: "base_currency",
            principalSchema: "kernel",
            principalTable: "currency",
            principalColumn: "code",
            onDelete: ReferentialAction.Restrict);

        // Explicit grants per table as literal text, fixed once merged (RR-39); the privilege matrix is the expected state the I-14 test checks.
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"unit_of_measure\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"currency\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"item_posting_group\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"item\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"kernel\".\"item_company\" TO \"themis_kernel\";");
    }

    /// <summary>The starter units of the Specification's Reference data paragraph: code, name, dimension, decimals.</summary>
    internal static IReadOnlyList<(string Code, string Name, string Dimension, int Decimals)> Units { get; } =
    [
        ("H87", "Piece", "Count", 0),
        ("KGM", "Kilogram", "Mass", 3),
        ("MTR", "Metre", "Length", 3),
    ];

    /// <summary>The starter currencies: code, name, minor units (SEK and EUR = 2, R-MON-02).</summary>
    internal static IReadOnlyList<(string Code, string Name, int MinorUnits)> Currencies { get; } =
    [
        ("SEK", "Swedish krona", 2),
        ("EUR", "Euro", 2),
    ];

    /// <summary>A fixed, name-derived id per reference row, so a re-run of the migration on a restored database is idempotent.</summary>
    internal static string ReferenceRowId(string table, string code)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(table + ":" + code));
        // Guid(byte[]) reads bytes 6 and 7 little-endian, so the version nibble lives in byte 7; bytes 8 to 15 are verbatim.
        hash[7] = (byte)((hash[7] & 0x0F) | 0x70);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    private static string AuditSql(string systemId, string entityType, string entityId, string changes) =>
        "INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) " +
        "SELECT gen_random_uuid(), now(), '" + systemId + "', 'SYSTEM.MIGRATE', '" + entityType + "', '" + entityId + "', '" + changes + "'::jsonb " +
        "WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '" + entityId + "');";

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_company_base_currency",
            schema: "kernel",
            table: "company");

        migrationBuilder.DropTable(
            name: "currency",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "item_company",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "item",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "item_posting_group",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "unit_of_measure",
            schema: "kernel");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_warehouse_id_company_id",
            schema: "kernel",
            table: "warehouse");

        migrationBuilder.DropIndex(
            name: "IX_company_base_currency",
            schema: "kernel",
            table: "company");
    }
}
