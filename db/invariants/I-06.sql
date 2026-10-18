-- I-06: the general ledger balance of INVENTORY-role accounts = the sum of InventoryEntry cost, per company, at any
-- date (R-INV-06, R-ACC-01). Checked at every distinct posting date of the company on either side: the cumulative
-- balance of the journal lines posted under the INVENTORY role up to and including the date against the cumulative
-- cost of the inventory entries up to and including the date.
WITH per_date AS (SELECT e.company_id, e.posting_date, l.amount AS ledger_amount, 0::numeric AS cost
                  FROM kernel.journal_line l
                           JOIN kernel.journal_entry e ON e.id = l.journal_entry_id
                  WHERE l.account_role = 'INVENTORY'
                  UNION ALL
                  SELECT company_id, posting_date, 0, cost_amount
                  FROM kernel.inventory_entry),
     totals AS (SELECT company_id, posting_date, sum(ledger_amount) AS ledger_amount, sum(cost) AS cost
                FROM per_date
                GROUP BY company_id, posting_date),
     cumulative AS (SELECT company_id,
                           posting_date,
                           sum(ledger_amount) OVER (PARTITION BY company_id ORDER BY posting_date) AS ledger_balance,
                           sum(cost) OVER (PARTITION BY company_id ORDER BY posting_date) AS inventory_cost
                    FROM totals)
SELECT company_id, posting_date, ledger_balance, inventory_cost, ledger_balance - inventory_cost AS difference
FROM cumulative
WHERE ledger_balance <> inventory_cost
ORDER BY company_id, posting_date;
