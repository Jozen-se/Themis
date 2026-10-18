<!-- SNAPSHOT. Do not edit by hand. Regenerate from the live document. -->

> **Snapshot** of the live document *Themis Foundation Design* (https://claude.ai/artifact/B7yhaqHmFJqQCAo23KSDXq), revision 161, taken 2026-10-18.
> The live document is authoritative; this copy exists so that code review and offline reading can cite rule IDs
> without leaving the repository. Where this copy and the live document differ, the live document wins.
> Diagrams are not reproduced; their captions are kept in place.

# Themis Foundation Design

2026-10-08 · Sebastian Hansson

Themis (working brand: Jozen) starts as a coherent kernel (parties, items, documents, immutable ledgers) plus purchasing and sales for a distributor as the first business phase (D-30), built backend-first and API-first, with the frontend later. This doc holds the concepts and rulesets; Claude Code builds from it. Every rule carries an ID so code, tests and decisions can trace back to it.

## Scope and phases

Version 1 is Phases 0 to 2: the kernel with master data and inventory, then purchasing and sales, then finance through a first financial close (D-30). Backend and API only. Later phases reuse the kernel as it is; a phase that needs a kernel change records a decision first.

1. **Phase 0, Kernel, master data and inventory:** installation, companies, sites and warehouses; parties, items, units of measure and pricing; number series and the document framework; the kernel posting service with the inventory ledger and the general ledger core (accounts, journal, posting profile); users, roles and audit. Built walking skeleton first (D-27).
2. **Phase 1, Purchasing and Sales:** purchase order and goods receipt; sales order, stock allocation, delivery and goods issue, customer return.
3. **Phase 2, Finance through a first financial close:** customer invoices, credit notes and payments (Receivables); supplier invoices with three-way match and supplier payments (Payables); general ledger close.
4. **Phase 3, Warehousing:** bins and locations, receiving, put-away, picking, packing, stock counts, transfers between warehouses.
5. **Phase 4, Quality, Transportation and Manufacturing:** inspection and non-conformance, shipments and freight, bills of material, routings, production orders and planning.
6. **Phase 5, Frontend:** task-based UI on the stable API, starting once the Phase 1 API is stable.
7. **Later:** Integration Hub (D-23), sales process (leads, opportunities), controlling, fixed assets.

**Gate between phases:** a phase is done when its ledger invariant tests and end-to-end scenario tests pass, and its rules in this doc are marked Decided.

**Build order inside Phase 0 (D-27):** a walking skeleton first, meaning Items, an inventory adjustment, its general ledger posting, one invariant test and one event, running end to end with CI. The rest of Phase 0 follows only once that runs. This keeps the kernel from growing before anything works.

**Not in v1:** payroll and HR, CRM and marketing, web shop, EDI, fixed assets, consolidation, foreign-exchange revaluation, country-specific statutory reporting beyond basic VAT. Currency is modelled from day one (R-MON-01) so that later multi-currency work does not need a data model change.

## Design principles

Eleven principles decide every design conflict; when two collide, the lower number wins.

| ID | Principle | What it means | Why it matters |
|---|---|---|---|
| P-01 | Ledgers are the truth | Stock and money change only through append-only ledger entries. Balances are derived, never edited. | Full audit trail; ends "stock is wrong and nobody knows why". |
| P-02 | One core, many processes | Every module uses the same Party, Item, Document and Ledger model. No module keeps its own copy. Kernel guard (D-46): a business concept or master data entity enters the kernel only if a ledger references it, or if at least two module specifications name it as a need. Kernel infrastructure (identity, idempotency, audit, outbox, settings) is outside this test. Every kernel entity records its justification in the Phase 0 specification (the ledger, or the two modules); a pull request that adds a kernel entity cites that justification, and the reviewer rejects it without one. D-46 defines ledger and provisional module justification. | Removes the reconciliation work that bolted-on modules create. |
| P-03 | Documents carry intent, ledgers carry effect | An order says what should happen; a posting records what did happen. | Clean separation; reversal is simple and traceable. |
| P-04 | Commands, not table edits | Every change is a named business command, for example `ReleaseSalesOrder`, with its own validation. No generic edit of transactional data. | Business rules live in one place; the API mirrors the process. |
| P-05 | Explicit and deterministic | Rules are written down with IDs and tested. The same input always gives the same result. | Explainable behaviour; safe AI-assisted building. |
| P-06 | Secure by default | Least privilege, every write attributed to a user and a command, personal data classified. | Trust, audit, GDPR. |
| P-07 | Configuration over customization | Behaviour varies through settings and defined extension points. Core code and core tables are never changed per customer. | Upgrades stay possible; this is where most large ERPs fail. |
| P-08 | Designed for reporting | Every ledger entry carries company, dates, party, item and source document. A semantic layer is part of the product. | BI without reverse-engineering the database. |
| P-09 | Process-first | APIs, and later screens, are shaped around jobs (allocate, pick, invoice), not around tables. | Adoption and fewer user errors. |
| P-10 | Boring technology | Proven, widely known components over novel ones. | Contributors, maintenance, longevity. |
| P-11 | Agile by design | Every rule is either an invariant, fixed because correctness, law or security depends on it, or a default: the best-practice behaviour shipped switched on, which a company can adjust through audited configuration. Not rigid, not unbounded. | Fits real companies without customisation, while the core promises hold (D-29). |

## Architecture at a glance

Themis is one deployable backend with strict module boundaries. A request becomes a named command, and the command writes its document, ledger entries, audit event and outbound events in a single database transaction.

*[Diagram in the live document: write path and read path · one modular monolith]*

Reporting and integrations only ever read from the reporting schema and the outbox, never from the transactional tables.

## Technology direction

PostgreSQL (D-04), the modular monolith (D-05) and C# on .NET (D-06) are decided. Rules and specifications stay language-neutral; only the build constraints below are specific to the chosen stack.

| Area | Direction | Status | Reasoning |
|---|---|---|---|
| Database | PostgreSQL | Decided (D-04) | ACID transactions, strong constraints, exact `numeric` type, row-level security, `jsonb` for extension fields, open licence, broad BI connectivity. |
| Architecture style | Modular monolith: one deployable, strict module boundaries | Decided (D-05) | Microservices would add distributed transactions to the hardest problem (ledger consistency) with no gain at this size. Modules can be split later along the same boundaries. |
| Backend language | C# on the current .NET LTS release (.NET 10, verify at scaffold time); if it is ever replaced, the reason decides the successor (Kotlin for the JVM ecosystem, TypeScript for the contributor pool) | Decided (D-06) | Scored in D-06. Stack: ASP.NET Core minimal APIs, EF Core with Npgsql, xUnit, Testcontainers, ArchUnitNET, OpenTelemetry. |
| API | HTTP + JSON, contract-first with OpenAPI | Proposed | Stable, documented contract that Claude Code, tests and the later frontend all build against. |
| Events | Transactional outbox table in PostgreSQL | Proposed | Events are written in the same transaction as the change, so none are lost. No message broker in v1. |
| Identity | OpenID Connect to an external identity provider; roles and permissions held in the ERP | Decided (D-12) | Do not build login and password handling. |
| Reporting access | Read-only reporting schema (views), optional read replica later | Proposed | BI tools never read transactional tables directly. |
| Runtime | Container images; Docker or Podman Compose for development | Proposed | Runs the same on any Linux host or cloud. |
| Frontend | Deferred | TypeScript, Phase 5 | API first. |
| Observability | Structured logging, OpenTelemetry tracing and metrics, health endpoints, a correlation id on every command | Decided (D-25) | In place from the first commit; far cheaper than adding it later. Audit events already carry the correlation id. |
| Boundary enforcement | Architecture tests in CI that fail the build on illegal cross-module references; one database schema and role per module | Decided (D-26) | Makes "strict module boundaries" true in practice, using the same database-role technique as R-LED-06. The ledgers belong to the kernel posting service (R-LED-07); the shared document tables belong to the kernel document framework (D-39), and the role mechanism is D-41. |

**Backend language criteria**, as scored for D-06:

- **Exact decimals:** C# has a native `decimal`; Kotlin and Java use `BigDecimal`; TypeScript needs a decimal library and lint rules that forbid plain `number` for money and quantity, which is easy to get wrong.
- **Domain modelling:** strong static types for documents, states and commands. All three qualify.
- **Contributor pool for open source:** TypeScript is largest; C# and Kotlin are smaller but common in enterprise teams.
- **Readability for the project owner:** you will review what Claude Code writes, so the language you read best counts.
- **Result****:** C# on .NET, scored in D-06; TypeScript for the frontend; responses and events carry money and quantities as strings, and requests accept strings or numbers (D-37).

**Build constraints for the scaffold** (from D-06, D-26, D-35, D-37 and D-38)

- **Rules engine (D-35):** a length and nesting-depth limit checked at save time is the primary control, because the language has no loops or recursion. A runtime timeout is defence in depth. No time or random functions; "today" is passed in as a value. Only functions implemented in decimal are exposed. Division rounds at a defined precision, or the rule rounds explicitly, consistent with R-MON-02. Test cases are stored with each rule and run on save.
- **DynamicExpresso test (D-35):** its default number type must be set to decimal, and context objects handed to it must be plain, closed types with no methods worth calling.
- **JSON (D-37):** A decimal-only System.Text.Json converter writes decimals as strings; the global number-handling option is not used, because it would also turn integers into strings. The OpenAPI schema declares these fields as string with a decimal format, so generated clients type them correctly. Inbound requests accept both strings and JSON numbers; the server parses the number text exactly.
- **Persistence (D-26):** one EF Core data context per module, each with its own schema and a separately named migrations history table. Foreign keys from a process module to kernel tables are allowed (the module role gets REFERENCES on them); foreign keys between two process modules are forbidden and caught by the architecture test.
- **Architecture tests (D-26):** ArchUnitNET (Apache-2.0), chosen at scaffold time on 2026-10-09: NetArchTest.Rules has had no release since May 2021 and its maintained fork none since June 2025.
- **Licences (D-38):** a licence scan in CI fails the build on any copyleft dependency. The core stack is permissive (MIT, Apache-2.0, PostgreSQL licence); the risk is a later convenience package, and PDF libraries are the classic trap.

## Kernel domain model

The kernel has seven areas and every later module builds on them; no module adds its own customer, item or ledger table. Entity names below are working names for the code.

| Area | Entity | Purpose | Key design choice |
|---|---|---|---|
| Organisation | `Installation` | One installation per company group | No tenant identifier (D-28); companies inside an installation are separated by company scope. |
| Organisation | `Company` | A legal entity | Owns its ledgers, chart of accounts, base currency, VAT number and fiscal calendar. |
| Organisation | `Warehouse` | A place that holds stock, at a `Site` | Stock is held per warehouse; bins arrive in Phase 3. |
| Parties | `Party` | Any organisation or person | One record per real-world party, whatever its roles. |
| Parties | `PartyRole` | Customer, supplier, carrier and so on | Terms (payment terms, credit limit, price list) sit on the role, per company. |
| Parties | `Address`, `Contact` | Where and who | Effective-dated; personal data flagged for GDPR. |
| Items | `Item` | Anything bought, stocked or sold | Has a base unit, item type (stocked, non-stocked, service), lot tracking and typed attributes. Company-specific settings (status, posting group, tax class, costing method) sit on ItemCompany (D-43). |
| Items | `UnitOfMeasure`, `ItemUnit``, Unit``Conversion` | Units and pack levels | Item-specific conversions (1 box = 200 pcs) plus global ones (1 kg = 1,000 g). |
| Items | `AttributeTemplate` | Structured item properties | Typed fields (thread, length, material, finish, standard), not free text. |
| Items | `Lot` | A batch of one item | Optional per item; carries traceability such as heat number and certificate. |
| Commercial | `PriceList`, `PriceListLine` | Selling and buying prices | Effective-dated, with price unit and quantity breaks. |
| Commercial | `Currency`, `ExchangeRate`, `PaymentTerms` | Money terms | ISO 4217 currency codes. |
| Commercial | `TaxCode`, `TaxRule` | VAT determination | Rule-based; the result is stored on the line at posting. |
| Documents | `Document`, `DocumentLine` | Every business document | One framework for orders, deliveries, invoices; type-specific fields as extensions. |
| Documents | `DocumentLink` | Line-to-line flow with quantity | Order line to delivery line to invoice line, fully traceable. |
| Documents | `NumberSeries` | Document numbers | Per company, document type and period. |
| Ledgers | `InventoryEntry` | One stock movement | Item, lot, warehouse, base quantity, cost value, source line. Append-only. |
| Ledgers | `JournalEntry`, `JournalLine` | General ledger postings | Balanced, append-only, linked to the source document. |
| Ledgers | `Account`, `FiscalPeriod` | Chart of accounts and periods | Periods open, soft-closed or closed. |
| Ledgers | `OpenItem` | Customer and supplier balances (AR, AP) | Invoices and payments matched against each other. |
| Ledgers | `Reservation` | Stock promised to an order line | A claim on stock, not a movement. |
| Security | `User`, `Role`, `Permission`, `Scope` | Who may do what, where | Permissions per command, scoped to company and site. |
| Security | `AuditEvent` | Every write | Who, when, which command, what changed. |

## Core rulesets

These 58 rules (17 added by the Phase 0 specification, 3 by D-24 and its follow-ons, 1 by D-41, 1 by the 2026-10-09 review, 1 by D-49, 1 by D-52) apply to every module; code, tests and API errors refer to them by ID. A rule changes only through a recorded decision.

| ID | Rule | Why |
|---|---|---|
| R-LED-01 | Ledger entries are never updated or deleted. A mistake is corrected by a reversing entry plus a new entry. | Audit trail; bookkeeping law requires traceable corrections. |
| R-LED-02 | Every journal entry balances: total debit equals total credit in company currency. Rounding differences go to a defined rounding account. | Double-entry integrity. |
| R-LED-03 | Every ledger entry references its source document line and the command that created it. | Drill-down from any balance to its cause. |
| R-LED-04 | Every entry has a posting date, a document date and a system timestamp. Posting into a closed period is rejected; a soft-closed period needs a specific permission. | Period control for month-end. |
| R-LED-05 | A document's status change and all its ledger effects are written in one database transaction. | No half-posted documents. |
| R-INV-01 | On-hand quantity is the sum of inventory entries per item, lot and warehouse. A cached balance is allowed only if it can be rebuilt from entries and is checked. | Stock is always explainable. |
| R-INV-02 | Inventory entries are in the item's base unit, as exact decimals with a precision defined per unit. | Unit conversions happen once, at the edge. |
| R-INV-03 | Negative on-hand stock is rejected by default. | Prevents cost and availability errors; see decision log. |
| R-INV-04 | Available-to-promise = on hand minus reservations minus blocked stock. | Simple, explainable availability for v1. |
| R-INV-05 | Valuation is moving weighted average cost per item and company in v1. Each outbound entry stores its cost at posting. | IFRS permits weighted average or FIFO; average is simpler and common in distribution. |
| R-INV-06 | Every valued inventory entry posts to the general ledger in the same transaction (perpetual inventory). | Stock value and ledger always agree. |
| R-DOC-01 | Each document type has an explicit state machine; only listed transitions are allowed. | Predictable process behaviour. |
| R-DOC-02 | Only Draft documents are editable. Released documents change through defined commands (amend, cancel, reverse), each audited. | No silent changes after release. |
| R-DOC-03 | A follow-on line cannot exceed the open quantity of its source line, except within a configured over-delivery tolerance. | Prevents double delivery and double invoicing. |
| R-DOC-04 | Invoice and credit note numbers are assigned at posting from a sequential series without gaps. Drafts use temporary IDs. | EU VAT rules require a sequential number that uniquely identifies each invoice. |
| R-DOC-05 | Price, tax, cost and exchange rate used at posting are stored on the line. Later master data changes never alter posted documents. | Reproducible history. |
| R-MD-01 | One Party per real-world entity. A duplicate check (organisation number, VAT number, name and address) runs before creation. | Clean customer and supplier analysis. |
| R-MD-02 | Master data changes are effective-dated and audited; history is kept. | Point-in-time answers. |
| R-MD-03 | Master data in use is deactivated, never deleted. Personal data is anonymised when erasure applies. | Keeps ledgers valid while respecting GDPR. |
| R-MD-04 | Each master data domain has a named owner role. | Clear accountability for data quality. |
| R-MD-05 | A business code that already exists within the uniqueness scope stated on its attribute in the Phase 0 specification is rejected with R-MD-05. | One rule and one error code for every duplicate key, with the scope kept in one place (review RR-01). |
| R-MON-01 | Every amount carries its currency. Money and quantities use exact decimals; floating point is forbidden. | Floating point causes rounding errors. |
| R-MON-02 | Amounts are rounded to the currency's minor unit only at defined points (line net, tax, document total). Unit prices may carry more decimals. | Deterministic totals. |
| R-MON-03 | The exchange rate used is stored on the document. Journal lines hold both transaction and company currency amounts. | Multi-currency ready from day one. |
| R-TAX-01 | VAT is determined by rule from company, ship-from and ship-to country, the party's VAT status and the item tax class. | No hand-typed tax in the normal flow. |
| R-TAX-02 | The tax result is stored per line with the rule that produced it. | Explainable VAT. |
| R-TIME-01 | System timestamps are stored in UTC; business dates are calendar dates in the company's time zone. | No time zone drift in period cut-off. |
| R-SEC-01 | Every command and query is authorised against permission and scope (company, site) before it runs. A site-limited assignment never satisfies a command or query without a site, and a command or query that cannot name its scope is rejected (D-47). | Least privilege. |
| R-SEC-02 | Defined conflicting permissions cannot be held by one user, for example "set credit limit" and "release credit-blocked order". | Segregation of duties. |
| R-AUD-01 | Every write creates an audit event: user, time, command, idempotency key, changed fields. A posting's ledger rows record their own command, user and time; they, the cache updates and the number series counters are traced through the command's audit events, not one event each (D-51). The outbox relay writes only the delivery columns published_at and attempts, outside the command pipeline, and writes no audit event (D-53). | Full traceability. |
| R-API-01 | Every command accepts an idempotency key; a repeated key returns the first result with no second effect. | Safe retries from integrations. |
| R-API-02 | Documents carry a version number; an update based on a stale version is rejected. | No lost updates. |
| R-API-03 | A rejected command returns a stable error code and the ID of the rule that failed. | Explainable errors for users and integrations. |
| R-EXT-01 | Customer-specific behaviour uses extension points (extra fields, event handlers, validation hooks). Core code and tables are not modified. | Upgradability. |
| R-EXT-02 | Extensions never write ledger entries directly, only through core commands. | Ledger integrity cannot be bypassed. |
| R-UOM-01 | An item unit's factor is immutable once used on a posted document; a different factor needs a new unit code. | Posted quantities stay reproducible. Added by the Phase 0 spec. |
| R-TAX-03 | Exactly one tax rule must match a line; zero or several matches rejects the command. | Deterministic VAT. |
| R-DOC-06 | A Draft can be discarded (or cancelled, for types numbered on create) but never deleted. | Every issued number stays accounted for. |
| R-DOC-07 | A gapless number is taken inside the posting transaction under a row lock on its series. | A failed posting returns its number, so no gap appears. |
| R-DOC-08 | A command that assigns a number requires the series of that document type for the scope the number falls in (Continuous, or the fiscal year of its date); without it the command is rejected, naming the type and, for a per-fiscal-year series, the fiscal year. | A missing series is a setup error the user can fix; there is no fallback series or sequence (D-52). |
| R-ACC-01 | Control accounts accept postings only from their sub-ledger or the system documents VAT_SETTLEMENT and OPENING_OPEN_ITEMS, never from manual journals. | Sub-ledgers and the general ledger always reconcile. |
| R-INV-07 | An issue that empties an item's stock takes the item's full remaining value. | No value left on zero stock. |
| R-INV-08 | A stock status change moves quantity between statuses with zero value and no journal entry. | Blocking stock is not a financial event. |
| R-INV-09 | Costing follows recording order; there is no retroactive revaluation in v1. | Simple, explainable costs. |
| R-PER-01 | A Closed fiscal period is final in v1. | Reported figures never change. |
| R-API-04 | Reusing an idempotency key with a different request is rejected. | Catches integration bugs early. |
| R-PP-01 | The most specific valid posting rule wins, with keys ranked: item posting group, party posting group or ship-to scope, tax code, reason code. | Deterministic account mapping. |
| R-PP-02 | A line with no matching posting rule, or whose matching rule's account is not Active, rejects the command, naming the role and the keys. Resolution never falls back to a less specific rule. | No posting to a guessed account. |
| R-PP-03 | Reversals reuse the original lines' accounts and never resolve again. | A reversal exactly undoes the original. |
| R-PP-04 | Control roles map only to control accounts; other roles map only to non-control accounts. | Protects reconciliation. |
| R-LED-06 | The application's database role has no UPDATE or DELETE rights on ledger and audit tables. | Immutability enforced by the database, not only by code. |
| R-OI-01 | When a settlement closes an open item, its company-currency value is the item's full remaining company amount. | No rounding residue on settled items. |
| R-OI-02 | Reversing a settlement also reverses its write-off and exchange difference entries. | Receivables stay reconciled after reversals. |
| R-LED-07 | Stock movements and their journal entries are written by one kernel posting service in one transaction, behind a published interface. No inline cross-module subscribers exist in v1. | Stock and the books always agree (D-24). |
| R-LED-08 | Locks in a command transaction are taken by lock class in one fixed sequence, set out in the lock order table of the Phase 0 specification; each phase specification extends that table before it adds a lock class. Within a class, rows are locked in one SQL statement, ordered by the key that table states for the class. The number series is always last, and the gapless number is taken as the last step before commit. | No deadlocks; shortest possible lock on the series (D-34). |
| R-PP-05 | Creating or changing an item posting group, party posting group or tax code is rejected if the posting profile would become incomplete. | A configuration gap stops a master data change, never a shipment (D-32). |
| R-PP-06 | A posting profile rule sets only the mapping keys of its account role. | A key the role does not use can never redirect a posting to another account (D-49). |
| R-DB-01 | Every statement runs under the role of the module that owns the data, switched with SET LOCAL ROLE inside the command's single transaction. The login role holds no table privileges of its own. | Module boundaries enforced by the database while R-LED-05 stays atomic (D-41). |

**Invariants and configurable defaults (P-11, D-29).** A rule is fixed only when correctness, law or security needs it. Every other rule ships as the best-practice default and can be adjusted per company through audited configuration. Where a setting is planned but not yet built, v1 ships the default only, and the design must keep the setting possible.

**Invariants** (changing one needs a decision): R-LED-01 to R-LED-08, R-DB-01, R-INV-01, R-INV-02, R-INV-06, R-INV-07, R-INV-08, R-DOC-01 (system states), R-DOC-02, R-DOC-03 (the limit itself), R-DOC-04 (where law requires gapless numbering), R-DOC-05, R-DOC-07, R-DOC-08, R-MD-02, R-MD-03, R-MD-05, R-MON-01, R-MON-03, R-TAX-02, R-TAX-03, R-TIME-01, R-SEC-01, R-AUD-01, R-API-01 to R-API-04, R-EXT-01, R-EXT-02, R-UOM-01, R-ACC-01, R-PP-01 to R-PP-06, R-OI-01, R-OI-02, R-O2C-05 (the billing limit), R-O2C-08 (a credit note references the invoice line), R-O2C-09.

**Configurable defaults**

| Rule | Best-practice default | What a company can adjust |
|---|---|---|
| R-INV-03 | Negative stock rejected | Allow it per warehouse. Needs a costing rule for negative quantities first (D-09). |
| R-INV-04 | Available minus reservations minus blocked stock | Which statuses and future movements count |
| R-INV-05 | Moving weighted average | Costing method per company or item (FIFO later) |
| R-INV-09 | Recording order, no retroactive revaluation | Retroactive cost adjustment (landed cost, price variances) through a batch cost-adjustment document, to be specified with Phase 2 (D-33) |
| R-DOC-01 | System states only | Extra user states, transitions and approvals per document type; system states stay fixed |
| R-DOC-03 | Tolerance 0 % | Over-delivery tolerance per customer |
| R-DOC-06 | Discarded drafts are kept | Purge Discarded drafts that never received a number, after a retention period; Cancelled documents are never purged |
| R-MD-01 | Organisation number, VAT number, name and postal code | Duplicate-check fields and threshold |
| R-MD-04 | One owner role per master data domain | Which roles own which domain |
| R-MON-02 | Half away from zero, at line, tax and total | Rounding mode and level (D-10) |
| R-TAX-01 | Rule-based VAT | Tax rules per company. A manual override needs a permission and is stored as an override record (user, reason) in place of the rule, so R-TAX-02 and R-TAX-03 still hold |
| R-SEC-02 | Shipped conflict pairs | Conflict pairs are data; add or tighten them |
| R-PER-01 | Closed periods are final | Reopen with permission, where law allows |
| R-O2C-01 | Credit check blocks | Block, warn or off, per customer group |
| R-O2C-02 | By requested date, then release time | Allocation priority |
| R-O2C-03 | Partial delivery allowed | Complete delivery only, per customer or order |
| R-O2C-04 | Goods issue posts on shipment | Posting point |
| R-O2C-05 | Shipped deliveries combined per customer | Invoice grouping: per delivery, collective or periodic. The billing limit itself is an invariant |
| R-O2C-06 | Agreement, then customer list, then base list | Price sources and search order |
| R-O2C-07 | Tolerance per company | Tolerance amount and write-off account |
| R-O2C-08 | Returns valued at the original issue cost (D-11) | Return valuation: original issue cost or current average cost |

Split rules: R-DOC-01, R-DOC-03, R-O2C-05 and R-O2C-08 appear in both lists, and each list names the part it covers. R-PP-01 and D-19 stay in force: account and tax determination remain decision tables (D-35). The mapping rows are configuration; the resolution algorithm is fixed.

## Order-to-cash flow

Six document types make up order-to-cash; return and credit note share one box below. Each has its own state machine (R-DOC-01), and each follow-on document links back to its source line (R-DOC-03).

*[Diagram in the live document: order-to-cash document chain · states and ledger effects]*

The states shown are the proposed v1 set; the full transition tables, including the sales documents of Phases 1 and 2, are in the Phase 0 specification.

## Order-to-cash rules and postings

Nine process rules govern order-to-cash, and releasing an order only reserves stock. Every event after release posts to the ledgers, as the posting table shows.

| ID | Rule |
|---|---|
| R-O2C-01 | Releasing an order checks that the customer is active and not blocked, then runs a credit check: open receivables + open released orders + this order must not exceed the credit limit. A failing order goes to Credit blocked; releasing it needs a separate permission (R-SEC-02). |
| R-O2C-02 | On release, each line reserves available stock (R-INV-04). A shortfall becomes backorder quantity. Competing lines are served by requested date, then release time. |
| R-O2C-03 | Partial delivery is allowed unless the order is flagged "complete delivery only". |
| R-O2C-04 | Goods issue posts when the delivery is confirmed as shipped, not when it is picked. |
| R-O2C-05 | Invoicing is delivery-based: an invoice line can only bill delivered, not yet invoiced quantity. Advance and prepayment invoices are out of scope for v1. |
| R-O2C-06 | Price is found in this order: customer price agreement, then customer price list, then base price list. Quantity breaks apply to the line quantity. The result is stored on the line (R-DOC-05). |
| R-O2C-07 | A payment is matched to open items. A difference within a configured tolerance posts to a payment difference account; anything larger stays open. Unmatched money stays as an on-account credit. |
| R-O2C-08 | A credit note references the invoice line it corrects. A return with goods posts stock back at the cost of the original issue. |
| R-O2C-09 | Cancelling a released order releases its reservations. Shipped quantity cannot be cancelled, only returned. |

**Postings per event.** Account names are account roles. Each company maps roles to its own chart of accounts in a posting profile; account numbers are never hard-coded.

| Event | Inventory effect | Debit | Credit | Amount basis |
|---|---|---|---|---|
| Order released | Reservation only, no ledger entry | None | None | None |
| Goods issue confirmed | Outbound entry, quantity negative | Cost of goods sold | Inventory | Quantity x value / quantity at posting, rounded; the full remaining value when stock empties (R-INV-07) |
| Invoice posted | None | Accounts receivable (gross) | Revenue (net) and Output VAT (tax) | Invoice line amounts |
| Payment received | None | Bank | Accounts receivable | Payment amount |
| Payment difference within tolerance | None | Payment differences | Accounts receivable | Difference amount |
| Credit note posted | None | Revenue (net) and Output VAT (tax) | Accounts receivable (gross) | Credit note amounts |
| Customer return received | Inbound entry, quantity positive | Inventory | Cost of goods sold | Cost of the original issue |

Every row balances by construction: the goods issue and return post the same amount on both sides, and the invoice and credit note split gross into net plus tax.

## Fastener and distribution specifics

These ten requirements are where generic ERPs usually force workarounds in fastener distribution; most are built into Phase 0 and Phase 1 rather than added later.

| Requirement | Example | How Themis handles it | Phase |
|---|---|---|---|
| Price per price unit | 45.00 per 100 pcs | `PriceListLine` holds a price unit. Line amount = quantity / price unit x price, rounded per R-MON-02. | 0 |
| Pack sizes and sales multiples | Box of 200 pcs; customer orders 450 | Item unit conversions plus sales multiple and minimum quantity per item and customer. Default: round up to 600 with a warning. | 0 |
| Weight per piece | Weight per 1,000 pcs drives freight and counting | Item weight per base unit; delivery gross weight calculated. Counting by weight arrives with stock counts. | 1 and 3 |
| Structured attributes | Hex screw ISO 4017, M8 x 30, 8.8, zinc plated | `AttributeTemplate` per product family; attributes are typed and searchable. | 0 |
| Material certificates and lot traceability | EN 10204 3.1 certificate tied to a heat number | `Lot` with attached certificate; lot recorded on every inventory entry and shown on the delivery. | 1 |
| Customer item numbers | Customer's own part number on order and invoice | Customer item reference per customer role and item. | 1 |
| Many low-value lines | Thousands of items, orders with hundreds of cheap lines | Bulk import, performance targets for line-heavy documents, minimum order value surcharge as a charge line. | 1 |
| Equivalent standards | DIN 933 and ISO 4017 are near-equivalents, with some sizes differing in width across flats | Item relations (equivalent, substitute) with a note on differences; never automatic substitution. | Later |
| Kanban and vendor-managed stock | Bins replenished at the customer's site | Customer-site locations and replenishment orders. | Later |
| Certificates on demand | Customer asks which lot went into which delivery | Lot-to-delivery trace query on the inventory ledger. | 1 |

## Integration, standards and reporting

Themis integrates through one documented API, an event feed and standard file formats, and it ships its own reporting layer. Nothing outside the core reads transactional tables directly.

| Area | Approach | Standard or reference | Phase |
|---|---|---|---|
| API | Commands as `POST`, queries as `GET`, versioned paths (`/v1`) | OpenAPI 3 | 0 |
| Events | Domain events such as `S``ALES_ORDER_RELEASED, GOODS_ISSUED, INVOICE_POSTED (D-44)`, published from the outbox with a schema version; consumers must be idempotent | Own event catalogue | 0 |
| Bulk import | CSV or JSON import of master data with a dry-run mode and a validation report | Own format, documented | 0 |
| Codes | Currencies, countries and units use international code lists | ISO 4217, ISO 3166-1 alpha-2, UN/ECE Recommendation 20 (H87 piece, KGM kilogram) | 0 |
| Delivery terms | Incoterms on orders and deliveries | Incoterms 2020 | 1 |
| E-invoicing | Invoice data model aligned to the European semantic model so the output can be generated later | EN 16931, Peppol BIS Billing 3.0 | 2 model, later transport |
| Accounting export | Export of vouchers and balances for Swedish auditors and accounting tools | SIE 4 | 2 |
| Barcodes | Optional GTIN per item and unit | GS1 GTIN | 3 |

**Semantic layer.** A read-only reporting schema exposes a star schema built from the ledgers:

- **Facts:** inventory entries, journal lines, document lines (orders, deliveries, invoices), open items.
- **Dimensions:** company, customer, supplier, item with attributes, warehouse, account, date.
- **KPI definitions** (order intake, delivered not invoiced, on-time in-full, stock value, days sales outstanding) are versioned in the repository with a named owner. The API and BI tools use the same definition.
- Power BI, Qlik or any SQL tool connects to this schema only.

## Security, governance and open source

Security is built into the kernel, not added later, and the licence must be chosen before the first public commit because it cannot easily change once others contribute.

- **Access model:** permissions per command, grouped into roles, scoped to company and site (R-SEC-01). A conflict matrix enforces segregation of duties (R-SEC-02).
- **Integrations:** service accounts with the narrowest scope, authenticated through the identity provider. No shared user accounts.
- **Company**** isolation:** one installation per company group, without a tenant identifier (D-28). Company-owned rows carry company_id, with PostgreSQL row-level security on company scope as a second line of defence.
- **Audit:** append-only audit events for every write (R-AUD-01), kept as long as the accounting records they explain.
- **Personal data:** limited to contacts, addresses and users, with classified fields. Erasure means anonymisation (R-MD-03), because accounting records must be kept for a statutory period, for example 7 years under the Swedish Bookkeeping Act. Retention rules per country need verification.
- **Secrets:** never in the repository; supplied through environment configuration.
- **AI-built code:** every change Claude Code produces is reviewed against the rule IDs here and ships with tests. Changes to R-LED and R-INV rules need explicit owner review.

**Licence options** (decision D-07):

| Licence | Effect | Used by (for reference) |
|---|---|---|
| AGPL-3.0 | Strong copyleft, including software offered over a network. Stops closed SaaS forks; may deter some companies. | No ERP example verified |
| GPL-3.0 | Strong copyleft for distributed software; does not cover network use. | ERPNext |
| LGPL-3.0 or MPL-2.0 | Weak copyleft: changes to the core stay open, extensions may be closed. | Odoo Community (LGPL-3.0) |
| Apache-2.0 | Permissive with a patent grant. Widest adoption; allows closed forks. | Apache OFBiz |

Decided for the project period: Apache-2.0 (D-07). It keeps later options open: Apache-2.0 code can be combined into a GPL-3.0 or AGPL-3.0 work, and the licence grants no trademark rights, which protects the Themis name. Contributor terms remain open (D-36).

## Risks and trade-offs

The biggest risk is a kernel model that turns out wrong after code depends on it; the mitigation is to test the model on paper against procure-to-pay before freezing it.

| Risk | Impact | Mitigation |
|---|---|---|
| Kernel model found wrong late | Costly data migrations | Walk procure-to-pay and warehouse flows through the model on paper before Phase 0 is frozen; build purchasing and sales end to end early (D-30). |
| Scope creep toward "everything" | The project never reaches a usable state | Phase gates and the non-goals list in Scope and phases. |
| AI-generated code drifts from the rules | Silent ledger or stock errors | Invariant tests (every entry balances; on hand equals the sum of entries), rule IDs referenced in tests, human review of ledger code. |
| Rounding and decimal errors | Wrong invoices and VAT | R-MON rules, exact decimal types, a library of reference calculations with known answers. |
| Derived balances slow at scale | Slow availability and reporting | Checked cached balances (R-INV-01), period snapshots, indexing. |
| Statutory rules differ per country | Not usable for real companies outside Sweden | Country rules as localisation packages; Sweden first; no compliance claims until reviewed. |
| Average cost does not fit every business | Adoption blocked where FIFO is required | Valuation method behind one interface so FIFO can be added. |
| One-person project | Work stalls | Docs first, recorded decisions, open source. |

**Trade-offs accepted:** append-only ledgers mean more rows and correction-by-reversal instead of editing; a modular monolith gives up independent scaling of modules in exchange for simple, consistent transactions.

## Decision log

Forty-nine decisions are made and four are open (D-10, D-20, D-36, D-48); none of the open ones blocks the walking skeleton.

| ID | Decision | Status | Note |
|---|---|---|---|
| D-01 | Goal: open-source product, built backend first, frontend later | [Decided] | Your answer, 2026-10-08 |
| D-02 | First end-to-end slice: order-to-cash for a distributor | [Decided] | Your answer, 2026-10-08. Superseded by D-30 (purchasing and sales together) |
| D-03 | Concepts and rules live here; Claude Code builds from them; design doc before prototype | [Decided] | Your answer, 2026-10-08 |
| D-04 | PostgreSQL as the system of record | [Decided] | Confirmed 2026-10-09 |
| D-05 | Modular monolith | [Decided] | Confirmed 2026-10-09 |
| D-06 | Backend language: C# on the current .NET LTS release (.NET 10; verify at scaffold time). TypeScript for the Phase 5 frontend | [Decided] | Your confirmation, 2026-10-08. Scored C# 161 of 175, Kotlin 135, TypeScript 131. Stack and build constraints under Technology direction |
| D-07 | Licence: Apache-2.0 for the project period | [Decided] | Your decision, 2026-10-08. Revisit before the first public release; contributor terms in D-36 |
| D-08 | Moving weighted average valuation in v1 | [Decided] | Confirmed 2026-10-09. R-INV-05; a configurable default under P-11 |
| D-09 | Reject negative stock by default; v1 ships this default only, and the per-warehouse setting stays possible | [Decided] | Confirmed 2026-10-08. Allowing negative stock first needs a costing rule for negative quantities |
| D-10 | VAT rounding per line or per document total | [Open] | A company setting under P-11; this decision picks the default. Needed before invoicing is built (Phase 2) |
| D-11 | Returns valued at the cost of the original issue | [Decided] | Confirmed 2026-10-09. Configurable under P-11 |
| D-12 | Identity: Keycloak for development and self-hosting; any OpenID Connect provider (for example Entra ID) in production; SCIM 2.0 for user provisioning | [Decided] | Confirmed 2026-10-08 |
| D-13 | First localisation: Sweden (SEK, Swedish VAT, SIE 4) | [Decided] | Confirmed 2026-10-09. Account choices still need an accountant (D-20) |
| D-14 | Product name: Themis, with Jozen as the working brand | [Decided] | Your choice, 2026-10-08. Trademark check before the repository goes public: Themis is already used by governance, risk and compliance software |
| D-15 | Journal amounts are signed, debit positive | [Decided] | Confirmed 2026-10-09 |
| D-16 | Type-specific document header fields in one-to-one extension tables, not JSON | [Decided] | Confirmed 2026-10-09. Ownership in D-39 |
| D-17 | Identifiers are UUID v7 | [Decided] | Confirmed 2026-10-09 |
| D-18 | Stock status (Available, Blocked) is part of the stock balance key | [Decided] | Confirmed 2026-10-09 |
| D-19 | Posting rule key order: item posting group, party posting group or ship-to scope, tax code, reason code | [Decided] | Confirmed 2026-10-09. In force: account determination stays a decision table (D-35) |
| D-20 | Swedish accounts for COGS, inventory adjustments and realised exchange differences | [Open] | Choose with an accountant before a real company goes live. Test fixtures use placeholders |
| D-21 | Foreign-currency documents from the first business phase, with realised exchange differences at settlement | [Decided] | Confirmed 2026-10-09 |
| D-22 | REVENUE is keyed by item posting group, ship-to scope and tax code; COGS by item posting group and ship-to scope. Ship-to scope alone cannot match VAT treatment, which also depends on the party's VAT status and the item's tax class (R-TAX-01) | [Decided] | Confirmed 2026-10-09; amended the same day to add tax code to REVENUE (review RR-19) |
| D-23 | Integration Hub deferred to a later phase; nothing decided may make it hard to add, so every business action stays reachable as a published command and event | [Decided] | Your decision, 2026-10-08 |
| D-24 | Stock and ledger consistency: one transaction (option A), through one kernel posting service behind a published interface; no inline cross-module subscribers in v1 | [Decided] | Your decision, 2026-10-08, with the architect's agreement. R-LED-07. Gaps are caught at master data time (D-32) |
| D-25 | Observability baseline from the first commit | [Decided] | Confirmed 2026-10-09 |
| D-26 | Enforced module boundaries: architecture tests in CI, one database schema and role per module | [Decided] | Confirmed 2026-10-09. Document tables owned by the kernel (D-39); role mechanism in D-41 |
| D-27 | Walking skeleton before the rest of Phase 0 | [Decided] | Confirmed 2026-10-09. Milestone 1 in the build brief; build progress in the repository README |
| D-28 | No tenant identifier: one installation per company group, with several companies inside | [Decided] | Your decision, 2026-10-08 |
| D-29 | Agile by design (P-11): invariants are fixed; everything else ships as a best-practice default that can be configured | [Decided] | Your decision, 2026-10-08. Classification under Core rulesets |
| D-30 | Build order: kernel with master data and inventory, then Purchasing and Sales together, then finance through a first financial close, then Warehousing, then Quality, Transportation and Manufacturing | [Decided] | Your decision, 2026-10-08. The general ledger core stays in Phase 0. Version 1 is Phases 0 to 2 |
| D-31 | Rules as data in a sandboxed, deterministic expression language | [Decided] | Your decision, 2026-10-08. Refined by D-35: account and tax determination stay decision tables |
| D-32 | Posting profile gaps are caught at master data time (R-PP-05) | [Decided] | Confirmed 2026-10-08 |
| D-33 | External finance systems consume Themis journal entries (event feed, SIE 4); the internal ledger stays. Sub-ledger accounting (option C) is documented and revisited before Phase 2 | [Decided] | Confirmed 2026-10-09 |
| D-34 | Fixed lock order in posting transactions (R-LED-08); several gapless series per company allowed | [Decided] | Confirmed 2026-10-08. Several series still to confirm with an accountant |
| D-35 | Rules expression language: NCalc in decimal mode behind one engine interface, with whitelisted decimal-only functions, defined division precision and save-time validation against a declared context. Used for guards, validation, defaulting and pricing conditions; account and tax determination stay decision tables. Exit conditions at scaffold time, DynamicExpresso tested against the same conditions; an owned decimal language modelled on CEL is the fallback | [Decided] | Confirmed 2026-10-08, with the architect's notes |
| D-36 | Contributor terms: Developer Certificate of Origin or a contributor licence agreement | [Open] | Before the repository goes public or accepts an outside contribution |
| D-37 | Responses and events carry money and quantities as JSON strings, never as JSON numbers. Requests accept strings or numbers and parse the number text exactly, never through a binary float | [Decided] | Confirmed 2026-10-08; wording clarified 2026-10-09 (review RR-02) |
| D-38 | A licence scan in CI fails the build on any copyleft dependency | [Decided] | Confirmed 2026-10-08. Watch PDF libraries in particular |
| D-39 | The document framework is a kernel platform module. It owns Document, DocumentLine, DocumentLink, NumberSeries and Attachment in the kernel schema, plus the state machine engine and numbering. Type-specific extension tables belong to the module that owns the document type, in that module's schema, with a foreign key to the kernel document; the kernel owns the extensions of its own document types | [Decided] | Your decision, 2026-10-09. A module reaches the framework only through its interface |
| D-40 | Document type registration is table-driven from the start, system-owned in v1 | [Decided] | Your decision, 2026-10-09. Configurable lifecycles under P-11 need no rewrite later |
| D-41 | Database access: one connection and one transaction per command, with SET LOCAL ROLE to the owning module's role before its statements. The login role holds no table privileges itself; cross-schema reads are granted explicitly. Rejected: one wide role per command. Later option: ledger inserts only through a security-definer function. Statement-level retries stay forbidden; a whole command may be retried in a new transaction, at most twice, only after SQLSTATE 40001 or 40P01, restarting the full pipeline including authorisation, with retries counted in a metric | [Decided] | Your decision, 2026-10-09. R-DB-01; proven by invariant test I-14. Retry amendment 2026-10-09 (review RR-06) |
| D-42 | Version control: Git. During development, GitHub is the single service for repository hosting and CI (GitHub Actions). The repository stays private until D-14 and D-36 are settled | [Decided] | Your decision, 2026-10-09. GitHub confirmed the same day |
| D-43 | Company-specific item data lives on ItemCompany, one row per item and company: status, item posting group, tax class, costing method, sales and purchase permission, default warehouse and units. Item keeps installation-wide identity: number, description, item type, base unit, lot tracking, units and attributes. Posting profile resolution and tax rules read ItemCompany | [Decided] | Your decision, 2026-10-09 (review RR-11). Costing method is master data on ItemCompany, not on the ItemCostState cache. Status is per company only; there is no installation-wide block, so retiring an item means deactivating it in every company (confirmed the same day) |
| D-44 | Every state change writes one outbox event (one per document or master data row whose state a command changes), named TYPE plus the command in past tense, version 1 (for example ITEM_CREATED, INVENTORY_ADJUSTMENT_DISCARDED). Document types record business names that replace the generated one (for example GOODS_ISSUED, INVENTORY_ADJUSTED) per document type, command and resulting state in the document type registry (D-40). Business payloads are specified only for events that integrations consume | [Decided] | Your decision, 2026-10-09 (review RR-04). One event per document or master data row whose state a command changes, with business names per document type, command and resulting state: confirmed the same day |
| D-45 | Temporary exception to P-11: the 7-day idempotency retention (IdempotencyRecord.expires_at) is a constant in KernelPipelineOptions until the settings framework (review RR-12) exists. It moves to a setting before Phase 0 is declared complete; this exception then ends | [Decided] | Your decision, 2026-10-09, on the coder's step 2 proposal (PR #5) |
| D-46 | P-02 amended with a kernel guard: a business concept or master data entity enters the kernel only if a ledger references it or at least two module specifications name it; kernel infrastructure is outside the test; every kernel entity records its justification in the Phase 0 specification, and a pull request adding one cites it. Ledger (L): a ledger row references the entity through a chain of required (NOT NULL) foreign keys, or the posting service reads its rows at posting time to determine, classify, value or post a ledger row; ledger caches count as L; data copied onto a document before posting does not make its source L. Module, provisional: names the two module specifications expected to need it; when the second is released the entity is confirmed or scheduled to leave the kernel before that module's build; a provisional entity is never read by posting or referenced by a ledger | [Decided] | Your decision, 2026-10-09 (second external review, register RR-34). Wording refined with the coder; definitions of L and provisional M confirmed the same day after the independent review of the justification table |
| D-47 | R-SEC-01 amended: queries are authorised like commands, against a TYPE.READ permission and scope; a site-limited assignment never satisfies a command or query without a site; a command or query that cannot name its scope is rejected (installation-wide ones declare installation scope) | [Decided] | Your decision, 2026-10-09, while settling the milestone 1 step 4 brief with the coder |
| D-48 | Placement of replenishment planning for distribution (reorder point, order quantity, safety stock, forecast-driven requisitions) on the roadmap: a new phase or an extension of Phase 3. Any placement changes the phase list set by D-30 | [Open] | Raised 2026-10-12 from the Capability Coverage Map (gap G-02). Decide when the Phase 1 scope is set; D-30 stands unchanged until then |
| D-49 | Posting profile rules hardened: R-PP-06 added (a rule sets only the mapping keys of its account role); R-PP-02 amended (a line whose matching rule's account is not Active is also rejected, and resolution never falls back to a less specific rule) | [Decided] | Your decision, 2026-10-13, while settling the milestone 1 step 6 brief with the coder. Enforced by database constraints generated from the account role catalogue (R-PP-04, R-PP-06) and by the resolver (R-PP-02) |
| D-50 | Event envelope (review RR-15): every outbox event carries its id, type and schema version (D-44), aggregate type and id, company (empty only for installation-scope aggregates), occurrence time, command name and correlation id. The idempotency key and the user never travel with events. Ordering, claiming, retries, dead letters and retention are specified before integrations | [Decided] | Your decision, 2026-10-17, while settling the milestone 1 step 7 brief with the coder. Built in step 7. Development and test databases may be recreated for the step 7 envelope migration, since none holds data worth keeping before milestone 1 is done |
| D-51 | Audit of postings (amends R-AUD-01): a posting's journal and inventory entries record their own command, user and time; caches are rebuilt from the ledgers; number series counters are traced by the numbers they issue. None of these gets its own audit event. The command's audit events cover the document and its lines, and every audit event stores the command id, so each ledger row leads to its command's audit events and idempotency key. Audit events written before the command id existed keep it empty; it is required for every audit event written after, and no audit row is ever changed | [Decided] | Your decision, 2026-10-18, while settling the milestone 1 step 8 brief. One audit event per ledger row and cache change was rejected: it roughly doubles the writes of every posting and adds no trace the ledger rows lack. A one-time fill of the earlier audit events was rejected after the independent review, since it would change audit rows. Built in step 8. |
| D-52 | Missing number series (rule R-DOC-08): a command that must assign a number and finds no series for its document type and scope is rejected; there is no fallback. A posting needs the journal entry series only when it writes a journal entry. A guard that a fiscal year cannot open without its series waits for the fiscal year commands | [Decided] | Your decision, 2026-10-18, while settling the milestone 1 step 8 brief; closes the item carried from step 7. Built in step 8 |
| D-53 | Outbox relay and audit (amends R-AUD-01): the relay writes only the delivery columns published_at and attempts of an outbox event. It runs outside the command pipeline, has no user, command or idempotency key, and writes no audit event; the delivery columns are the record. Each pass is one short transaction run as themis_kernel with SET LOCAL ROLE, as R-DB-01 requires of a command's transaction. It picks up events in short transactions that never let two passes hold the same event at the same time; delivery is at least once, so consumers must be idempotent. Ordering per aggregate, claiming with a lease, retries, dead letters and retention stay with the D-50 delivery terms | [Decided] | Your decision, 2026-10-18, while settling the milestone 1 step 9 brief with the coder. One audit event per published event was rejected: a relay pass has none of the fields R-AUD-01 names, so the event would be malformed as well as redundant. Built in step 9. |

## Next steps

The Phase 0 specification exists; the next design steps are the Phase 1 purchasing and Phase 2 finance specifications. Coding starts with milestone 1 of the Themis Build Brief, now that D-06 and D-07 are Decided.

- Review this doc and comment on any rule you disagree with
- Decide D-06, backend language, by scoring the criteria
- Decide D-07, licence (contributor terms continue as D-36)
- Confirm D-13, Sweden as first localisation
- Walk procure-to-pay through the kernel model on paper
- Write the Phase 0 specification: attribute lists per entity, state machine per document type, posting profile design
- Write the order-to-cash scenario test catalogue
- Settle D-20 and D-10 with an accountant
- Write the Phase 1 purchasing specification (purchase order, goods receipt)
- Choose the rules expression language (D-35)
- Write the Phase 2 finance specification (supplier invoice, supplier payment, cost adjustment, year-end)
- Write the build brief for Claude Code: repository layout, a CLAUDE.md that carries the rule IDs, first milestone
