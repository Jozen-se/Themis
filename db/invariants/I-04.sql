-- I-04: ItemCostState value and quantity = sums of InventoryEntry cost and quantity per company and item; the value is
-- 0 when the quantity is 0 (R-INV-05, R-INV-07). The database refuses a non-zero value at quantity 0
-- (ck_item_cost_state_value); the other parts are checked here. An item with entries but no cost state row is a
-- state of 0 that differs from its entries.
WITH entries AS (SELECT company_id, item_id, sum(quantity_base) AS quantity, sum(cost_amount) AS value
                 FROM kernel.inventory_entry
                 GROUP BY company_id, item_id)
SELECT s.company_id,
       s.item_id,
       s.quantity_base,
       s.inventory_value,
       coalesce(e.quantity, 0) AS entry_quantity,
       coalesce(e.value, 0) AS entry_value,
       CASE
           WHEN s.quantity_base = 0 AND s.inventory_value <> 0 THEN 'value not 0 at quantity 0'
           WHEN s.quantity_base <> coalesce(e.quantity, 0) THEN 'quantity differs from the entries'
           ELSE 'value differs from the entries' END AS violation
FROM kernel.item_cost_state s
         LEFT JOIN entries e ON e.company_id = s.company_id AND e.item_id = s.item_id
WHERE (s.quantity_base = 0 AND s.inventory_value <> 0)
   OR s.quantity_base <> coalesce(e.quantity, 0)
   OR s.inventory_value <> coalesce(e.value, 0)
UNION ALL
SELECT e.company_id,
       e.item_id,
       NULL,
       NULL,
       e.quantity,
       e.value,
       'entries without an item cost state row'
FROM entries e
WHERE NOT EXISTS (SELECT 1 FROM kernel.item_cost_state s WHERE s.company_id = e.company_id AND s.item_id = e.item_id)
ORDER BY 1, 2;
