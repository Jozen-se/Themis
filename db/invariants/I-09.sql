-- I-09: gapless series have no gaps or duplicates (R-DOC-04, R-DOC-07). For every gapless number series the numbers
-- 1 to next_number - 1 each exist exactly once among the entries that took their number from it: journal entries of
-- the series' company whose number carries the series' prefix and whose fiscal period lies in the series' fiscal year.
-- A number at or above next_number was never handed out by the series. Milestone 1 has one gapless document type,
-- JOURNAL_ENTRY (D-52); a gapless series of another document type is reported until this query covers its documents,
-- so a new gapless type cannot pass unchecked. Duplicates are also refused by ux_journal_entry_company_number.
WITH series AS (SELECT s.id AS series_id, s.company_id, s.document_type, s.fiscal_year_id, coalesce(s.prefix, '') AS prefix, s.next_number
                FROM kernel.number_series s
                WHERE s.gapless),
     taken AS (SELECT s.series_id,
                      e.id AS journal_entry_id,
                      e.entry_number,
                      CASE WHEN substring(e.entry_number FROM length(s.prefix) + 1) ~ '^[0-9]+$'
                               THEN substring(e.entry_number FROM length(s.prefix) + 1)::bigint END AS number
               FROM series s
                        JOIN kernel.journal_entry e ON e.company_id = s.company_id AND left(e.entry_number, length(s.prefix)) = s.prefix
                        JOIN kernel.fiscal_period p ON p.id = e.fiscal_period_id
               WHERE s.document_type = 'JOURNAL_ENTRY'
                 AND (s.fiscal_year_id IS NULL OR p.fiscal_year_id = s.fiscal_year_id))
SELECT s.company_id,
       s.series_id,
       s.document_type,
       s.prefix,
       x.number,
       x.violation
FROM series s
         CROSS JOIN LATERAL (
    SELECT n.number, CASE WHEN count(t.journal_entry_id) = 0 THEN 'number missing (gap)' ELSE 'number taken more than once (duplicate)' END AS violation
    FROM generate_series(1, s.next_number - 1) AS n(number)
             LEFT JOIN taken t ON t.series_id = s.series_id AND t.number = n.number
    WHERE s.document_type = 'JOURNAL_ENTRY'
    GROUP BY n.number
    HAVING count(t.journal_entry_id) <> 1
    UNION ALL
    SELECT t.number, 'number at or above next_number, never handed out by the series'
    FROM taken t
    WHERE t.series_id = s.series_id
      AND t.number >= s.next_number
    UNION ALL
    SELECT NULL, 'gapless series of a document type this query does not cover'
    WHERE s.document_type <> 'JOURNAL_ENTRY') x
ORDER BY s.company_id, s.series_id, x.number;
