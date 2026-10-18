-- I-02: no JournalLine has amount 0 and no InventoryEntry has quantity 0; transaction and company amounts of a
-- journal line share a sign (Specification, data quality). An InventoryEntry may have cost 0 (I-08).
-- The database refuses every such row (ck_journal_line_amount, ck_journal_line_txn_sign, ck_inventory_entry_quantity);
-- the query exists so that the nightly check and the tests read every invariant the same way.
SELECT 'kernel.journal_line' AS table_name,
       company_id,
       id,
       CASE WHEN amount = 0 THEN 'amount is 0' ELSE 'transaction and company amounts differ in sign' END AS violation
FROM kernel.journal_line
WHERE amount = 0
   OR sign(txn_amount) <> sign(amount)
UNION ALL
SELECT 'kernel.inventory_entry',
       company_id,
       id,
       'quantity is 0'
FROM kernel.inventory_entry
WHERE quantity_base = 0
ORDER BY 1, 2, 3;
