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
}
