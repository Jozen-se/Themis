<!-- SNAPSHOT. Do not edit by hand. Regenerate from the live document. -->

> **Snapshot** of the live document *Themis Build Brief* (https://claude.ai/artifact/MghYVSVm65mLxjfuBUGvSe), revision 22, taken 2026-10-09.
> The live document is authoritative; this copy exists so that code review and offline reading can cite it without
> leaving the repository. Where this copy and the live document differ, the live document wins.

# Themis Build Brief

2026-10-09 · Sebastian Hansson

This brief tells Claude Code how to start building Themis: the stack, the repository layout, the database role model and the first milestone, a walking skeleton that posts an inventory adjustment to both ledgers in one transaction. It adds no new business rules; those live in the Themis Foundation Design and the Themis Phase 0 Specification.

## Sources of truth and order of authority

When two sources disagree, the higher one wins and the coder reports the conflict instead of choosing.

1. **Themis Foundation Design:** principles P-01 to P-11, the 55 core rules and 9 order-to-cash rules (R-xx) with their invariant or default classification, the decision log (D-xx), phases and the build constraints for the scaffold.
2. **Themis Phase 0 Specification:** entity attributes, state machines, posting profile, posting events (E-xx) and invariants (I-xx).
3. **This brief:** stack, layout, database model and milestone scope.
4. **Code and tests.** They implement the documents; they never redefine them.

The Architecture Recommendations doc is background reading only. Where it differs from the foundation doc, the foundation doc wins (for example on the general ledger posting path, D-24).

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
| Database | PostgreSQL, current major version | D-04 | Pin the image tag in the compose file |
| Persistence | EF Core with Npgsql; one data context per module; raw SQL allowed for ledger reads | D-06, D-26 |  |
| Identity | OpenID Connect through the standard ASP.NET Core handler; Keycloak in the compose file | D-12 |  |
| Rules engine | NCalc in decimal mode behind one engine interface; DynamicExpresso tested against the same exit conditions | D-35 | Exit conditions in Technology direction; not needed until after milestone 1 |
| Tests | xUnit; Testcontainers for PostgreSQL; FsCheck or CsCheck for property-based invariant tests | D-06 | Licences pass the D-38 scan |
| Architecture tests | ArchUnitNET (TngTech.ArchUnitNET 0.13.4, Apache-2.0), pinned through central package management and the lock file | D-26 | Done 2026-10-09: NetArchTest.Rules (last release May 2021) and NetArchTest.eNhancedEdition (June 2025) checked and rejected |
| Observability | OpenTelemetry for logs, traces and metrics; health endpoints | D-25 |  |
| Licence scan | A NuGet licence scanner in CI that fails on copyleft | D-38 | Pick the tool |
| Local runtime | One compose file that runs with Docker Compose and Podman Compose | D-27 | Runs on the owner's Linux machine with Podman |
| CI | Build, unit, architecture, integration and invariant tests, licence scan, on every push | D-26, D-38 | GitHub repository with GitHub Actions workflows; private until D-14 and D-36 are settled (D-42) |

## Repository layout

Milestone 1 is kernel only. Master data that the ledgers reference (items, units, posting groups, currencies, and later parties and prices) belongs to the kernel, as the foundation's kernel domain model and P-02 say. Process modules start in Phase 1 and all follow one five-project template; until then a test-only fixture module proves the boundary rules.

```text
themis/
  CLAUDE.md                        instructions for Claude Code (last section of this brief)
  README.md                        how to run, pinned versions
  LICENSE                          Apache-2.0 (D-07)
  global.json                      pinned .NET SDK
  Directory.Build.props            nullable reference types on, warnings as errors, analysers
  Themis.sln
  compose.yaml                     PostgreSQL and Keycloak; Docker Compose or Podman Compose
  db/bootstrap/                    login roles, module roles, schemas, revokes (D-41); run once per database
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
      Themis.Kernel.MasterData/    items, units, item posting groups, currencies (parties and prices later in Phase 0)
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

**Module template** (every process module from Phase 1): `Contracts`, `Domain`, `Application`, `Infrastructure` (its own data context, schema and migrations), `Api`. Only `Contracts` may be referenced from outside the module.

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
- No default privileges: every table and view gets its grants explicitly in the migration that creates it. A catalogue test compares the actual privileges with an expected privilege matrix and fails on anything extra or missing (part of I-14), so a new ledger table can never silently receive UPDATE.
- Business numbers come from number series rows, not database sequences. If a migration adds a sequence, it grants USAGE explicitly.
- No REFERENCES grants are needed: foreign keys are created by `themis_migrator`, which owns both tables.
- Reporting views run with their owner's rights in milestone 1. How reporting views respect row-level security is decided when row-level security is added.

**One command, one transaction:**

1. The pipeline opens one Npgsql connection as `themis_app` and begins the transaction.
2. The kernel data context, and in later phases any module contexts, are built on that same connection with `UseTransaction`. No retrying execution strategy: a retry would replay half a command.
3. Every statement, raw SQL included, goes through the unit of work. A command interceptor issues `SET LOCAL ROLE` to the owning role whenever the current role differs. Nothing opens a second connection.
4. The posting service takes its locks in one SQL statement ordered in PostgreSQL (`ORDER BY item_id ... FOR UPDATE`), never by .NET `Guid` ordering, which differs. The first posting of an item inserts its cost state row with `ON CONFLICT DO NOTHING` before locking. The number series row is locked and the number taken last (R-LED-08).
5. Commit. `SET LOCAL` ends with the transaction, so pooled connections return clean.

The outbox relay is a background service with its own short transactions, running as `themis_kernel`.

**Migrations:** one data context for the whole kernel schema, because kernel tables reference each other. Module contexts map the kernel tables they read with `ExcludeFromMigrations()`. Each schema has its own migrations history table. Order: kernel first, then modules. Only `Themis.Migrator`, with the `themis_migrator` connection string, applies migrations; the application never calls `Migrate()`. Foreign keys between two process modules are forbidden and the architecture test fails on them.

**What this protects, and what it does not.** It stops accidental cross-module writes and any update or delete of ledger rows, even by kernel code. It does not stop deliberately malicious code inside the application process, because `themis_app` may switch to any module role. The later option of ledger inserts only through a security-definer function closes part of that gap (D-41).

Row-level security on company scope (D-28) is added later in Phase 0, after milestone 1; every company-owned table carries `company_id` from the start.

## Cross-cutting conventions

These apply to every module from the first commit, so that later modules look the same as the first one.

| Topic | Convention | Source |
|---|---|---|
| Identifiers | UUID v7 generated in the application (`Guid.CreateVersion7()`), stored as `uuid` | D-17 |
| Money and quantity | `decimal` in C#, `numeric` in PostgreSQL with the precisions in the spec's Conventions section; `Money` always carries its currency | R-MON-01 |
| JSON numbers | A decimal-only converter writes decimals as strings (the global number-handling option would turn integers into strings too); an OpenAPI schema transformer declares them as string with a decimal format; inbound accepts strings and numbers | D-37 |
| Time | `timestamptz` in UTC for system time; `DateOnly` and `date` for business dates in the company's time zone; business time comes from an injected clock so tests use fixed dates | R-TIME-01 |
| Commands | Create: `POST /v1/{area}/{resource}`. Every other command: `POST /v1/{area}/{resource}/{id}/{command}`, for example `POST /v1/kernel/inventory-adjustments/{id}/post`. The permission code is TYPE.COMMAND, for example `ITEM.CREATE` or `INVENTORY_ADJUSTMENT.POST` | P-04, R-SEC-01 |
| Queries | `GET` only; queries never change state | P-04 |
| Idempotency | `Idempotency-Key` header required on every command. Completed results, rule rejections included, are stored and replayed. A second request with the same key while the first is still running gets 409 Conflict. The same key with a different body is rejected | R-API-01, R-API-04 |
| Concurrency | `If-Match` carrying the record version on every command that changes an existing record | R-API-02 |
| Errors | Problem details (RFC 9457) with `ruleId` and `code` members | R-API-03 |
| Audit | One audit event per changed entity, carrying the command name, idempotency key and correlation id | R-AUD-01 |
| Events | Outbox rows named in upper snake case with a schema version, for example `INVENTORY_ADJUSTED` version 1, written in the command's transaction | Foundation, Integration section |
| Observability | W3C trace context; every log line carries command name, correlation id and user | D-25 |
| Database naming | `snake_case`; table names match the spec's entity names, for example `inventory_entry` | Phase 0 spec |
| Settings | Configurable defaults live in settings tables with their documented default, never in code or `appsettings` | P-11 |
| Secrets | Environment variables or a secret store; never in the repository | Foundation, Security section |
| Migrations | Forward-only; a migration that drops or rewrites data needs an owner decision first | P-05 |

## Milestone 1, walking skeleton: scope

Milestone 1 proves the architecture end to end with the smallest real business flow: create an item, post an inventory adjustment, and see one transaction write the document, the inventory ledger, the general ledger, the audit trail and an event (D-27). Build it in this order; each step runs before the next starts.

1. **Repository and pipeline:** solution, pinned SDK, analysers, compose file with PostgreSQL and Keycloak, role bootstrap script (D-41), `Themis.Migrator`, CI with all test suites and the licence scan, health endpoint, OpenTelemetry with a console exporter.
2. **Kernel primitives and command pipeline:** Money, Quantity, BusinessDate, typed ids, Result and Error carrying rule IDs; dispatch with validation, permission check, idempotency, one transaction with role switching, audit events and telemetry.
3. **Organisation fixture:** one installation; the company's own party row (name, organisation number, country); one company (SEK, Europe/Stockholm, tax rounding level Line as a test value until D-10); one site; one warehouse; fiscal year 2026 with 12 periods, period 1 Closed and the rest Open.
4. **Identity:** a Keycloak realm with two test users; User rows mapped to their subjects; roles and role assignments; permissions ITEM.CREATE and INVENTORY_ADJUSTMENT.CREATE, UPDATE, POST and DISCARD, of which the second user lacks POST. Integration tests use a local test token issuer; one smoke test logs in through Keycloak.
5. **Master data in the kernel:** UnitOfMeasure H87 (0 decimals), Currency SEK (2 minor units), ItemPostingGroup TRADE_GOODS. ITEM.CREATE and a GET query with item number, description, item type Stocked, base unit H87, item posting group, tax class Standard, lot tracking None, costing method MovingAverage, status Active. Creating item posting groups, and the R-PP-05 check that goes with it, come later in Phase 0.
6. **General ledger core:** chart of accounts fixture with BAS 1460 Lager av handelsvaror as a control account, and a non-control placeholder account clearly named as a test account for INVENTORY_ADJUSTMENT until D-20; posting profile rows for both roles.
7. **Document framework:** registry row for INVENTORY_ADJUSTMENT (numbered on post, not gapless); number series rows for INVENTORY_ADJUSTMENT and for JOURNAL_ENTRY (per fiscal year, gapless); shared document tables; state machine engine for CREATE, UPDATE, POST and DISCARD.
8. **Posting service:** AdjustmentIn and AdjustmentOut entries. Inbound cost = quantity x entered unit cost, rounded to the currency's minor unit. Outbound cost = quantity x value / quantity on hand, computed exactly and then rounded, never from the stored average (spec, moving average algorithm). Stock balance and cost state caches, journal entry per E-01 and E-02 through the posting profile, locks per R-LED-08.
9. **Outbox:** INVENTORY_ADJUSTED written in the posting transaction; the relay marks it published and logs it. No external delivery yet.
10. **Reporting:** one view in the `reporting` schema with stock quantity and value per item and warehouse.
11. **Tests:** the acceptance criteria below, including property-based invariant tests and the role and privilege test I-14.

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

Each step is its own INVENTORY_ADJUSTMENT document with reason code CountDifference and posting date 2026-06-15, an Open period. Each posted step writes one balanced journal entry: inbound debits INVENTORY and credits INVENTORY_ADJUSTMENT (E-01); outbound the reverse (E-02).

**Checklist**

- The compose file, `Themis.Migrator` and `dotnet run` start the system with Docker Compose and with Podman Compose; the health endpoint reports healthy.
- CI is green: build with warnings as errors, unit, architecture, integration and invariant tests, licence scan.
- A user without INVENTORY_ADJUSTMENT.POST is rejected with 403 and ruleId R-SEC-01.
- ITEM.CREATE works; a duplicate item number is rejected with its rule ID.
- Posting an adjustment writes the document status, inventory entries, one balanced journal entry, the audit events and the outbox event in one transaction, and all 12 reference steps match to the last decimal.
- A failure injected after the inventory entries and before commit leaves nothing behind (R-LED-05).
- An adjustment whose lines all have zero cost posts its inventory entries and no journal entry, as I-08 allows.
- Posting with a missing posting profile row is rejected with R-PP-02; posting dated in period 1, which is Closed, is rejected with R-LED-04.
- Journal entry numbers are gapless per fiscal year, also when a posting fails mid-transaction (R-DOC-07, I-09).
- Replaying a command with the same Idempotency-Key returns the first result and posts nothing twice; a concurrent duplicate gets 409; the same key with a different body is rejected (R-API-01, R-API-04).
- A command with a stale If-Match version is rejected (R-API-02).
- All money, quantity and cost values in JSON responses are strings, and integers such as versions stay numbers (D-37).
- Role and privilege test I-14 passes: the actual grants equal the expected privilege matrix; `themis_fixture` cannot write to `kernel`; `themis_kernel` cannot update or delete journal or inventory entries; `themis_app` cannot write any table without switching role.
- Property-based runs of random adjustment sequences keep I-01, I-02, I-03, I-04, I-06, I-08 and I-09 true.
- The reporting view returns stock quantity and value per item and warehouse, and `themis_reporting` holds no privilege on any `kernel` table.
- An illegal reference from the fixture module into a kernel internal project makes the architecture tests fail.
- Every log line carries the command name and correlation id, and traces are visible in the console exporter.

## Out of scope, and what the coder must not decide alone

Milestone 1 stays small on purpose; everything below waits for the rest of Phase 0 or later, and the second list goes back to the owner.

**Not in milestone 1:** parties and prices; creating item posting groups and the R-PP-05 check; REVERSE and the other kernel documents (manual journal, stock status change); lots; reservations; row-level security; the rules engine; units other than the base unit; multi-currency; external event delivery and webhooks; SCIM provisioning; anything from Phase 1 or later; any user interface.

**Decisions the coder must bring back to the owner:**

- Any change to a principle, rule or decision, including "temporary" exceptions.
- Real account numbers beyond the test fixture (D-20).
- The VAT rounding default (D-10), when invoicing is built.
- Any dependency whose licence is not clearly permissive (D-38).
- Making the repository public or accepting outside contributions (D-14 trademark check, D-36 contributor terms).
- Any change to the GitHub setup in D-42, such as repository visibility, paid Actions minutes or self-hosted runners.

## CLAUDE.md for the repository

This file goes in the repository root so every Claude Code session starts with the same rules. The design documents are copied into `docs/design/` as dated read-only snapshots, because Claude Code works from the repository; the planning project stays the source of truth.

```markdown
# Themis ERP (working brand: Jozen)

Open-source ERP for distributors of industrial fasteners and components. Backend first; the frontend comes in Phase 5.

## Sources of truth (higher wins; report conflicts, never choose)
1. docs/design/themis-foundation-design.md: principles P-xx, rules R-xx (invariant or configurable default), decisions D-xx, phases, build constraints
2. docs/design/themis-phase-0-specification.md: entities, state machines, posting profile, posting events E-xx, invariants I-xx
3. docs/design/themis-build-brief.md: stack, layout, database roles, current milestone
These are dated snapshots of the planning documents. If a snapshot looks wrong or is silent, stop and ask the owner.

## Ground rules
- Cite rule IDs: in a comment where a rule is enforced, and in the test name that proves it.
- Rejected commands return problem details with ruleId and code (R-API-03).
- No double or float for money, quantity, cost or rates. C# decimal, PostgreSQL numeric, JSON strings (R-MON-01, D-37).
- Ledger and audit tables are insert-only. Corrections are reversals (R-LED-01, R-LED-06).
- Only the kernel posting service writes inventory and journal entries, in the command's single transaction (R-LED-05, R-LED-07).
- Master data the ledgers reference lives in the kernel (P-02). Process modules start in Phase 1.
- Modules reference only Themis.Kernel.Primitives, Themis.Kernel.Contracts and other modules' Contracts projects (D-26).
- One connection, one transaction per command, SET LOCAL ROLE to the owning role, no retrying execution strategy (D-41, R-DB-01).
- Every command: permission TYPE.COMMAND, Idempotency-Key, If-Match on changes, audit events, outbox event in the same transaction.
- Only Themis.Migrator applies migrations; the application never calls Migrate().
- Every new table or view gets explicit grants; the privilege matrix test must pass.
- Configurable defaults live in settings tables, never in code (P-11).
- New dependencies must pass the licence scan; no copyleft (D-38).
- Never change a rule or decision in code. Write "Proposed change to R-xx" in the pull request for the owner.

## Commands
- Start dependencies: docker compose up -d (or podman compose up -d)
- Migrate: dotnet run --project src/Themis.Migrator
- Build: dotnet build
- All tests: dotnet test
- Run: dotnet run --project src/Themis.Host

## Current milestone
Milestone 1, walking skeleton. Scope and acceptance criteria are in docs/design/themis-build-brief.md. Do not start work outside that scope.
```
