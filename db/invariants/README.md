# Invariant queries

One SQL file per checkable invariant of the Phase 0 Specification, named by its ID (`I-01.sql`, ...). Each query
returns the violating rows; an empty result means the invariant holds. The queries take no parameters, read every
company and run as the table owner `themis_migrator` (the kernel role sees the same tables; the reporting role does
not). They are written once (Build Brief, step 11) and read in two places:

- the tests: `tests/Themis.Integration.Tests/Invariants` runs every query after every command of the property-based
  runs and plants a violation of every part a constraint does not already refuse, to prove that the query reports it;
- the production nightly check, which is built before Phase 0 is declared complete and will run the same files.

| File | Invariant | Where the database already refuses the violation |
|---|---|---|
| `I-01.sql` | Every journal entry sums to 0 (R-LED-02) | |
| `I-02.sql` | No zero journal line or zero-quantity inventory entry; transaction and company amounts share a sign | `ck_journal_line_amount`, `ck_journal_line_txn_sign`, `ck_inventory_entry_quantity` |
| `I-03.sql` | Stock balance = sum of the entries per key, never below 0 (R-INV-01, R-INV-03, D-09) | `ck_stock_balance_on_hand` for the negative part (RR-47) |
| `I-04.sql` | Item cost state = sums of the entries per company and item; value 0 at quantity 0 (R-INV-05, R-INV-07) | `ck_item_cost_state_value` for the zero-value part |
| `I-06.sql` | INVENTORY-role balance = sum of the entry costs, per company, at every posting date (R-INV-06, R-ACC-01) | |
| `I-08.sql` | A costed entry links to a journal entry; a posted document has one journal entry exactly when a line has a cost (R-INV-06, R-LED-05) | `ck_inventory_entry_journal_entry` for the entry part |
| `I-09.sql` | Gapless series have no gaps or duplicates (R-DOC-04, R-DOC-07); JOURNAL_ENTRY series in milestone 1; a number not 1 to 18 digits after its prefix, or matching no gapless series of its company, is reported (RR-77) | `ux_journal_entry_company_number` for duplicates |
| `I-15.sql` | The reported warehouse values sum to the item cost state and the INVENTORY balance, none negative, none without stock (D-54) | `ck_stock_balance_on_hand` and `ck_item_cost_state_value` make a negative value impossible; the sum and the no-stock parts hold by the view's definition |

Not covered here: I-11 and I-14 are role and privilege statements, proven by the privilege matrix and role model tests
in `tests/Themis.Integration.Tests`; I-05, I-07, I-10, I-12 and I-13 need entities of later phases (reservations,
receivables, document links, open items, ROUNDING lines) and are listed with their phase in
`tests/Themis.Integration.Tests/Invariants/InvariantQueries.cs`, never faked as passing.
