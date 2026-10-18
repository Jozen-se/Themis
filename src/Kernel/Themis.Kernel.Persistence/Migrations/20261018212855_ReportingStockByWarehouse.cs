using Microsoft.EntityFrameworkCore.Migrations;
using Themis.Kernel.Persistence.Privileges;

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 1 step 10: the reporting view <c>reporting.stock_by_warehouse</c> (D-54, I-15), owned by themis_migrator
/// and running with its owner's rights; SELECT to themis_reporting and nothing else (I-14). No table changes.
/// </summary>
internal sealed partial class ReportingStockByWarehouse : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ReportingModel.StockByWarehouseViewSql);
        migrationBuilder.Sql(ReportingModel.StockByWarehouseCommentSql);

        // Explicit grants per view, declared once in the privilege matrix (I-14).
        foreach (string statement in KernelPrivilegeMatrix.GrantsFor([.. KernelPrivilegeMatrix.ReportingViews]))
        {
            migrationBuilder.Sql(statement);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ReportingModel.DropStockByWarehouseSql);
    }
}
