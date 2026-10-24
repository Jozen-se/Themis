<!-- SNAPSHOT. Do not edit by hand. Regenerate from the live document. -->

> **Snapshot** of the live document *Themis Build Brief* (https://claude.ai/code/artifact/a781c405-11f8-42e1-bf7f-7090dbc9df8b), revision 135, taken 2026-10-24.
> The live document is authoritative; this copy exists so that code review and offline reading can cite rule IDs
> without leaving the repository. Where this copy and the live document differ, the live document wins.
> Diagrams are not reproduced; their captions are kept in place.

# Themis Build Brief

2026-10-09 · Sebastian Hansson

This brief tells Claude Code how to start building Themis: the stack, the repository layout, the database role model and the first milestone, a walking skeleton that posts an inventory adjustment to both ledgers in one transaction. It adds no new business rules; those live in Themis Foundation Design and Themis Phase 0 Specification.

## Sources of truth and order of authority

When two sources disagree, the higher one wins and the coder reports the conflict instead of choosing.

1. **Themis Foundation Design:** principles P-01 to P-11, the core and order-to-cash rules (R-xx) with their invariant or default classification, the decision log (D-xx), phases and the build constraints for the scaffold.
2. **Themis Phase 0 Specification:** entity attributes, state machines, posting profile, posting events (E-xx) and invariants (I-xx).
3. **This brief:** stack, layout, database model and milestone scope.
4. **Code and tests.** They implement the documents; they never redefine them.

The Architecture Recommendations, once background reading, no longer exist (removed 2026-10-24); what was accepted from them is in the foundation doc, for example the general ledger posting path (D-24).

## Ground rules for the coder

Ten rules govern every change; breaking one is a defect even if the tests pass.

1. **Cite the rule.** Code that enforces a rule names its ID in a comment, and the test that proves it names the ID in its test name, for example `R_LED_02_journal_entry_must_balance`.
2. **Rejected commands return the rule ID** that failed, with a stable error code (R-API-03).
3. **No floating point** for money, quantity, cost or rates anywhere: C# `decimal`, PostgreSQL `numeric`, JSON strings (R-MON-01, D-37). A build-time analyser or test fails on `double` or `float` in domain and persistence code.
4. **Ledger and audit tables are insert-only** (R-LED-01, R-LED-06). Corrections are reversals.
5. **Stock and journal entries are written only by the kernel posting service**, in the command's single transaction (R-LED-05, R-LED-07).
6. **Modules talk through Contracts projects only** (D-26). No module touches another module's tables or internal types.
7. **Every business action is a named command** with permission, idempotency key, optimistic concurrency and an audit event (R-SEC-01, R-API-01, R-API-02, R-AUD-01). Every state change writes its outbox event in the same transaction.
8. **Defaults are configuration, invariants are code** (P-11). Never hard-code a value the foundation doc lists as a configurable default; read it from settings, with the documented default.
9. **When the documents are silent or ambiguous, stop and ask** with a short question that names the section. Do not invent business rules.
10. **Propose, don't drift.** If the implementation shows a rule is wrong or missing, write the proposal in the pull request description as "Proposed change to R-xx" for the owner to decide in the planning project.

## Stack and versions

Every component is decided; the coder only confirms current versions at scaffold time and records them in the repository README.

| Area | Choice | Decision | Check at scaffold time |
|---|---|---|---|
| Language and runtime | C# on the current .NET LTS (.NET 10) | D-06 | Exact SDK version; pin it in `global.json` |
| Web API | ASP.NET Core minimal APIs with built-in OpenAPI generation | D-06 |  |
| API exploration | Interactive page over the built-in OpenAPI document: Scalar or Swagger UI, both MIT | Review RR-22 | Development only; licence passes the D-38 scan |
| Database | PostgreSQL, current major version | D-04 | Pin the image tag in the compose file |
| Persistence | EF Core with Npgsql; one data context per module; raw SQL allowed for ledger reads | D-06, D-26 |  |
| Identity | OpenID Connect through the standard ASP.NET Core handler; Keycloak in the compose file | D-12 |  |
| Rules engine | NCalc in decimal mode behind one engine interface; DynamicExpresso tested against the same exit conditions | D-35 | Exit conditions in Technology direction; not needed until after milestone 1 |
| Tests | xUnit; Testcontainers for PostgreSQL; FsCheck or CsCheck for property-based invariant tests | D-06 | Licences pass the D-38 scan |
| Architecture tests | ArchUnitNET (TngTech.ArchUnitNET 0.13.4, Apache-2.0), pinned through central package management and the lock file | D-26 | Done 2026-10-09: NetArchTest.Rules (last release May 2021) and NetArchTest.eNhancedEdition (June 2025) checked and rejected |
| Observability | OpenTelemetry for logs, traces and metrics; health endpoints | D-25 |  |
| Licence scan | A NuGet licence scanner in CI that fails on copyleft | D-38 | Pick the tool |
| Local runtime | One compose file that runs with Docker Compose and Podman Compose | D-27 | Runs on the owner's Linux machine with Podman |
| CI | Build, unit, architecture, integration and invariant tests, licence scan, on every push | D-26, D-38 | GitHub repository with GitHub Actions workflows; public since D-14 and D-36 were settled (D-42, RR-09) |

## Repository layout

Milestone 1 is kernel only. Master data that the ledgers reference (items, units, posting groups, currencies, and later parties in Phase 0 and prices in Phase 1, D-55) belongs to the kernel, as the foundation's kernel domain model and P-02 say. Process modules start in Phase 1 and all follow one five-project template; until then a test-only fixture module proves the boundary rules.

```text
themis/
  CLAUDE.md                        instructions for Claude Code (last section of this brief)
  README.md                        how to run, pinned versions
  LICENSE                          Apache-2.0 (D-07)
  global.json                      pinned .NET SDK
  Directory.Build.props            nullable reference types on, warnings as errors, analysers
  Themis.sln
  compose.yaml                     PostgreSQL and Keycloak; Docker Compose or Podman Compose
  db/bootstrap/                    login roles, module roles, schemas, revokes, btree_gist (D-41, R-MD-02); run once per database
  docs/design/                     dated read-only snapshots of the design documents
  src/
    Themis.Host/                   composition root: wiring only, no business logic; never runs migrations
    Themis.Migrator/               console tool that applies migrations as themis_migrator
    Kernel/
      Themis.Kernel.Primitives/    Money, Quantity, UnitCode, BusinessDate, typed ids, Result and Error with rule IDs
      Themis.Kernel.Contracts/     the interfaces modules may call: posting, documents, organisation, master data, identity
      Themis.Kernel.Pipeline/      command and query dispatch: validation, authorisation, idempotency, transaction, audit, telemetry
      Themis.Kernel.Persistence/   the one kernel data context, unit of work, shared connection and transaction, role switching
      Themis.Kernel.Organisation/  installation, company, site, warehouse, fiscal years and periods
      Themis.Kernel.MasterData/    items, units, item posting groups, currencies (parties in milestone 2 step 8; prices in Phase 1, D-55)
      Themis.Kernel.Documents/     document framework (D-39, D-40): shared tables, state machine engine, numbering
      Themis.Kernel.Posting/       posting service (R-LED-07): inventory ledger, general ledger core, posting profile, costing
      Themis.Kernel.Messaging/     outbox and relay
      Themis.Kernel.Identity/      principals, permissions, role assignments, OpenID Connect
    Modules/                       empty in milestone 1; Phase 1 adds Purchasing and Sales
  tests/
    Fixtures/Themis.FixtureModule/ test-only module with its own schema and role, used by architecture and role tests
    Themis.Architecture.Tests/     boundary rules (D-26), no floating point in domain or persistence code (R-MON-01)
    Themis.Kernel.Tests/           unit tests
    Themis.Integration.Tests/      Testcontainers PostgreSQL; role and privilege tests (I-14)
    Themis.Invariants.Tests/       property-based tests for the I-xx invariants
```

**Module template** (every process module from Phase 1): `Contracts`, `Domain`, `Application`, `Infrastructure` (its own data context, schema and migrations), `Api`. Only `Contracts` may be referenced from outside the module. Routes are declared without referencing the web framework (ICommandRoute, mapped by the composition root through ICommandRouteMapper), so every request passes through the one command pipeline and modules keep referencing only Primitives and Contracts (D-26).

**Reference rules,** enforced by the architecture tests:

- A module's projects may reference `Themis.Kernel.Primitives`, `Themis.Kernel.Contracts` and other modules' `Contracts` projects, nothing else outside the module.
- Kernel projects never reference a module or the fixture module.
- `Themis.Host` and `Themis.Migrator` reference everything and contain wiring only.

## Database model: schemas, roles and privileges

The running application logs in as one role that owns nothing and can do nothing until it switches to a module role inside the transaction (D-41, R-DB-01). Milestone 1 has the schemas `kernel` and `reporting`, plus `fixture` in the test database only.

| Role | Login | Purpose | Privileges |
|---|---|---|---|
| `themis_migrator` | Yes | Used only by `Themis.Migrator` | Owns all schemas, tables and views; issues every grant explicitly in its migrations |
| `themis_app` | Yes, NOINHERIT | The application's connection | No table privileges of its own. Member of each module role with `WITH INHERIT FALSE, SET TRUE` (PostgreSQL 16 or later) |
| `themis_kernel` | No | Kernel statements, including the posting service, document framework and outbox relay | USAGE on `kernel`. SELECT, INSERT, UPDATE on master data, document, cache and settings tables; INSERT and SELECT only on journal, inventory entry and audit tables (R-LED-06); UPDATE only on outbox delivery columns; DELETE only on expired idempotency records |
| `themis_fixture` | No | Test-only module role | USAGE and full DML on `fixture`; SELECT on the kernel tables it reads |
| `themis_reporting` | Yes | BI tools | USAGE on `reporting`; SELECT on its views only |

**Grant hygiene:**

- Revoke CONNECT on the database and CREATE on the `public` schema from PUBLIC; grant CONNECT only to the three login roles.
- No default privileges: every table and view gets its grants explicitly in the migration that creates it. A catalogue test compares the actual privileges with an expected privilege matrix and fails on anything extra or missing (part of I-14), so a new ledger table can never silently receive UPDATE. The one default-privilege setting is restrictive: the bootstrap script revokes EXECUTE from PUBLIC on functions that migrations create in the kernel and reporting schemas (ALTER DEFAULT PRIVILEGES FOR ROLE themis_migrator ... REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC), so a function runs only for roles its migration grants EXECUTE explicitly. Confirmed by the owner 2026-10-17.
- Business numbers come from number series rows, not database sequences. If a migration adds a sequence, it grants USAGE explicitly.
- No REFERENCES grants are needed: foreign keys are created by `themis_migrator`, which owns both tables.
- Reporting views run with their owner's rights in milestone 1. How reporting views respect row-level security is decided in milestone 2 step 13a (RR-13, RR-58).

**One command, one transaction:**

1. The pipeline opens one Npgsql connection as `themis_app` and begins the transaction.
2. The kernel data context, and in later phases any module contexts, are built on that same connection with `UseTransaction`. No statement-level retrying execution strategy: it would replay half a command. The pipeline may instead retry the whole command in a new transaction, at most twice, only after SQLSTATE 40001 or 40P01, restarting the full pipeline, authorisation included, with retries counted in a metric (D-41).
3. Every statement, raw SQL included, goes through the unit of work. A command interceptor issues `SET LOCAL ROLE` to the owning role whenever the current role differs. Nothing opens a second connection.
4. The posting service takes its locks in one SQL statement ordered in PostgreSQL (`ORDER BY item_id ... FOR UPDATE`), never by .NET `Guid` ordering, which differs. The first posting of an item inserts its cost state row with `ON CONFLICT DO NOTHING` before locking. The number series row is locked and the number taken last. The full sequence (own document row, ItemCostState, StockBalance, number series) is the lock order table in the Phase 0 specification (R-LED-08).
5. Commit. `SET LOCAL` ends with the transaction, so pooled connections return clean.

The outbox relay is a background service with its own short transactions, running as `themis_kernel`.

**Migrations:** one data context for the whole kernel schema, because kernel tables reference each other. Module contexts map the kernel tables they read with `ExcludeFromMigrations()`. Each schema has its own migrations history table. Order: kernel first, then modules. Only `Themis.Migrator`, with the `themis_migrator` connection string, applies migrations; the application never calls `Migrate()`. Foreign keys between two process modules are forbidden and the architecture test fails on them.

**What this protects, and what it does not.** It stops accidental cross-module writes and any update or delete of ledger rows, even by kernel code. It does not stop deliberately malicious code inside the application process, because `themis_app` may switch to any module role. The later option of ledger inserts only through a security-definer function closes part of that gap (D-41).

Row-level security on company scope (D-28) is added in milestone 2 step 13a (D-67); every company-owned table carries `company_id` from the start.

## Cross-cutting conventions

These apply to every module from the first commit, so that later modules look the same as the first one.

| Topic | Convention | Source |
|---|---|---|
| Identifiers | UUID v7 generated in the application (`Guid.CreateVersion7()`), stored as `uuid` | D-17 |
| Money and quantity | `decimal` in C#, `numeric` in PostgreSQL with the precisions in the spec's Conventions section; `Money` always carries its currency | R-MON-01 |
| JSON numbers | A decimal-only converter writes decimals as strings (the global number-handling option would turn integers into strings too); an OpenAPI schema transformer declares them as string with a decimal format; inbound accepts strings and numbers | D-37 |
| Time | `timestamptz` in UTC for system time; `DateOnly` and `date` for business dates in the company's time zone; business time comes from an injected clock so tests use fixed dates | R-TIME-01 |
| Commands | Create: `POST /v1/{area}/{resource}`. Every other command: `POST /v1/{area}/{resource}/{id}/{command}`, for example `POST /v1/kernel/inventory-adjustments/{id}/post`. The permission code is TYPE.COMMAND, for example `ITEM.CREATE` or `INVENTORY_ADJUSTMENT.POST` | P-04, R-SEC-01 |
| Queries | `GET` only; queries never change state. Company-scoped queries name the company in the route, for example GET /v1/kernel/companies/{companyId}/items/{id}, so authorisation runs before anything is loaded | P-04 |
| Idempotency | `Idempotency-Key` header required on every command. Completed results, rule rejections included, are stored and replayed. A second request with the same key while the first is still running gets 409 Conflict. The same key with a different request is rejected with R-API-04. The request fingerprint is the command type, the body and the If-Match value. Stored and replayed: handler outcomes, rule rejections included. Not stored: authentication, authorisation and validation outcomes, the 409 duplicate, 428 and 412 | R-API-01, R-API-04 |
| Concurrency | `If-Match` carrying the record version on every command that changes an existing record; a missing If-Match gets 428, a stale version 412. Every successful command returns the new version as ETag, and a replay returns the stored one. The endpoint writes If-Match into the command's expectedVersion and overwrites any client-supplied value; expectedVersion is not part of the OpenAPI document | R-API-02 |
| Errors | Status codes: 200 for every successful command; 400 malformed request or missing or invalid Idempotency-Key; 401 not authenticated; 403 missing permission (R-SEC-01); 404 unknown resource; 409 concurrent duplicate; 412 stale version; 422 R-API-04 reuse and rule violations; 428 missing If-Match. ruleId is present on 422 and 403 (the rule that failed, R-API-03) and absent on 400, where invalid input breaks no rule; unknown references found inside the transaction are 400s stored as the key's result. A value that breaks a rule tied to master data is a 422 with that rule, for example a quantity with more decimals than its unit allows (R-INV-02, code quantity_precision), which also covers an item's sales multiple and minimum order quantity. Rejections carry problem details (RFC 9457) with `ruleId` and `code` members | R-API-03 |
| Authorisation | The token's subject (sub) is the only claim mapped, after issuer and audience validation; token roles and email are ignored, and the Keycloak realm carries no roles. No user is created on login. Every command and query declares its scope from its own data (company, and site where one applies); a create command carries it in the body, and a command on an existing record is scope-checked after the record is loaded, inside the command's transaction, before any business logic and again on every retry. A command or query without a scope is rejected; installation-wide ones declare installation scope. A site-limited assignment never satisfies a command or query without a site. Assignments are valid from valid_from (inclusive) to valid_to (exclusive), judged on the company's business date. Failures are 403 with ruleId R-SEC-01 and distinct codes for unknown subject, disabled user, missing permission and out of scope; unknown-subject and disabled-user attempts are logged, since no audit event can exist without a user. The system user owns seeded rows and can never act through the API | R-SEC-01, D-47, D-12, R-TIME-01 |
| Audit | One audit event per changed entity, carrying the command id, command name, idempotency key and correlation id. Ledger rows, cache rows and number series counters get none; they are traced through their command (D-51). The command id is the trusted link; the correlation id may come from the caller, is limited in format and length, and is informational (RR-53) | R-AUD-01 |
| Test seams | Code that exists only for development and tests (failure switches, fixture modules, test-only permissions) stays out of Themis.Kernel.Contracts and every other production contract, and has no effect on a production build. The repository keeps one register of these parts, each with its purpose, where it lives and how it is kept out of production | Owner, 2026-10-18; D-26 |
| Events | One outbox row per state change (per document or master data row a command changes), named TYPE plus the command in past tense (ITEM_CREATED) unless the document type registry gives a business name (D-44), schema version 1, for example `INVENTORY_ADJUSTED` version 1, written in the command's transaction. Every event carries the envelope of D-50: aggregate type and id, company (empty only for installation-scope aggregates), occurrence time, command name and correlation id; never the idempotency key or the user | D-44 |
| Observability | W3C trace context; every log line of a command carries command name, correlation id and user. The outbox relay's line per event carries the event's command name and correlation id and no user; its lines about a pass itself carry neither (D-53) | D-25 |
| Database naming | `snake_case`; table names match the spec's entity names, for example `inventory_entry` | Phase 0 spec |
| Settings | Configurable defaults live in settings tables with their documented default, never in code or `appsettings`. The framework is D-58: SettingCatalog in code, values in the insert-only setting_value table, R-SET-01; the idempotency retention became its first setting and D-45 ended with milestone 2 step 2. Data and Planned definitions follow D-63. Host operating options that no company adjusts (for example Outbox:Relay:*, step 9) are not configurable defaults under P-11: they are host configuration in appsettings, with their defaults documented in code | P-11 |
| Secrets | Environment variables or a secret store; never in the repository. Real credentials, secret values and the operational security setup (for example the secret store, keys and certificates) are handled by the owner outside any LLM or agent harness; the coder and the planner build and test against placeholders and never receive real values (owner, 2026-10-17). The security features themselves (authentication, authorisation, R-SEC rules) are built as usual | Foundation, Security section |
| Migrations | Forward-only; a migration that drops or rewrites data needs an owner decision first. A migration's SQL is fixed once it is merged: it never reads code that can change later (grant lists, model constants), and every later change, a grant or a view included, goes in a new migration (RR-39). From step 2b, a migration that must change rows of a table guarded by the D-60 triggers disables and re-enables them inside its own script and needs the owner's review. Reference lists (currencies, units, later countries) are seeded by migrations, each row audited under the system user; system catalogues such as permissions are applied from code by Themis.Migrator on every run | P-05 |
| Review | Every pull request passes the layers of D-59 on its head commit: the required checks with no bypass, an independent review in a separate session against REVIEW.md and the brief, the planner's conformance check, and the owner's squash merge as the sign-off, made only when the reviewer's summary names the head commit being merged. Review is tiered by the files a pull request changes (D-59, amended 2026-10-24): one that changes only snapshots under docs/design, the README or other documentation, or a patch or minor dependency update changing only package versions and lock files, needs the required checks, the planner's conformance check and the owner's merge, with no reviewer session; any other change to CLAUDE.md, REVIEW.md, SECURITY.md, CODEOWNERS, .github, src, db or tests, and every major dependency update, passes all layers. The coder never merges, approves or resolves a reviewer conversation; it answers each one with a fix commit or a reason. The pull request body labels local results as local, ticks CI only when the head commit's checks have passed, and lists every deviation from the brief and every change the brief does not ask for | D-59; RR-90, RR-91 |
| CI runners | Runner images are pinned to a version (ubuntu-24.04), never a moving label such as ubuntu-latest; an upgrade is its own pull request | Owner, 2026-10-22; RR-104 |

## Milestone 1, walking skeleton: scope

Milestone 1 proves the architecture end to end with the smallest real business flow: create an item, post an inventory adjustment, and see one transaction write the document, the inventory ledger, the general ledger, the audit trail and an event (D-27). Build it in this order; each step runs before the next starts.

1. **Repository and pipeline:** solution, pinned SDK, analysers, compose file with PostgreSQL and Keycloak, role bootstrap script (D-41), `Themis.Migrator`, CI with all test suites and the licence scan, health endpoint, OpenTelemetry with a console exporter.
2. **Kernel primitives and command pipeline:** Money, Quantity, BusinessDate, typed ids, Result and Error carrying rule IDs; dispatch with validation, permission check, idempotency, one transaction with role switching, audit events and telemetry.
3. **Organisation fixture:** one installation; the company's own party row (name, organisation number, country); one company (SEK, Europe/Stockholm, tax rounding level Line as a test value until D-10); one site; one warehouse; fiscal year 2026 with 12 periods, period 1 Closed and the rest Open.
4. **Identity:** a Keycloak realm with two test users; User rows mapped to their subjects; roles and role assignments; permissions ITEM.CREATE and INVENTORY_ADJUSTMENT.CREATE, UPDATE, POST and DISCARD, of which the second user lacks POST. Integration tests use a local test token issuer; one smoke test logs in through Keycloak. TYPE.READ permissions for queries (D-47). Segregation of duties: SodConflict seeded with the specification's v1 example pairs and checked when the fixture assigns roles (R-SEC-02). A system user owns seeded rows; the foreign keys from audit and idempotency records to User are added and validated.
5. **Master data in the kernel:** UnitOfMeasure H87 (0 decimals), Currency SEK (2 minor units), ItemPostingGroup TRADE_GOODS. ITEM.CREATE and a GET query. ITEM.CREATE creates, in one command, the installation-wide Item (item number, description, item type Stocked, base unit H87, lot tracking None) and its ItemCompany row for the company (status Active, item posting group, tax class Standard, costing method MovingAverage; D-43). A duplicate item number is rejected with R-MD-05. Creating item posting groups, and the R-PP-05 check that goes with it, come later in Phase 0.
6. **General ledger core:** through the test/dev seeder (audit events, no outbox events), accounts BAS 1460 Lager av handelsvaror (Asset, control) and TEST-ADJ "Test placeholder, inventory adjustment (D-20 open)" (Expense, non-control) in the company's chart, and posting profile rules INVENTORY (TRADE_GOODS) to 1460 and INVENTORY_ADJUSTMENT (TRADE_GOODS, CountDifference) to TEST-ADJ, valid from 2026-01-01. Account roles are a catalogue in code; database constraints generated from it enforce the role list, R-PP-04 (composite foreign key on the account's control flag) and R-PP-06; an exclusion constraint enforces R-MD-02 with empty keys compared as a value. The rule validator (account in the company's chart, Active, R-PP-04, R-PP-06) and the resolver (R-PP-01, R-PP-02, D-19) are built here; the resolver rejects a winning rule whose account is not Active and never falls back. Step 8 calls the resolver.
7. **Document framework:** the document type registry as a code catalogue (INVENTORY_ADJUSTMENT with its four transitions and event names; JOURNAL_ENTRY as a numbering-only row); the document, document_line and inventory_adjustment tables (document_link and attachment wait for Phase 1); the state machine engine; INVENTORY_ADJUSTMENT.CREATE, UPDATE (whole draft, lines matched by line number, none deleted), DISCARD and READ, replacing the fixture stand-in for UPDATE; the numbering service with series rows for INVENTORY_ADJUSTMENT (IA-, continuous) and JOURNAL_ENTRY (JE2026-, per fiscal year, gapless); the event envelope (D-50). The POST transition is registered, but the POST command, its guards and its effects come together in step 8 (R-LED-05).
8. **Posting service:** AdjustmentIn and AdjustmentOut entries. Inbound cost = quantity x entered unit cost, rounded to the currency's minor unit; an inbound line without an entered cost takes quantity x value / quantity on hand while stock is above 0, and the stored average only at zero stock. Outbound cost = quantity x value / quantity on hand, computed exactly and then rounded, never from the stored average (spec, moving average algorithm). Stock balance and cost state caches, journal entry per E-01 and E-02 through the posting profile, locks per R-LED-08. INVENTORY_ADJUSTMENT.POST with the guards of the specification's state machine (a missing number series is rejected with R-DOC-08), replacing the fixture's POST stand-in. One journal entry per document; a zero-cost line writes its inventory entry but no journal lines. Audit events for the document and its lines, with the command id on every audit event (D-51).
9. **Outbox:** INVENTORY_ADJUSTED written in the posting transaction; the relay marks it published and logs it. No external delivery yet. The relay runs in the host in short transactions as themis_kernel, writes only published_at and attempts and no audit event (D-53); its options are host configuration.
10. **Reporting:** one view in the `reporting` schema with stock quantity and value per item and warehouse. Value per warehouse is the allocation of D-54, defined as a versioned KPI in the repository; the view reads the caches.
11. **Tests:** the acceptance criteria below, including property-based invariant tests and the role and privilege test I-14. The invariant checks are written once as reusable queries; the production nightly check that runs them is built before Phase 0 is declared complete.

## Milestone 1: acceptance criteria

Milestone 1 is done when every box below is ticked and CI is green on the main branch. The reference calculations are the known answers the posting tests must reproduce exactly.

**Reference calculations** (company currency SEK, rounding half away from zero to 2 decimals, R-MON-02)

| Step | Item | Movement | Entry quantity | Entry cost (SEK) | Quantity after | Value after (SEK) | Average after |
|---|---|---|---|---|---|---|---|
| 1 | TEST-001 | In 100 at 2.00 | +100 | +200.00 | 100 | 200.00 | 2.00 |
| 2 | TEST-001 | In 100 at 3.00 | +100 | +300.00 | 200 | 500.00 | 2.50 |
| 3 | TEST-001 | Out 50 | -50 | -125.00 | 150 | 375.00 | 2.50 |
| 4 | TEST-001 | Out 150, empties stock | -150 | -375.00 | 0 | 0.00 | 2.50 kept |
| 5 | TEST-001 | Out 1 | Rejected, R-INV-03 |  | 0 | 0.00 | 2.50 |
| 6 | TEST-002 | In 3 at 1.00 | +3 | +3.00 | 3 | 3.00 | 1.00 |
| 7 | TEST-002 | In 3 at 1.01 | +3 | +3.03 | 6 | 6.03 | 1.005 |
| 8 | TEST-002 | Out 1 | -1 | -1.01 (1.005 rounded half away from zero) | 5 | 5.02 | 1.004 |
| 9 | TEST-002 | Out 5, empties stock | -5 | -5.02 | 0 | 0.00 | 1.004 kept |
| 10 | TEST-003 | In 100,000,000 at 0.01 | +100,000,000 | +1,000,000.00 | 100,000,000 | 1,000,000.00 | 0.01 |
| 11 | TEST-003 | In 200,000,000 at 0.02 | +200,000,000 | +4,000,000.00 | 300,000,000 | 5,000,000.00 | 0.0166666667 stored, informational |
| 12 | TEST-003 | Out 150,000,000 | -150,000,000 | -2,500,000.00 from value / quantity; the stored average would wrongly give 2,500,000.01 | 150,000,000 | 2,500,000.00 | 0.0166666667 |
| 13 | TEST-003 | In 150,000,000 without entered cost | +150,000,000 | +2,500,000.00 from value / quantity; the stored average would wrongly give 2,500,000.01 | 300,000,000 | 5,000,000.00 | 0.0166666667 |

Each step is its own INVENTORY_ADJUSTMENT document with reason code CountDifference and posting date 2026-06-15, an Open period. Each posted step writes one balanced journal entry: inbound debits INVENTORY and credits INVENTORY_ADJUSTMENT (E-01); outbound the reverse (E-02).

**Checklist**

- The compose file, `Themis.Migrator` and `dotnet run` start the system with Docker Compose and with Podman Compose; the health endpoint reports healthy.
- CI is green: build with warnings as errors, unit, architecture, integration and invariant tests, licence scan.
- A user without INVENTORY_ADJUSTMENT.POST is rejected with 403 and ruleId R-SEC-01.
- ITEM.CREATE works; a duplicate item number is rejected with R-MD-05.
- Posting an adjustment writes the document status, inventory entries, one balanced journal entry, the audit events and the outbox event in one transaction, and all 13 reference steps match to the last decimal.
- A failure injected after the inventory entries and before commit leaves nothing behind (R-LED-05).
- An adjustment whose lines all have zero cost posts its inventory entries and no journal entry, as I-08 allows.
- A Scrap adjustment with a non-zero cost, which no posting profile rule maps, is rejected with R-PP-02 naming the role, the keys and the date, and nothing is saved (step 6 proves the resolver part, step 8 the end-to-end part); posting dated in period 1, which is Closed, is rejected with R-LED-04.
- Journal entry numbers are gapless per fiscal year, also when a posting fails mid-transaction (R-DOC-07, I-09).
- Replaying a command with the same Idempotency-Key returns the first result and posts nothing twice; a concurrent duplicate gets 409; the same key with a different request is rejected (R-API-01, R-API-04).
- A command with a stale If-Match version is rejected (R-API-02).
- All money, quantity and cost values in JSON responses are strings, and integers such as versions stay numbers (D-37).
- Role and privilege test I-14 passes: the actual grants equal the expected privilege matrix; `themis_fixture` cannot write to `kernel`; `themis_kernel` cannot update or delete journal or inventory entries; `themis_app` cannot write any table without switching role.
- Property-based runs of random adjustment sequences keep I-01, I-02, I-03, I-04, I-06, I-08, I-09 and I-15 true.
- The reporting view returns stock quantity and value per item and warehouse (D-54, I-15 holds), and `themis_reporting` holds no privilege on any `kernel` table.
- An illegal reference from the fixture module into a kernel internal project makes the architecture tests fail.
- Every log line of a command carries the command name and correlation id (relay lines as in the Observability convention), and traces are visible in the console exporter.
- Two adjustments on the same items, posted concurrently with their lines in opposite order, both commit without deadlock (R-LED-08).
- A serialization failure injected on the first attempt is retried once, and the adjustment posts exactly once with one outbox event (D-41).
- Creating an item writes one ITEM_CREATED outbox event, and every other state-changing command in the milestone writes its event (D-44).
- A CI check fails when code, tests, database scripts or build files cite a rule, principle, decision, invariant or event ID that does not exist in docs/design, or when a snapshot lacks its revision stamp (review RR-08).

## Out of scope, and what the coder must not decide alone

Milestone 1 stays small on purpose; everything below waits for the rest of Phase 0 or later, and the second list goes back to the owner.

**Not in milestone 1:** parties and prices; creating item posting groups and the R-PP-05 check; REVERSE and the other kernel documents (manual journal, stock status change); lots; reservations; row-level security; the rules engine; units other than the base unit; multi-currency; external event delivery and webhooks; SCIM provisioning; anything from Phase 1 or later; any user interface.

**Decisions the coder must bring back to the owner:**

- Any change to a principle, rule or decision, including "temporary" exceptions.
- Real account numbers beyond the test fixture (D-20).
- The VAT rounding default (D-10), when invoicing is built.
- Any dependency whose licence is not clearly permissive (D-38).
- Making the repository public or accepting outside contributions (the owner's decision; contributor terms are settled in D-36).
- Any change to the GitHub setup in D-42, such as repository visibility, paid Actions minutes or self-hosted runners.

## Milestone 2: kernel complete

Milestone 2 completes Phase 0 within the scope of D-55, on the skeleton milestone 1 proved. The owner approved this plan on 2026-10-19. Steps are released one at a time with their briefs. Steps 1 and 2 are merged, and so are the two hardening steps that followed them, step 2a (interim fix) and step 2b (database defence in depth); step 3 is next. Named gates (below the plan) settle the decisions each brief depends on before it is drafted (D-67). While steps 1 to 7 are built, the Phase 1 specification is written in parallel under D-57.

1. **Hardening from the code review of 2026-10-18:** the fixes the owner decided in the Remediation Register, RR-39 to RR-53 and RR-56, among them frozen migration SQL with a drift test (RR-39), retry on wrapped database errors (RR-40), a zero-test guard in CI (RR-41), expired idempotency records treated as absent (RR-42), non-overlapping fiscal periods (RR-44), unique-key races as rule rejections (RR-45), value bounds (RR-46), database backstops for stock and journal balance (RR-47) and migrator diagnostics (RR-48).
2. **Settings framework (RR-12):** setting definitions and values with scope levels; the idempotency retention becomes a setting and D-45 ends; the purge of expired idempotency records (RR-42); the document line limit (RR-54); negative stock per warehouse (D-09) defined but locked. Specification section first. Merged 2026-10-22. Followed by step 2a, the interim fix from the kernel review and the review of pull request 42 (the development setup, two 500 answers, CI and document hygiene, the step 2 follow-ups, D-63 and the D-59 review files, amended 2026-10-23: no sign-off workflow), and step 2b, database defence in depth (RR-62 to RR-64, RR-70, RR-71, RR-73, RR-77, RR-81, RR-83, RR-84, D-60), with a test seam for cleaning the test database once the D-60 triggers exist.
3. **Organisation commands:** company, site and warehouse CREATE and UPDATE, the chart of accounts header, and the company's own party with the installation-level PARTY series (D-64, D-65, R-MD-06); the test/dev seeder stays for tests. A real installation can use these commands only once step 13 provides its first administrator; until then they are proven with fixture users. Costing follows the company's rounding mode before COMPANY.UPDATE can change it (RR-74).
4. **Chart of accounts and accounts:** accounts: CREATE, UPDATE and the master data lifecycle, with the amended R-PP-05 (D-56); the chart header comes with step 3. Also the command catalogue in the repository with a CI check that every command declares its permission, If-Match, audit, event and rule IDs (RR-07), and the OpenAPI document generated from code, committed and checked in CI for breaking changes (RR-10).
5. **Item posting groups and posting profile rules:** CREATE and UPDATE with the R-PP-05 completeness check, the same check VALIDATE_POSTING_PROFILE runs (D-32), counting only the roles of posting events whose document types are built (D-67); a rule that names a ship-to scope is rejected until the EU country list exists in Phase 1 (D-55).
6. **Fiscal years and periods:** number series commands for company and installation-level series (a new company's Continuous series, the PARTY series format), CREATE_YEAR, which also creates the year's number series from their templates (D-52; the migration adding prefix_template sets JE{fiscal_year}- on the existing JOURNAL_ENTRY series, a data change the owner approved on 2026-10-19; a company's first year takes the JOURNAL_ENTRY template and padding as arguments and is rejected under R-DOC-08 without them), SOFT_CLOSE, REOPEN, CLOSE, the FISCAL_PERIOD.POST_SOFT_CLOSED permission, VALIDATE_POSTING_PROFILE; postings lock the period against a concurrent close, with a lock order entry (RR-44, RR-67, R-LED-08); a new gapless series is rejected when its prefix overlaps the prefix of another gapless series of the same company and fiscal year, so I-09 counts each entry once (RR-106). CLOSE_YEAR waits for the Phase 2 finance specification.
7. **Master data lifecycle for Item, ItemCompany and Warehouse (WAREHOUSE.UPDATE itself comes with step 3):** UPDATE, BLOCK, UNBLOCK, DEACTIVATE, REACTIVATE, and Company DEACTIVATE and REACTIVATE; postings lock the item's status against a concurrent BLOCK, with a lock order entry (RR-67).
8. **Parties (PartyRole comes with Phase 1, D-55),** numbered from the installation-level party series; personal data in audit events as references to an erasable table, built before step 8's first party command (D-66); the full R-MD-01 duplicate check through its setting; COMPANY.CREATE may link an existing party; PARTY.UPDATE refuses the fields the company commands own on a company's party (D-64).
9. **INVENTORY_ADJUSTMENT REVERSE,** with the amended R-PP-03 (D-56); the posting service contract shape is settled here (RR-55), with a reversal that reuses the original accounts and copies the amounts it carries over without rounding them again, returning outbound lines at their original cost and costing inbound lines by the moving average (D-56 note), an overflow-safe costing order (RR-75) and each posting profile rule resolved once per posting (RR-76).
10. **STOCK_STATUS_CHANGE:** POST and REVERSE, zero-value status entries (R-INV-08).
11. **MANUAL_JOURNAL:** POST and REVERSE, gapless numbers, no control accounts (R-ACC-01); I-06 and I-15 settled for journal lines without the INVENTORY tag (RR-77).
12. **Lots:** lot-tracked items, lots on inventory entries and balances (an empty lot means no lot, as the balance key already allows), BLOCK_LOT and RELEASE_LOT; the backorder effects wait for reservations in Phase 1; Lot's certificate column comes with Attachment in Phase 1 (D-55).
13. **Users and roles administration:** role assignment commands with the segregation of duties check (R-SEC-02, RR-57), four-eyes posting (R-SEC-03, D-62) with the SoD pair grant its commands need (RR-64), an audit trail query, user commands for an identity-provider subject (no credentials in Themis), and the installation bootstrap: the installation record, enforcing its Suspended status, and the first company with its first administrator in one transaction; SCIM 2.0 (D-12) comes before the first production installation (D-67). Followed by step 13a, row-level security on company scope (RR-13, D-28, D-67): how company context enters the transaction, FORCE ROW LEVEL SECURITY, which roles may bypass it, reporting views and BI identities (RR-58), the idempotency DELETE grant (RR-80), and tests for cross-company reads as well as writes.
14. **Nightly invariant check and Phase 0 close:** a separate console runner of the invariant queries with its own read-only database role, exiting with a failure code on any finding; acceptance map, review and tag.

**Gates (D-67).** A brief is drafted only once its gate is passed; the planner checks the remaining steps for dependencies before the step 4 brief and adds what it finds to the gates.

| Gate | Before | What it settles |
|---|---|---|
| A | Step 3 is released | VAT rounding lock (D-65), reversal wording (D-56 note), document consistency fixes from the whole-project review. Passed 2026-10-24 |
| B | Step 5 brief | Posting profile check scope (D-67) and the ship-to scope rejection (D-55) are decided; the dependency check of steps 5 to 7 |
| C | Step 8 brief | Procure-to-pay walk (RR-20) and scenario catalogue (RR-17) written; the personal data field list and erasable table (D-66) |
| D | Step 9 brief | Goods receipt posting events specified (RR-122); backorder allocation mechanism chosen, or confirmed to need no posting service hook (RR-123) |
| E | Step 12 and 13 briefs | User commands and bootstrap design (D-67); step 13a design (RR-13) |
| Phase 1 | Phase 1 specification is frozen | Product vision (RR-37) and volumes (RR-21) from the questionnaire; Phase 1 scope with D-48, output management (G-05) and the file store; tax determination timing and the credit exposure formula (RR-124); landed cost and the GRNI and payables invariants (RR-126); the accountant's answers (RR-25) |
| F | Phase 0 close (step 14) | Phase 0 exit list (RR-127); every register item due before Phase 0 completes is closed |

## CLAUDE.md for the repository

This file goes in the repository root so every Claude Code session starts with the same rules. The design documents are copied into `docs/design/` as dated read-only snapshots, because Claude Code works from the repository; the planning project stays the source of truth.

```markdown
# Themis ERP (working brand: Jozen)

Open-source ERP for companies trading mainly in C-parts and components, such as industrial fasteners. Backend first; the frontend comes in Phase 5.

## Sources of truth (higher wins; report conflicts, never choose)
1. docs/design/themis-foundation-design.md: principles P-xx, rules R-xx (invariant or configurable default), decisions D-xx, phases, build constraints
2. docs/design/themis-phase-0-specification.md: entities, state machines, posting profile, posting events E-xx, invariants I-xx
3. docs/design/themis-build-brief.md: stack, layout, database roles, current milestone
These are dated snapshots of the planning documents. If a snapshot looks wrong or is silent, stop and ask the owner.

## Live planning documents
The live documents are the source of truth; docs/design holds revision-stamped copies. Each snapshot header names the revision it was taken from. If a live document's revision is higher than its snapshot, report it to the owner before relying on the snapshot.
Snapshots are refreshed from the live documents at the revision the planner names in its release line, reading that exact revision even if the live document has moved on. The coder records the revision in each snapshot header and in a pull request labelled docs that lists the revisions read and the IDs added or changed. If the named revision cannot be read, stop and ask the owner.
- Themis Foundation Design: https://claude.ai/code/artifact/51f4caaa-2d53-4cc2-b486-0be0a7366534 (snapshot: docs/design/themis-foundation-design.md)
- Themis Phase 0 Specification: https://claude.ai/code/artifact/237492a8-0f0a-4e9d-a8da-44f65de0a56b (snapshot: docs/design/themis-phase-0-specification.md)
- Themis Build Brief: https://claude.ai/code/artifact/a781c405-11f8-42e1-bf7f-7090dbc9df8b (snapshot: docs/design/themis-build-brief.md)

Reference only (not sources of truth; the documents above win on any conflict):
- Themis Review Remediation Register: https://claude.ai/code/artifact/91610c8f-7465-46a7-9f48-0bf2c5171ede (snapshot: docs/design/themis-review-remediation-register.md; findings RR-xx and the owner's decision on each; accepted items are already in the documents above)
- Themis Capability Coverage Map: https://claude.ai/code/artifact/fb7a2e56-0e18-44ef-980b-846a9be72acd (capability checklist with gaps G-xx; nothing in it is in scope until a D-xx entry puts it there)
- Themis Atlas: https://claude.ai/artifact/5Doff8FWHhWHKrnQtSeu7W (visual project map and system map for orientation; it decides nothing, and build progress itself lives in the README)
- Themis Review and Audit Setup: https://claude.ai/code/artifact/cc9a06bc-0f81-4776-8ef2-dc26e27f7aa7 (how pull requests are reviewed and signed off under D-59)

## Ground rules
- Cite rule IDs: in a comment where a rule is enforced, and in the test name that proves it.
- Rejected commands return problem details with ruleId and code (R-API-03).
- No double or float for money, quantity, cost or rates. C# decimal, PostgreSQL numeric, JSON strings (R-MON-01, D-37).
- Ledger and audit tables are insert-only. Corrections are reversals (R-LED-01, R-LED-06).
- Only the kernel posting service writes inventory and journal entries, in the command's single transaction (R-LED-05, R-LED-07).
- Master data the ledgers reference lives in the kernel (P-02). Process modules start in Phase 1.
- Modules reference only Themis.Kernel.Primitives, Themis.Kernel.Contracts and other modules' Contracts projects (D-26).
- One connection, one transaction per command, SET LOCAL ROLE to the owning role, no statement-level retries; whole-command retry only as D-41 allows (D-41, R-DB-01).
- Every command: permission TYPE.COMMAND, Idempotency-Key, If-Match on changes, audit events, outbox event in the same transaction.
- Only Themis.Migrator applies migrations; the application never calls Migrate().
- Every new table or view gets explicit grants; the privilege matrix test must pass.
- Configurable defaults live in settings tables, never in code (P-11).
- New dependencies must pass the licence scan; no copyleft (D-38).
- Never change a rule or decision in code. Write "Proposed change to R-xx" in the pull request for the owner.
- Every commit carries a Signed-off-by line matching its author (DCO, D-36). Commits you make are authored and signed off as Jozen <a07sebjo@gmail.com>, which the owner certifies; never as an AI identity.
- No model identifiers in anything pushed. Commits and pull requests carry the session link as traceability.
- Pull requests follow D-59, tiered by the files they change: never merge, approve or resolve a reviewer conversation; answer each reviewer comment with a fix commit or a reason.
- In the pull request body, label local test results as local, tick CI only when the head commit's checks have passed, and list every deviation from the brief and every change the brief does not ask for.

## Commands
- Start dependencies: docker compose up -d (or podman compose up -d)
- Migrate: dotnet run --project src/Themis.Migrator
- Build: dotnet build
- All tests: dotnet test
- Run: dotnet run --project src/Themis.Host

## Current milestone
Milestone 2, kernel complete. The step plan is in docs/design/themis-build-brief.md; work only on a step the planner has released, and nothing outside it.
```
