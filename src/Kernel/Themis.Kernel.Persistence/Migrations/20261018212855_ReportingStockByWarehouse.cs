using Microsoft.EntityFrameworkCore.Migrations;

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 1 step 10: the reporting view <c>reporting.stock_by_warehouse</c> (D-54, I-15), owned by themis_migrator
/// and running with its owner's rights; SELECT to themis_reporting and nothing else (I-14). No table changes. The view
/// SQL lives here as literal text (RR-39); docs/kpi/stock-value.md describes the definition.
/// </summary>
internal sealed partial class ReportingStockByWarehouse : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // D-54 in one query (the definition the KPI document describes), fixed here as literal text once merged (RR-39).
        migrationBuilder.Sql(
            """
            CREATE VIEW reporting.stock_by_warehouse AS
            WITH balance AS (
                SELECT b.company_id, b.item_id, b.warehouse_id,
                       sum(b.on_hand_base) AS on_hand_total,
                       coalesce(sum(b.on_hand_base) FILTER (WHERE b.stock_status = 'Available'), 0) AS on_hand_available,
                       coalesce(sum(b.on_hand_base) FILTER (WHERE b.stock_status = 'Blocked'), 0) AS on_hand_blocked,
                       sum(b.reserved_base) AS reserved
                FROM kernel.stock_balance b
                GROUP BY b.company_id, b.item_id, b.warehouse_id
                HAVING sum(b.on_hand_base) <> 0
            ),
            share AS (
                SELECT bal.company_id, bal.item_id, bal.warehouse_id,
                       bal.on_hand_total, bal.on_hand_available, bal.on_hand_blocked, bal.reserved,
                       s.inventory_value, s.average_cost,
                       trunc(s.inventory_value * bal.on_hand_total / NULLIF(s.quantity_base, 0), cur.minor_units) AS share_value,
                       row_number() OVER (PARTITION BY bal.company_id, bal.item_id ORDER BY bal.on_hand_total DESC, bal.warehouse_id) AS allocation_rank
                FROM balance bal
                JOIN kernel.item_cost_state s ON s.company_id = bal.company_id AND s.item_id = bal.item_id
                JOIN kernel.company c ON c.id = bal.company_id
                JOIN kernel.currency cur ON cur.code = c.base_currency
            ),
            allocated AS (
                SELECT sh.*,
                       sh.share_value + CASE WHEN sh.allocation_rank = 1 THEN sh.inventory_value - sum(sh.share_value) OVER (PARTITION BY sh.company_id, sh.item_id) ELSE 0 END AS stock_value
                FROM share sh
            )
            SELECT a.company_id,
                   c.code AS company_code,
                   a.item_id,
                   i.item_number,
                   i.description AS item_description,
                   a.warehouse_id,
                   w.code AS warehouse_code,
                   i.base_unit AS unit_code,
                   a.on_hand_total,
                   a.on_hand_available,
                   a.on_hand_blocked,
                   a.reserved,
                   a.stock_value,
                   c.base_currency AS currency_code,
                   a.average_cost AS item_average_cost
            FROM allocated a
            JOIN kernel.company c ON c.id = a.company_id
            JOIN kernel.item i ON i.id = a.item_id
            JOIN kernel.warehouse w ON w.id = a.warehouse_id
            """);
        migrationBuilder.Sql("COMMENT ON VIEW reporting.stock_by_warehouse IS 'Stock value KPI, docs/kpi/stock-value.md version 1 (D-54, I-15): current stock quantity and value per company, item and warehouse from the caches; value allocated by share of on hand, rounded down to the minor unit, remainder to the warehouse with the largest on hand.'");

        // Explicit grants per view as literal text, fixed once merged (RR-39); the privilege matrix is the expected state the I-14 test checks.
        migrationBuilder.Sql("GRANT SELECT ON \"reporting\".\"stock_by_warehouse\" TO \"themis_reporting\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS reporting.stock_by_warehouse");
    }
}
