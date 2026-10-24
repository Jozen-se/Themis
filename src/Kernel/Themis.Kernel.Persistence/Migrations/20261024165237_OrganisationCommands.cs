using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 2 step 3, organisation commands (D-64, D-65, R-MD-05, R-MD-06). number_series.company_id becomes optional, and the check
/// ck_number_series_installation_continuous keeps an installation-level series Continuous, as the Specification states. The
/// PARTY document type (numbering only, not gapless) is inserted here with a fixed id, because the migrator applies
/// migrations before it writes the registry, and the installation-level PARTY series is seeded with its documented default
/// (prefix P-, padding 6, next_number 1, not gapless); both rows get one audit event under the system user with one command
/// id (R-AUD-01, D-51). company gains organisation_number_normalised, filled for existing rows with the SQL equivalent of
/// the C# normalisation (uppercase letters and digits only) and written by the commands from then on, the unique key on
/// country and normalised number (R-MD-05), and the unique key on party_id (D-64: a party is the own party of at most one
/// company). The IX_company_party_id index the party foreign key carried becomes that unique index. No grant changes. The
/// Down leaves the two audit events, which the D-60 triggers keep. Every statement is literal text, fixed once merged (RR-39).
/// </summary>
internal sealed partial class OrganisationCommands : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_company_party_id",
            schema: "kernel",
            table: "company");

        migrationBuilder.AlterColumn<Guid>(
            name: "company_id",
            schema: "kernel",
            table: "number_series",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        // The column is added empty, filled for the rows that exist (the SQL twin of DuplicateKeys.Normalise: uppercase
        // letters and digits only), and then made required; the commands write it from here on (R-MD-05, D-64).
        migrationBuilder.AddColumn<string>(
            name: "organisation_number_normalised",
            schema: "kernel",
            table: "company",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.Sql("UPDATE kernel.company SET organisation_number_normalised = regexp_replace(upper(organisation_number), '[^[:alnum:]]', '', 'g');");

        migrationBuilder.AlterColumn<string>(
            name: "organisation_number_normalised",
            schema: "kernel",
            table: "company",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(50)",
            oldMaxLength: 50,
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_number_series_installation_continuous",
            schema: "kernel",
            table: "number_series",
            sql: "company_id IS NOT NULL OR scope = 'Continuous'");

        migrationBuilder.CreateIndex(
            name: "ux_company_country_organisation_number",
            schema: "kernel",
            table: "company",
            columns: CountryOrganisationNumberColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_company_party",
            schema: "kernel",
            table: "company",
            column: "party_id",
            unique: true);

        // D-64: the PARTY document type (Specification, DocumentType: numbering only, not gapless) and the installation-level
        // PARTY series with its documented default format, each with one audit event under the system user (R-AUD-01) and
        // the one command id of this migration (D-51, ck_audit_event_command_id). Fixed ids; a restored database that holds
        // the rows gets nothing twice.
        migrationBuilder.Sql(
            "INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, party_role, created_at, created_by, updated_at, updated_by) " +
            "SELECT '" + PartyTypeId + "', 'PARTY', 'NumberingOnly', false, false, false, NULL, now(), '" + SystemUserId + "', now(), '" + SystemUserId + "' " +
            "WHERE NOT EXISTS (SELECT 1 FROM kernel.document_type WHERE code = 'PARTY');");
        migrationBuilder.Sql(AuditSql("DocumentType", PartyTypeId,
            "{\"code\": {\"before\": null, \"after\": \"PARTY\"}, \"numberAssigned\": {\"before\": null, \"after\": \"NumberingOnly\"}, \"gapless\": {\"before\": null, \"after\": \"False\"}, \"postsInventory\": {\"before\": null, \"after\": \"False\"}, \"postsGeneralLedger\": {\"before\": null, \"after\": \"False\"}, \"partyRole\": {\"before\": null, \"after\": null}}"));
        migrationBuilder.Sql(
            "INSERT INTO kernel.number_series (id, company_id, document_type, scope, fiscal_year_id, prefix, padding, next_number, gapless, type_requires_gapless, created_at, created_by, updated_at, updated_by) " +
            "SELECT '" + PartySeriesId + "', NULL, 'PARTY', 'Continuous', NULL, 'P-', 6, 1, false, false, now(), '" + SystemUserId + "', now(), '" + SystemUserId + "' " +
            "WHERE NOT EXISTS (SELECT 1 FROM kernel.number_series WHERE company_id IS NULL AND document_type = 'PARTY');");
        migrationBuilder.Sql(AuditSql("NumberSeries", PartySeriesId,
            "{\"companyId\": {\"before\": null, \"after\": null}, \"documentType\": {\"before\": null, \"after\": \"PARTY\"}, \"scope\": {\"before\": null, \"after\": \"Continuous\"}, \"fiscalYearId\": {\"before\": null, \"after\": null}, \"prefix\": {\"before\": null, \"after\": \"P-\"}, \"padding\": {\"before\": null, \"after\": \"6\"}, \"nextNumber\": {\"before\": null, \"after\": \"1\"}, \"gapless\": {\"before\": null, \"after\": \"False\"}}"));
    }

    private static readonly string[] CountryOrganisationNumberColumns = ["country_code", "organisation_number_normalised"];

    /// <summary>The system user of every seeded row (WellKnownUsers.System), as literal text so the migration never reads code.</summary>
    private const string SystemUserId = "00000000-0000-7000-8000-000000000001";

    private const string CommandId = "00000000-0000-7000-8000-000000000a00";
    private const string PartyTypeId = "00000000-0000-7000-8000-000000000a01";
    private const string PartySeriesId = "00000000-0000-7000-8000-000000000a02";

    private static string AuditSql(string entityType, string entityId, string changes) =>
        "INSERT INTO kernel.audit_event (id, command_id, occurred_at, user_id, command_name, entity_type, entity_id, changes) " +
        "SELECT gen_random_uuid(), '" + CommandId + "', now(), '" + SystemUserId + "', 'SYSTEM.MIGRATE', '" + entityType + "', '" + entityId + "', '" + changes + "'::jsonb " +
        "WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '" + entityId + "');";

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM kernel.number_series WHERE id = '" + PartySeriesId + "';");
        migrationBuilder.Sql("DELETE FROM kernel.document_type WHERE id = '" + PartyTypeId + "';");

        migrationBuilder.DropCheckConstraint(
            name: "ck_number_series_installation_continuous",
            schema: "kernel",
            table: "number_series");

        migrationBuilder.DropIndex(
            name: "ux_company_country_organisation_number",
            schema: "kernel",
            table: "company");

        migrationBuilder.DropIndex(
            name: "ux_company_party",
            schema: "kernel",
            table: "company");

        migrationBuilder.DropColumn(
            name: "organisation_number_normalised",
            schema: "kernel",
            table: "company");

        migrationBuilder.AlterColumn<Guid>(
            name: "company_id",
            schema: "kernel",
            table: "number_series",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_company_party_id",
            schema: "kernel",
            table: "company",
            column: "party_id");
    }
}
