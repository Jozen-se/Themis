# CLAUDE.md

Themis is an open source ERP for distribution and manufacturing: a kernel (organisation, parties, items, documents,
immutable ledgers) built backend-first and API-first in C# on .NET with PostgreSQL. This file is the working
agreement for the build session (Claude Code) on this repository. Read it before any change.

## Governing documents

Two live documents govern every line of code. Markdown snapshots live under `docs/design/`; the live documents win
where they differ.

| Document | Live | Snapshot |
|---|---|---|
| Themis Foundation Design: principles, kernel model, the 55 core rules, the decision log | https://claude.ai/artifact/B7yhaqHmFJqQCAo23KSDXq | `docs/design/themis-foundation-design.md` |
| Themis Phase 0 Specification: entity attributes, state machines, posting profile, the 14 invariants | https://claude.ai/artifact/5NwCL8oDM8UmE8tg8Abicz | `docs/design/themis-phase-0-specification.md` |

The build session turns these documents into code and never invents a rule. When the documents do not answer a
question the code needs answered, stop, open a "Rule gap" issue (template in `.github/ISSUE_TEMPLATE/`) with the
question, the rules consulted and the options seen, and report it. The planner session owns the documents; the
owner decides.

## Working model

- The owner steers and reviews. The planner session owns the two documents. The build session builds.
- A task brief has five lines: goal, rule IDs, entities touched, acceptance as invariants and scenarios, out of scope.
  Do not start a task without one (the pull request template repeats the five lines).
- One task is one pull request on a short-lived branch into `main`. Squash merge; the head branch is deleted.
- Commit messages and the pull request title carry the rule IDs they implement, for example
  `Post inventory adjustments through the kernel posting service (R-LED-05, R-LED-07, R-INV-06, E-01, E-02)`.
- CI must be green before review: format, build with warnings as errors, architecture tests, unit tests, integration
  tests against PostgreSQL, licence scan.
- Ledger, inventory and posting code (R-LED, R-INV, R-PP, the posting profile), the database role model under `db/`
  and the design snapshots always get the owner's explicit review (CODEOWNERS).
- No model identifiers, assistant names or session links in anything pushed: not in code, comments, commit messages
  or pull request text.
- Labels: `phase-0`, `phase-1`, `kernel`, `ledger`, `docs`, `blocked`. Put `blocked` on a pull request that waits on
  a rule gap.

## Rule-ID vocabulary

Every behaviour traces to an identifier. Use them in code comments, test names, error codes (R-API-03) and commits.

| Prefix | Meaning | Where defined |
|---|---|---|
| `P-nn` | Design principle; the lower number wins a conflict | Foundation, Design principles |
| `R-<AREA>-nn` | Rule. Areas: `LED` ledger, `INV` inventory, `DOC` documents, `MD` master data, `MON` money, `TAX` tax, `TIME` time, `SEC` security, `AUD` audit, `API` API, `EXT` extensions, `UOM` units, `ACC` accounts, `PER` periods, `PP` posting profile, `OI` open items, `DB` database, `O2C` order-to-cash | Foundation, Core rulesets and Order-to-cash rules |
| `D-nn` | Decision in the decision log, with status Decided, Proposed or Open | Foundation, Decision log |
| `I-nn` | Invariant that must hold after every command; each is an automated test | Phase 0 specification, Invariants |
| `E-nn` | Posting event: the debit and credit roles of one ledger effect | Phase 0 specification, Posting rules per event |
| `ARCH-nn` | Module boundary rule enforced by `tests/Themis.ArchitectureTests` | `ThemisArchitectureRules.cs` |

A rule marked as an invariant in the Foundation changes only through a recorded decision. A configurable default
ships switched on and may be adjusted per company through audited configuration (P-11, D-29).

## Repository layout

```text
src/Kernel/Themis.Kernel.Contracts        public contract surface: commands, queries, events, shared value types
src/Kernel/Themis.Kernel.Domain           kernel entities and domain rules, framework-free, internal
src/Kernel/Themis.Kernel.Application      handlers, command pipeline, kernel posting service (R-LED-07)
src/Kernel/Themis.Kernel.Infrastructure   persistence in schema "kernel", migrations, SET LOCAL ROLE (R-DB-01), outbox
src/Kernel/Themis.Kernel.Api              thin endpoint registration
src/Host/Themis.Host                      composition root, OpenTelemetry baseline (D-25), health endpoints
src/Modules/<Name>/                       business modules from Phase 1, same five layers (see src/Modules/README.md)
tests/Themis.ArchitectureTests            ARCH-01 to ARCH-07 with negative fixtures
tests/Themis.UnitTests                    unit tests (no database)
tests/Themis.IntegrationTests             PostgreSQL via Testcontainers; the role model test for I-14
db/init/01-roles.sql                      database roles, schemas and default privileges (D-41, R-DB-01, R-LED-06)
docker-compose.yml                        PostgreSQL 17 and Keycloak for local development
docs/design/                              snapshots of the governing documents
eng/                                      banned symbols (R-MON-01, R-TIME-01) and the licence allowlist (D-38)
```

Dependency rules (ARCH-01 to ARCH-07): the kernel references no module and not the host; a module references
`Themis.Kernel.Contracts` and other modules' `Contracts` projects only; inside a unit the layers depend downwards
(Api and Infrastructure on Application, Application on Domain, everything on Contracts); Domain and Contracts
reference no web, ORM or database framework; public types exist only in `Contracts` projects.

## Conventions the build must keep

- **Exact decimals.** `decimal` for money, quantities, costs and rates (R-MON-01). `float`, `double` and `Half` do
  not compile anywhere in the solution (banned symbols, `eng/BannedSymbols.txt`). Money always carries a currency.
- **Time.** System time comes from the injected `TimeProvider` as UTC (R-TIME-01); the static clock does not compile.
  Business dates are `DateOnly` values in the company's time zone, supplied by the command.
- **Identifiers.** UUID v7 generated by the application (D-17). Business codes are uppercase, 1 to 40 characters.
- **Commands, not table edits (P-04).** Every change is a named command with validation, authorisation (R-SEC-01),
  idempotency (R-API-01, R-API-04), one transaction (R-LED-05), an audit event (R-AUD-01) and an outbox event.
- **Database access (D-41, R-DB-01).** One connection and one transaction per command. The application connects as
  `themis_app`, which holds no table privileges, and runs `SET LOCAL ROLE themis_<module>` before each module's
  statements. Migrations run as `themis_owner`. Ledger and audit tables receive `SELECT` and `INSERT` only.
- **Append-only ledgers (R-LED-01).** Journal entries and inventory entries are never updated or deleted; a mistake
  is a reversing entry. Posted documents are immutable; corrections are new documents (P-03).
- **Persistence (D-26).** One EF Core data context per module, its own schema, its own migrations history table.
  Foreign keys from a module to kernel tables are allowed; between two modules they are forbidden.
- **API (D-37).** Money and quantities travel as JSON strings; OpenAPI declares them as `string` with format
  `decimal`; inbound accepts strings and numbers.
- **Errors (R-API-03).** A rejected command returns a stable error code and the ID of the rule that failed.
- **Rules engine (D-35).** Guards, validation, defaulting and pricing conditions use the expression engine behind one
  interface; account and tax determination stay decision tables (R-PP-01, D-19). Not yet built.
- **Configuration over customisation (P-07, R-EXT-01).** Core tables and code are never changed per customer.

## Commands

```bash
# Toolchain: .NET SDK from global.json (10.0.x), Docker for the integration tests and the local stack.
dotnet tool restore
dotnet restore Themis.sln --locked-mode          # packages.lock.json is committed; CI restores in locked mode
dotnet format Themis.sln --verify-no-changes     # lint; run `dotnet format Themis.sln` to fix
dotnet build Themis.sln -c Release               # warnings are errors

dotnet test --solution Themis.sln -c Release --no-build --filter-trait "Category=Architecture" --ignore-exit-code 8
dotnet test --solution Themis.sln -c Release --no-build --filter-trait "Category=Unit" --ignore-exit-code 8
dotnet test --solution Themis.sln -c Release --no-build --filter-trait "Category=Integration" --ignore-exit-code 8
dotnet test --solution Themis.sln -c Release --no-build      # everything

# Integration tests without Docker: point them at a PostgreSQL superuser; they create a throw-away database.
THEMIS_TEST_CONNECTION="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres" \
  dotnet test --solution Themis.sln -c Release --no-build --filter-trait "Category=Integration" --ignore-exit-code 8

# Licence scan (D-38), the same command CI runs
dotnet nuget-license -i Themis.sln -t -a eng/licenses/allowed-licenses.json \
  -mapping eng/licenses/license-url-mappings.json -override eng/licenses/package-overrides.json -o Table

# Local stack and host
docker compose up -d                              # PostgreSQL on 5432 (db themis), Keycloak on 8080 (admin/admin)
dotnet run --project src/Host/Themis.Host         # http://localhost:5080/health/live and /health/ready
```

Adding a package: put the version in `Directory.Packages.props`, reference it without a version in the project,
run `dotnet restore Themis.sln` to update the lock files, run the licence scan. Never edit `packages.lock.json` by
hand.

## Definition of done for a pull request

1. The five-line brief is in the description and every line is satisfied.
2. Every new behaviour cites its rule ID in code and in a test; invariant tests (I-nn) cover every ledger or stock
   effect touched.
3. CI is green. No test was skipped, disabled or weakened to get there.
4. Ledger, inventory and posting changes are marked for the owner's review.
5. No rule was invented. Open questions are "Rule gap" issues, and the pull request carries the `blocked` label
   while one is open.
