using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 2 step 2b, read-only system catalogues (RR-64; I-14, R-SEC-02). The permission catalogue and the
/// segregation-of-duties pairs are written by Themis.Migrator only, as the table owner; the kernel role keeps SELECT and
/// loses INSERT and UPDATE, as document_type and document_transition already are. The SoD pair grant returns with the
/// role commands of milestone 2 step 13. Literal statements, fixed once merged (RR-39).
/// </summary>
internal sealed partial class ReadOnlySystemCatalogues : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("REVOKE INSERT, UPDATE ON \"kernel\".\"permission\" FROM \"themis_kernel\";");
        migrationBuilder.Sql("REVOKE INSERT, UPDATE ON \"kernel\".\"sod_conflict\" FROM \"themis_kernel\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("GRANT INSERT, UPDATE ON \"kernel\".\"sod_conflict\" TO \"themis_kernel\";");
        migrationBuilder.Sql("GRANT INSERT, UPDATE ON \"kernel\".\"permission\" TO \"themis_kernel\";");
    }
}
