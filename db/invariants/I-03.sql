-- I-03: StockBalance on hand = sum of InventoryEntry quantity per balance key (company, item, warehouse, lot, stock
-- status), and is never below 0 in a warehouse that does not allow negative stock (R-INV-01, R-INV-03). Every
-- warehouse refuses negative stock in v1 (D-09), so no balance may be negative. A key that has entries but no
-- balance row is a balance of 0 that differs from its entries.
WITH entries AS (SELECT company_id, item_id, warehouse_id, lot_id, stock_status, sum(quantity_base) AS quantity
                 FROM kernel.inventory_entry
                 GROUP BY company_id, item_id, warehouse_id, lot_id, stock_status)
SELECT b.company_id,
       b.item_id,
       b.warehouse_id,
       b.lot_id,
       b.stock_status,
       b.on_hand_base,
       coalesce(e.quantity, 0) AS entry_quantity,
       CASE WHEN b.on_hand_base < 0 THEN 'on hand below 0' ELSE 'on hand differs from the entries' END AS violation
FROM kernel.stock_balance b
         LEFT JOIN entries e ON e.company_id = b.company_id AND e.item_id = b.item_id AND e.warehouse_id = b.warehouse_id
    AND e.lot_id IS NOT DISTINCT FROM b.lot_id AND e.stock_status = b.stock_status
WHERE b.on_hand_base < 0
   OR b.on_hand_base <> coalesce(e.quantity, 0)
UNION ALL
SELECT e.company_id,
       e.item_id,
       e.warehouse_id,
       e.lot_id,
       e.stock_status,
       NULL,
       e.quantity,
       'entries without a stock balance row'
FROM entries e
WHERE NOT EXISTS (SELECT 1
                  FROM kernel.stock_balance b
                  WHERE b.company_id = e.company_id AND b.item_id = e.item_id AND b.warehouse_id = e.warehouse_id
                    AND b.lot_id IS NOT DISTINCT FROM e.lot_id AND b.stock_status = e.stock_status)
ORDER BY 1, 2, 3, 5;
