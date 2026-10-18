-- I-15: the reported stock values per warehouse of an item sum to its ItemCostState value, per company and item, and
-- so per company to the balance of the INVENTORY-role accounts; no reported warehouse value is negative, and a
-- warehouse with no stock on hand reports none (D-54, I-04, I-06). Read from reporting.stock_by_warehouse, the view
-- that serves the stock value KPI.
WITH per_item AS (SELECT company_id, item_id, sum(stock_value) AS reported_value, min(stock_value) AS least_value, count(*) AS warehouses
                  FROM reporting.stock_by_warehouse
                  GROUP BY company_id, item_id)
SELECT s.company_id,
       s.item_id,
       NULL::uuid AS warehouse_id,
       v.reported_value,
       s.inventory_value AS expected_value,
       CASE WHEN v.item_id IS NULL THEN 'item with stock and no reported row'
            WHEN v.least_value < 0 THEN 'negative reported warehouse value'
            ELSE 'reported values do not sum to the item cost state value' END AS violation
FROM kernel.item_cost_state s
         LEFT JOIN per_item v ON v.company_id = s.company_id AND v.item_id = s.item_id
WHERE (s.quantity_base <> 0 AND v.item_id IS NULL)
   OR v.least_value < 0
   OR (v.item_id IS NOT NULL AND v.reported_value <> s.inventory_value)
UNION ALL
SELECT v.company_id,
       v.item_id,
       NULL,
       v.reported_value,
       0,
       'reported rows for an item without a cost state'
FROM per_item v
WHERE NOT EXISTS (SELECT 1 FROM kernel.item_cost_state s WHERE s.company_id = v.company_id AND s.item_id = v.item_id)
UNION ALL
SELECT w.company_id,
       w.item_id,
       w.warehouse_id,
       w.stock_value,
       NULL,
       'reported row for a warehouse without stock on hand'
FROM reporting.stock_by_warehouse w
WHERE w.on_hand_total = 0
UNION ALL
SELECT c.id,
       NULL,
       NULL,
       r.reported,
       g.balance,
       'reported company total differs from the INVENTORY balance'
FROM kernel.company c
         CROSS JOIN LATERAL (SELECT coalesce(sum(stock_value), 0) AS reported FROM reporting.stock_by_warehouse w WHERE w.company_id = c.id) r
         CROSS JOIN LATERAL (SELECT coalesce(sum(l.amount), 0) AS balance FROM kernel.journal_line l WHERE l.company_id = c.id AND l.account_role = 'INVENTORY') g
WHERE r.reported <> g.balance
ORDER BY 1, 2, 3;
