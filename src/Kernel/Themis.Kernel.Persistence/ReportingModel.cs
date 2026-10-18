namespace Themis.Kernel.Persistence;

/// <summary>
/// The reporting schema (milestone 1 step 10): one view, <c>reporting.stock_by_warehouse</c>, that a BI tool logged
/// in as themis_reporting reads for current stock quantity and value per company, item and warehouse. The view is
/// owned by themis_migrator and runs with its owner's rights (Build Brief, Database roles), so themis_reporting
/// needs, and holds, no privilege on any kernel table. It reads the caches only: StockBalance for quantities and
/// ItemCostState for value, kept equal to the inventory entries by I-03 and I-04. Value per warehouse is the
/// allocation of D-54: the item's inventory value times the warehouse's share of the on hand quantity, each share
/// rounded down to the company currency's minor unit, the rounding difference to the warehouse with the largest on
/// hand, then the lowest warehouse id, so the warehouse values sum to the item value and none is negative (I-15).
/// Rows with no stock on hand are left out. The definition is the versioned KPI docs/kpi/stock-value.md.
/// </summary>
internal static class ReportingModel
{
    public const string Schema = "reporting";
    public const string StockByWarehouseView = "stock_by_warehouse";
    public const string KpiDefinition = "docs/kpi/stock-value.md";
    public const int KpiVersion = 1;

    /// <summary>
    /// D-54 in one query: the per-warehouse balances summed over stock statuses and lots (HAVING keeps stock on hand
    /// only), joined to the item's cost state and the company currency; the share truncated toward zero to the
    /// currency's minor units with NULLIF so no row can divide by zero; the remainder added to the first row in the
    /// order on_hand_total descending, warehouse_id ascending (R-MON-01: numeric throughout).
    /// </summary>
    public const string StockByWarehouseViewSql =
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
        """;

    public const string StockByWarehouseCommentSql =
        "COMMENT ON VIEW reporting.stock_by_warehouse IS 'Stock value KPI, docs/kpi/stock-value.md version 1 (D-54, I-15): current stock quantity and value per company, item and warehouse from the caches; value allocated by share of on hand, rounded down to the minor unit, remainder to the warehouse with the largest on hand.'";

    public const string DropStockByWarehouseSql = "DROP VIEW IF EXISTS reporting.stock_by_warehouse";
}
