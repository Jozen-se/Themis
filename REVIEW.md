# Themis review checklist

Every pull request is reviewed against this checklist and the released step brief, on its head commit (D-59).
Any failure of items 1 to 8 and 14 to 16 is Important.

1. Money, quantity, cost and rates: C# decimal, PostgreSQL numeric, JSON strings; never double or float (R-MON-01, D-37).
2. Ledger and audit tables are insert-only; corrections are reversals (R-LED-01, R-LED-06).
3. Only the kernel posting service writes inventory and journal entries, in the command's single transaction (R-LED-05, R-LED-07).
4. Locks are taken in the R-LED-08 order.
5. Every command: permission TYPE.COMMAND, Idempotency-Key, If-Match on changes, audit events, outbox event in the same transaction.
6. One connection, one transaction, SET LOCAL ROLE, no statement-level retries (D-41, R-DB-01).
7. Merged migrations are unchanged (golden scripts, RR-39); every new table or view has explicit grants and a privilege matrix entry.
8. Rejections return problem details with ruleId and code (R-API-03).
9. Configurable defaults come from settings, never code (P-11, R-SET-01).
10. Modules reference only Kernel.Primitives, Kernel.Contracts and other modules' Contracts (D-26).
11. Rule IDs are cited where a rule is enforced and in the test that proves it; no rule is changed in code.
12. Every brief item has a test; no existing test is weakened, skipped or deleted.
13. New dependencies pass the licence scan (D-38); no secrets, no model identifiers.
14. The pull request body labels local test results as local, ticks CI only when the head commit's checks have passed, and lists every deviation from the brief and every change the brief does not ask for (RR-91).
15. From milestone 2 step 13: a posting command enforces four-eyes posting where it is on for the document type; the user who posts is neither the user who created the document nor the user who last changed it or one of its lines, else 422 under R-SEC-03 (D-62).
16. From milestone 2 step 2b: the BEFORE UPDATE, BEFORE DELETE and BEFORE TRUNCATE triggers stay on journal_entry, journal_line, inventory_entry, audit_event and setting_value; a migration that must change such rows disables and re-enables them inside its own script, visible in its golden script, and is marked for the owner's review (D-60).
