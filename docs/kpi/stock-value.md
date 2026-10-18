# KPI: Stock value

| Field | Value |
|---|---|
| KPI | Stock value, per company, item and warehouse |
| Version | 1 |
| Owner | Role **Finance owner**; current holder: Sebastian Hansson (project owner) |
| Decided | D-54 (2026-10-18); invariant I-15 |
| Served by | View `reporting.stock_by_warehouse` (schema `reporting`, read by `themis_reporting`); the view's comment names this document and version |
| Since | Milestone 1 step 10 |

The Foundation Design keeps KPI definitions versioned in the repository with a named owner, so that the API and BI
tools use the same definition. This is the first one. The version lives in this document, not in the view's name: a
change of meaning gets a new view name and a version 2 of this document; adding a column does not.

## Definition

**Stock value of an item in a company** is the item's `ItemCostState.inventory_value`: the sum of the cost amounts
of its inventory entries, kept by the posting service under the moving average cost method (R-INV-05, R-INV-07, I-04).
Per company it equals the balance of the INVENTORY-role accounts (I-06).

**Stock value of an item in a warehouse** is a reporting allocation of that company value (D-54), not a ledger fact:
average cost is kept per company and item, never per warehouse, so adding up a warehouse's own ledger costs can show
value at zero quantity or negative value. The allocation:

1. Take the warehouse's **total on hand** across stock statuses (Available and Blocked) and lots, `on_hand_total`,
   and the item's `ItemCostState.quantity_base`.
2. The warehouse's **exact share** is `inventory_value x on_hand_total / quantity_base`.
3. **Round each share down** (toward zero) to the company currency's minor unit (`currency.minor_units` of
   `company.base_currency`), whatever the company's rounding mode. Rounding down keeps every warehouse value at 0 or
   above; rounding half away from zero would not (four equal shares of 0.02 each round to 0.01 and leave -0.01 for
   the fourth). This is a reporting rounding point defined by D-54, outside the line, tax and total points of R-MON-02.
4. **Give the rounding difference** (`inventory_value` minus the sum of the rounded shares) to the warehouse with the
   largest `on_hand_total`, ties broken by the lowest `warehouse_id`. That warehouse is above its exact share by less
   than one minor unit per other warehouse of the item; every other warehouse is at or below its exact share by less
   than one minor unit.

So the warehouse values of an item **sum exactly** to its `inventory_value`, none is negative, and a warehouse with no
stock on hand has no row (I-15). Non-negative values rely on stock never going below 0 per warehouse (R-INV-03);
negative stock per warehouse would need a new version of this definition.

## Source

| Column | Source |
|---|---|
| `on_hand_total`, `on_hand_available`, `on_hand_blocked`, `reserved` | `kernel.stock_balance`, summed over stock statuses and lots per company, item and warehouse; `on_hand_available` is the Available stock status, `on_hand_blocked` the Blocked one, `reserved` the sum of `reserved_base` (0 in milestone 1). Available minus reservations (R-INV-04) will be a separate column, so no column changes meaning. |
| `stock_value` | The allocation above from `kernel.item_cost_state.inventory_value` |
| `item_average_cost` | `kernel.item_cost_state.average_cost`, the item's company-level figure, the same on every warehouse row of the item (informational, R-INV-05) |
| `currency_code`, `unit_code` | `company.base_currency`, `item.base_unit` |
| Identifiers | `company_id`, `company_code`, `item_id`, `item_number`, `item_description`, `warehouse_id`, `warehouse_code` |

The view reads the caches only, never the journal lines or inventory entries; the caches are written in the posting
transaction and kept equal to the entries by I-03 and I-04. Money and quantities are `numeric` (R-MON-01). Rows with
`on_hand_total` 0 are left out. The view shows every company of the installation until row-level security is added.

## Verification

Integration tests (`StockByWarehouseViewTests`) compare the view with `item_cost_state`, the inventory entries and
the INVENTORY-role journal lines after real postings, and prove the rounding rule, the remainder rule, the exact sum,
the non-negative values and the dropped zero rows (I-04, I-06, I-15), the role model (I-14) and the declared column
list.

## Change log

| Version | Date | Change |
|---|---|---|
| 1 | 2026-10-19 | First definition (D-54, I-15), milestone 1 step 10 |
