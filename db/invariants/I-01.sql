-- I-01: every JournalEntry sums to 0 in company currency (R-LED-02).
-- Returns the journal entries whose lines do not sum to 0; no rows means the invariant holds.
SELECT e.company_id,
       e.id AS journal_entry_id,
       e.entry_number,
       e.posting_date,
       coalesce(sum(l.amount), 0) AS balance
FROM kernel.journal_entry e
         LEFT JOIN kernel.journal_line l ON l.journal_entry_id = e.id
GROUP BY e.company_id, e.id, e.entry_number, e.posting_date
HAVING coalesce(sum(l.amount), 0) <> 0
ORDER BY e.company_id, e.entry_number;
