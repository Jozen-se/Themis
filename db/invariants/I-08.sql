-- I-08: every InventoryEntry with cost not 0 links to a JournalEntry; every posted document whose type posts to the
-- general ledger and that has at least one non-zero amount has one journal entry, and a document whose lines all have
-- zero cost writes none (R-INV-06, R-LED-05). The database refuses a costed entry without a journal entry
-- (ck_inventory_entry_journal_entry); the document part is checked here. Milestone 1 posts INVENTORY_ADJUSTMENT only;
-- REVERSE and the reversing entries come later, so the status checked is Posted.
SELECT e.company_id,
       e.id AS entity_id,
       'kernel.inventory_entry' AS table_name,
       'costed entry without a journal entry' AS violation
FROM kernel.inventory_entry e
WHERE e.cost_amount <> 0
  AND e.journal_entry_id IS NULL
UNION ALL
SELECT d.company_id,
       d.id,
       'kernel.document',
       CASE WHEN j.entries = 0 THEN 'posted document with a costed entry and no journal entry'
            WHEN c.costed = 0 THEN 'posted document without a costed entry and a journal entry'
            ELSE 'posted document with more than one journal entry' END
FROM kernel.document d
         JOIN kernel.document_type t ON t.code = d.document_type AND t.posts_general_ledger
         CROSS JOIN LATERAL (SELECT count(*) AS entries FROM kernel.journal_entry j WHERE j.source_document_id = d.id) j
         CROSS JOIN LATERAL (SELECT count(*) AS costed
                             FROM kernel.inventory_entry e
                                      JOIN kernel.document_line l ON l.id = e.source_line_id
                             WHERE l.document_id = d.id
                               AND e.cost_amount <> 0) c
WHERE d.status = 'Posted'
  AND j.entries <> CASE WHEN c.costed > 0 THEN 1 ELSE 0 END
ORDER BY 1, 2;
