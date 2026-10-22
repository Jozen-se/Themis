# Local development

## Toolchain

- .NET SDK as pinned in `global.json` (10.0.112 or a later patch of the 10.0.1xx band, D-06).
- Docker or Podman with a compose plugin, for the local stack and for the integration tests, which start PostgreSQL
  through Testcontainers. With Podman, point `DOCKER_HOST` at the Podman socket.
- No other tools: the licence scanner is a local dotnet tool restored by `dotnet tool restore`.

## The local stack

`docker compose up -d` (or `podman compose up -d`) starts:

| Service | Image | Port | Credentials (development only) |
|---|---|---|---|
| PostgreSQL | `postgres:18` | 5432 | superuser `postgres` / `postgres`, database `themis` |
| Keycloak | `quay.io/keycloak/keycloak:26.8` | 8080 | admin console `admin` / `admin`; realm `themis`, client `themis-api` (secret `themis-dev-secret`), user `dev` / `dev` |

On first start PostgreSQL runs `db/bootstrap/01-roles.sql`, which creates the roles and the `kernel` and
`reporting` schemas, revokes every default and installs the `btree_gist` extension, which the no-overlap exclusion
constraints on effective-dated rows need (R-MD-02; first used by `kernel.posting_profile_rule`). A hand-made database
needs the same script run once as a superuser, or the general ledger migration fails on the missing extension. The roles:

| Role | Login | Purpose |
|---|---|---|
| `themis_migrator` | yes, password `themis` | Owns schemas, tables and views; used only by `Themis.Migrator` |
| `themis_app` | yes, password `themis` | The application's connection; no table privileges; `SET LOCAL ROLE` to a module role inside each transaction (R-DB-01, D-41) |
| `themis_kernel` | no | The kernel's role; privileges only as granted explicitly by migrations |
| `themis_reporting` | yes, password `themis` | BI tools; SELECT on `reporting` views only |
| `themis_fixture` | no | Test-only module role, created by the integration tests |

To reset the database, `docker compose down -v` removes the volume and the next start re-runs the bootstrap.
Keycloak uses its own file database in development; the realm is imported from `docker/keycloak/themis-realm.json`.

## Migrator and host

```bash
export THEMIS_MIGRATOR_CONNECTION="Host=localhost;Port=5432;Database=themis;Username=themis_migrator;Password=themis"
dotnet run --project src/Themis.Migrator     # verifies the role, applies the pending kernel migrations, seeds the permission catalogue
dotnet run --project src/Themis.Migrator -- --fixture milestone-1   # also seeds the milestone 1 organisation, identity and master data fixtures (idempotent)
dotnet run --project src/Themis.Host
curl -i http://localhost:5080/health/live     # 200 as soon as the process serves requests
curl -i http://localhost:5080/health/ready    # 200 when PostgreSQL answers as themis_app, 503 otherwise
open http://localhost:5080/scalar             # API reference (development only); the description is at /openapi/v1.json
```

The milestone 1 organisation fixture (step 3 brief; `THEMIS_MIGRATOR_FIXTURE=milestone-1` is the environment form)
runs only in tests and local development, never as part of a production migration. It is one installation
`THEMIS-DEV` (sv-SE), the company's own party `P-000001` (Themis Fictitious Fasteners AB, organisation number
555555-5555, SE), the chart of accounts `BAS` (header only), company `SE01` (SEK, Europe/Stockholm, fiscal year start
month 1, HalfAwayFromZero, tax rounding level Line as a test value until D-10, no payment tolerance), site `MAIN`,
default warehouse `MAIN` and fiscal year 2026 with 12 monthly periods, period 1 Closed and periods 2 to 12 Open. Every
value is test data with fixed identifiers; the rows are written by `themis_migrator` with the system user as
`created_by`, each with one audit event under command name `SYSTEM.SEED_FIXTURE` (R-AUD-01), and a second run
inserts nothing. Its definition lives in `Themis.Kernel.Organisation` (`Milestone1OrganisationFixture`) and the
writer in `Themis.Kernel.Persistence` (`OrganisationFixtureSeeder`). The identity fixture of step 4 (users, roles
and assignments, see Identity below) is seeded with it by `IdentitySeeder`, after the segregation-of-duties check
(R-SEC-02); a fixture that fails the check makes the migrator exit with code 4. The master data fixture of step 5
(the item posting group `TRADE_GOODS`, see Master data below) is seeded after it by `MasterDataFixtureSeeder`, and the
general ledger fixture of step 6 (two accounts and two posting profile rules, see General ledger below) after that by
`GeneralLedgerFixtureSeeder`, behind the posting profile rule check; a fixture that fails it exits with code 5. The
number series fixture of step 7 (two series, see Documents below) follows by `NumberSeriesFixtureSeeder`. Every seeder
writes its audit events with one command id per call (D-51, see Posting below). On every run,
fixture or not, the migrator also writes the document type registry from the catalogue in code (`DocumentTypeSeeder`,
exit code 7 if the catalogue breaks a registry rule), and it refuses with exit code 6 to apply the step 7 migration on
a database whose outbox already holds rows (D-50, see Documents below).

The organisation reads (`IOrganisationReader` in Contracts, implemented in `Themis.Kernel.Organisation`) give the
company, the warehouse with its site and company, the business date of a UTC instant in the company's time zone
(R-TIME-01) and the fiscal period of a business date, rejected with R-LED-04 outside every fiscal year. The company
is also served by `GET /v1/kernel/companies/{id}` (permission `COMPANY.READ`), the first query of the pipeline.

Migrations live in `src/Kernel/Themis.Kernel.Persistence/Migrations` and are generated with the pinned tool:

```bash
dotnet ef migrations add <Name> --project src/Kernel/Themis.Kernel.Persistence --startup-project src/Kernel/Themis.Kernel.Persistence --output-dir Migrations
```

Make the generated classes `internal sealed` (ARCH-07). A migration's SQL is fixed once it is merged (Build Brief,
Migrations; RR-39): every statement it emits is literal text in the migration file, never a call into code that can
change later. Grants are written out per table or view (copy the statements `GrantSql` renders for the new
`KernelPrivilegeMatrix` entries, which stay the expected state the I-14 test compares the database with), constraints
and views are literal SQL, and a later change to a grant, a constraint or a view goes in a new migration. Two tests
keep this honest:

- **Golden scripts** (`db/migrations/golden/<migration>.sql`, `MigrationGoldenScriptTests`): each migration's script,
  generated on its own from the migration before it with `IMigrator.GenerateScript` (not idempotent, no database),
  equals its committed file, with the EF product version and the line endings normalised. A new migration adds its
  file with `THEMIS_GOLDEN_RECORD=1 dotnet test --project tests/Themis.Kernel.Tests --filter-class
  Themis.Kernel.Tests.Migrations.MigrationGoldenScriptTests`; a changed script of a merged migration, also one caused
  by a provider or EF upgrade, needs the owner's review before the file is updated.
- **Drift dumps** (`db/migrations/drift/<tag>.sql`, `MigrationDriftTests`): a schema-only `pg_dump` with ACLs of a
  database built by the tag's own `Themis.Migrator`, followed by the data that run wrote (the rows the migrations
  seed, the system catalogues the migrator applies, the migration history) as INSERT statements, since the head
  migrator's seeders need the system user the migrations create. The audit events of that run are left out: they carry
  no command id, which the audit check constraint refuses on a restore. The test restores it into a bare database,
  runs the head migrator on it and compares the PostgreSQL catalog (tables, columns, constraints, indexes, views,
  functions, triggers, ownership, grants, comments) with a database built from scratch; the one pg_dump round-trip
  artefact, CHECK lists stored with per-element casts after a restore, is normalised before the comparison. When a
  milestone tag is cut, build a database with that tag's migrator (bootstrap first, no fixture) and commit
  `pg_dump --schema-only` followed by `pg_dump --data-only --column-inserts --exclude-table-data=kernel.audit_event`,
  with the psql `\restrict` guard lines removed so the file runs through Npgsql (db/migrations/README.md has the
  commands).

Milestone 2 step 1 adds the hardening migration (`HardeningBackstops`): the no-overlap exclusion constraint on
fiscal periods (RR-44; it stops with a message on a database that already holds overlapping periods), the CHECK that
keeps stock on hand at or above 0 (RR-47, I-03) and the deferred constraint triggers that reject at commit a journal
entry with fewer than two lines or out of balance (RR-47, I-01). The trigger function is SECURITY DEFINER with a fixed
search path and nobody, PUBLIC and its owner included, holds EXECUTE on it; a test asserts that no role holds EXECUTE
on any kernel or reporting function outside extensions. The triggers raise SQLSTATE 23514 with constraint name
`ct_journal_entry_balanced`, which the pipeline neither retries nor maps; they are backstops behind code that checks
first, and a commit they refuse is a 500 with the SQLSTATE in the log, no idempotency record and the key free for the
retry.

The migrator's exit codes:

| Code | Meaning |
|---|---|
| 0 | Up to date, or everything applied and seeded |
| 1 | Usage error: no connection string, or an unknown fixture name |
| 2 | Connected as another role than `themis_migrator` (D-41) |
| 3 | PostgreSQL is not reachable |
| 4 | The identity fixture violates segregation of duties (R-SEC-02) |
| 5 | The general ledger fixture violates the posting profile rules (R-PP-04, R-PP-06, or an account outside the chart or not Active) |
| 6 | The database holds outbox rows written before the D-50 envelope and must be recreated before the step 7 migration |
| 7 | The document type catalogue in code breaks a registry rule |
| 8 | PostgreSQL refused a statement; the SQLSTATE and message are printed (RR-48), for example `P0001` from the overlap check of RR-44 |

A run is not one transaction: each migration applies in its own transaction and each seeder commits on its own, so
a failure leaves the steps before it applied. Every step is safe to re-run: the migrations history skips applied
migrations and the seeders insert only what is missing, so the same command is repeated once the cause is resolved.

The host never applies migrations. Its configuration comes from `appsettings.json`, `appsettings.Development.json`
and the environment; the connection string key is `ConnectionStrings:Themis` (environment variable
`ConnectionStrings__Themis`). Secrets never go into the repository.

Telemetry (D-25): traces print to the console in development (`Telemetry:ConsoleExporter`), and everything is
exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Every response carries `X-Correlation-Id`; send one
to trace an integration's own call through the logs.

## Tests

| Category | Project | Needs |
|---|---|---|
| Architecture | `tests/Themis.Architecture.Tests` | nothing; ARCH-01 to ARCH-08, each proven against fixtures |
| Unit | `tests/Themis.Kernel.Tests` | nothing; the host starts in memory without a database |
| Integration | `tests/Themis.Integration.Tests` | Docker or Podman, or `THEMIS_TEST_CONNECTION` pointing at a PostgreSQL 16+ superuser |
| Invariant | `tests/Themis.Invariants.Tests` and `tests/Themis.Integration.Tests/Invariants` | the primitives and the JSON number contract need nothing; the property-based runs over the real pipeline need PostgreSQL like the integration tests |

```bash
dotnet test
dotnet test --filter-trait "Category=Integration" --ignore-exit-code 8
THEMIS_TEST_CONNECTION="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres" dotnet test --filter-trait "Category=Integration" --ignore-exit-code 8
```

`--ignore-exit-code 8` tolerates test projects in which the trait filter matches nothing.

The integration tests apply the bootstrap to a fresh database, run `Themis.Migrator` with the milestone 1 fixture
against it and create the fixture module's table, then attempt the writes the role model must refuse (I-14): the login role writing a table, a
module role writing outside its schema, a module role updating or deleting where only SELECT and INSERT were granted,
the reporting role touching `kernel`, and the login role assuming the migrator role. The privilege matrix test
compares every table privilege in `kernel`, `reporting` and `fixture` with the declared matrix in
`Themis.Kernel.Persistence` (plus the fixture table's own), so a migration cannot grant more than it declares.

The command pipeline tests start an in-memory host (`tests/Themis.Integration.Tests/Pipeline`) wired like
`Themis.Host`, with the fixture module's two commands on `/v1/fixture/counters`, the kernel's own commands and
queries (the company read, `ITEM.CREATE` and the item reads, the inventory adjustments) and the real identity
capability behind the test token issuer: the tests mint bearer tokens for the fixture's users (see Identity below).
Create shares `ITEM.CREATE` with the real command and touch runs under the test-only permission `FIXTURE.TOUCH` (see
Documents below); the post stand-in of step 4 retired in step 8, whose permission tests run against the real
`INVENTORY_ADJUSTMENT.POST`. A counter belongs to a company and optionally a site, which is where its scope comes from.
The fixture commands take a `failure` member that injects a rejection, an exception, a serialization failure on the
first attempt or a write to a kernel table after the business write, so the tests prove what the pipeline leaves
behind in each case. The test implementations of the kernel's seams (the posting hook's `PostingFailureSwitch` and
`TestPostingHook`, the relay's `TestOutboxRelaySeam`) live in `tests/Themis.Integration.Tests/Pipeline/TestSeams.cs`
and are registered before the kernel's registrations, which keep them; the register of every test-only part is
`docs/development/test-seams.md` (Build Brief, Test seams).

The Keycloak smoke test starts a Keycloak container with the realm export and signs in as the clerk. Without a
container runtime it is skipped; set `THEMIS_TEST_KEYCLOAK` to the base URL of a running Keycloak with the realm
(for example `http://localhost:8080` from the compose stack) to run it against that, or
`THEMIS_TEST_REQUIRE_KEYCLOAK=true` (as CI does) to fail instead of skipping.

## The command pipeline

The order of a command's checks since milestone 2 step 1 (RR-51): 401 without a signed-in subject, 400 without an
`Idempotency-Key`, then inside the transaction the subject's user row (403), the validators (400; pure, run on every
attempt, never stored), the scope (404 for a missing record, 403 `scope_undeclared`) and the authorisation (403),
then the advisory lock and the idempotency record. An expired idempotency record counts as absent: it is deleted in
the command's transaction under the advisory lock and the key runs as new; a 412, a failure or a D-41 retry rolls the
delete back (RR-42; the host's purge removes the rest, see Settings). Inside the handler's savepoint,
a unique violation (23505) of a constraint a capability registered as a `UniqueConstraintRule` becomes that rule's
rejection (in milestone 1 `ux_item_item_number` to R-MD-05 `item_number_duplicate`), and a numeric overflow (22003)
becomes a 422 under R-DOC-01 with code `amount_out_of_range`; both are stored as the key's result, any other database
failure stays a 500 (RR-45, RR-46). The retry check of D-41 reads SQLSTATE 40001 and 40P01 wherever they sit in the
exception chain, so a deadlock Entity Framework wraps in a `DbUpdateException` is retried too (RR-40). Validators
also refuse any quantity, base quantity, entered cost or quantity times entered cost outside the range of its column
with 400 `value_out_of_range` (RR-46). A caller's `X-Correlation-Id` is accepted only as one value of at most 128
ASCII letters, digits, dots, dashes, underscores and colons; anything else is replaced and the replacement logged at
warning, and the command id stays the trusted audit link (RR-53, D-51). A query string key given twice is a 400
`query_parameter_repeated` (RR-56).

Every command goes through `Themis.Kernel.Pipeline` (brief, One command, one transaction):

| Step | What happens | Rules |
|---|---|---|
| Headers | `Idempotency-Key` is required (400 without it); `If-Match` is required on commands that change an existing record (428 without it) and its value becomes the command's `expectedVersion`, overwriting anything the body supplies; route values such as `{id}` are merged into the body and win over it. Every success returns the record's new version as the `ETag` header, ready for the next `If-Match` | R-API-01, R-API-02 |
| Authentication | the bearer token is validated against the configured provider (issuer, audience, signature, lifetime); a request without a valid token is 401. The token's `sub` claim is the only claim read; roles and email in the token are ignored | D-12 |
| Validation | validators run before any statement; their outcome is never stored | R-API-03 |
| Authorisation | inside the transaction, before any business statement and again on every retry: the subject's User row becomes the principal (403 `subject_unknown`, `user_disabled`; no user is created on login), the command names its scope from its own data or from the record it loads (403 `scope_undeclared`), and the principal must hold the permission named by `[Command("TYPE.COMMAND")]` through a role assigned in that company (and site, where one applies) and valid on the company's business date, `valid_from` inclusive to `valid_to` exclusive (403 `permission_missing`, `out_of_scope`). A site-limited assignment never satisfies a command without a site. Every 403 carries `ruleId` R-SEC-01 and none is stored | R-SEC-01, R-TIME-01, D-47 |
| Transaction | one connection as `themis_app`, one transaction; each statement switches with `SET LOCAL ROLE` to the role that owns the table (the kernel stores to `themis_kernel`, a module's own SQL to its role) | D-41, R-DB-01 |
| Duplicate | a transaction-scoped advisory lock on (user, key) is tried, never waited for: a second request while the first runs gets 409 at once | R-API-01 |
| Replay | a stored record with the same request fingerprint (command type, body and If-Match version) is replayed with the header `Idempotency-Replayed: true`; a different fingerprint is rejected with 422 and R-API-04 | R-API-01, R-API-04 |
| Handler | runs inside a savepoint; a rejection rolls back to the savepoint, so the business writes disappear while the rejection itself is stored as the key's result and committed. Not stored: authorisation and validation outcomes, the 409 duplicate, a missing If-Match (428) and a stale version (412), so a retry with the fresh version runs afresh | R-API-03, R-API-02 |
| Success | the buffered audit events (one per changed entity, with user, time, command, key and correlation id) and outbox rows (`TYPE_VERB` in past tense, schema version 1) are written as `themis_kernel`, then the idempotency record, then commit | R-AUD-01, D-44 |
| Retry | only SQLSTATE 40001 and 40P01 restart the whole command, authorisation included, at most twice; the retries are counted in the metric `themis.command.retries` | D-41 |
| Errors | problem details (RFC 9457) with `code`, `ruleId`, `correlationId` and `errors`; an unhandled exception rolls everything back, stores nothing and returns 500 | R-API-03 |
| Queries | `GET` only; a query names its permission with `[Query("TYPE.READ")]` and its scope like a command, is authenticated and authorised the same way, runs in one read-only transaction that is rolled back, and returns its result or problem details; no idempotency, audit or outbox | P-04, R-SEC-01, D-47 |
| Numbers | decimals are JSON strings on output and strings or numbers on input; integers stay numbers; the OpenAPI description declares decimals as `string` with format `decimal`, integers as `integer`, keeps `expectedVersion` out of request schemas and documents the `Idempotency-Key`, `If-Match` and `ETag` headers | D-37, R-API-02 |

Idempotency records expire after 7 days (the specification's default). The owner approved a temporary exception to
P-11: the value stays a constant until the settings framework (review finding RR-12) exists and moves there before
Phase 0 is declared complete. The purge that deletes expired records is later work.

A caller outside HTTP (a background service, a test) dispatches through `ICommandDispatcher` with a `CommandEnvelope`;
when it supplies no correlation id, the pipeline creates one, so audit events always carry it (D-25).

## Identity

Themis never handles passwords (D-12). The host trusts one OpenID Connect provider, configured in section `Identity`
of `appsettings`: `Authority` (the issuer, `http://localhost:8080/realms/themis` for the compose stack), `Audience`
(`themis-api`) and `RequireHttpsMetadata` (false only for the local stack). Tokens are validated by the standard
JWT bearer handler against the provider's discovery document. Without an authority the host starts, serves its
health endpoints and answers 401 to every command and query.

Permissions, roles and assignments live in the ERP (Phase 0 Specification, Security entities):

| Table | Holds |
|---|---|
| `user_account` | a user per OpenID Connect subject (`external_subject`), display name and email (personal data), service account flag, status Active or Disabled. Named `user_account` because `user` is reserved in PostgreSQL |
| `permission` | the command catalogue: one row per command plus one `TYPE.READ` per resource type with a query (D-47), with a risk level (`INVENTORY_ADJUSTMENT.POST` is Sensitive). Seeded by `Themis.Migrator` on every run from `PermissionCatalog` in `Themis.Kernel.Identity`; a unit test keeps catalogue and command types equal in both directions |
| `role`, `role_permission` | named sets of permissions |
| `role_assignment` | a role held by a user in one company, optionally narrowed to one site, valid from `valid_from` (inclusive) to `valid_to` (exclusive) on the company's business date (R-TIME-01). Assignments end by `valid_to`; nothing is deleted |
| `sod_conflict` | the pairs one user may not hold in the same company (R-SEC-02), seeded with the specification's v1 examples; the fixture seeder refuses a fixture that combines a pair |

The system user (`WellKnownUsers.System`, subject `themis:system`) owns every row written outside a command:
migrations and fixture seeds. Audit events and idempotency records reference the user table; the system user can
never act through the API.

The milestone 1 fixture (`--fixture milestone-1`) adds two users, mapped to the users of the Keycloak realm in
`docker/keycloak/themis-realm.json`, both assigned to company SE01 without site restriction:

| User | Password (compose stack only) | Roles | May post | May create items |
|---|---|---|---|---|
| `clerk` | `clerk` | INVENTORY_PREPARER, INVENTORY_POSTER and ITEM_MAINTAINER | yes | yes |
| `preparer` | `preparer` | INVENTORY_PREPARER | no | no |

INVENTORY_PREPARER grants COMPANY.READ, ITEM.READ and INVENTORY_ADJUSTMENT.CREATE, UPDATE, DISCARD and READ;
INVENTORY_POSTER grants INVENTORY_ADJUSTMENT.POST alone; ITEM_MAINTAINER grants ITEM.CREATE and ITEM.READ (step 5
brief, acceptance 6: the preparer without ITEM.CREATE gets 403). The fixture seeder only inserts, so a development
database seeded before ITEM.CREATE left INVENTORY_PREPARER keeps the old row; reset the stack (`docker compose down
-v`) to get the current fixture. To call the API from the compose stack, take a token with the password grant and
send it as a bearer token:

```bash
curl -s -X POST http://localhost:8080/realms/themis/protocol/openid-connect/token \
  -d grant_type=password -d client_id=themis-api -d client_secret=themis-dev-secret \
  -d username=clerk -d password=clerk | jq -r .access_token
```

**Test token issuer.** The integration tests configure `Identity:TestIssuer:Issuer` and
`Identity:TestIssuer:SigningKey` (base64, at least 32 bytes) instead of an authority and mint their own HMAC-signed
tokens, so issuer, audience, signature and lifetime are still validated without a provider running. The host refuses
to start with a test issuer configured outside the Development and Test environments.

## Master data

Step 5 brings the master data entities of the Phase 0 Specification into the kernel (P-02): `unit_of_measure`,
`currency`, `item_posting_group`, `item` and `item_company`. Reference data is seeded by the migration itself, in
every installation, with one audit event per row under the system user and command name `SYSTEM.MIGRATE`
(R-AUD-01): the units `H87` (piece, 0 decimals), `KGM` (kilogram, 3) and `MTR` (metre, 3), and the currencies `SEK`
and `EUR` (2 minor units). The rows carry fixed, name-derived identifiers so that a re-run on a restored database
inserts nothing. The item posting group `TRADE_GOODS` is company configuration, not reference data, so it comes only
with `--fixture milestone-1`, under `SYSTEM.SEED_FIXTURE` like the organisation fixture. References to reference data
are by code and are real, validated foreign keys: `item.base_unit` to `unit_of_measure.code`,
`company.base_currency` to `currency.code`, `item_company.item_posting_group` to `item_posting_group.code`; the
default warehouse of an item in a company must belong to that company (company scope convention). The kernel role
holds SELECT, INSERT and UPDATE on the five tables and never DELETE (R-MD-03).

An item is installation-wide and its company row holds what differs per company (D-43). `ITEM.CREATE`
(`POST /v1/kernel/items`) creates both in one command for the company in the body, which is also the command's scope
(R-SEC-01). The item number is stored uppercase and is unique per installation; a duplicate is 422 with ruleId
`R-MD-05` and code `item_number_duplicate`; a sales multiple or minimum order quantity finer than the base unit's
decimals is 422 with ruleId `R-INV-02` and code `quantity_precision`, the field named in the error's details, the
same rejection `Quantity.Create` gives everywhere (Build Brief, Errors: a value that breaks a rule tied to master
data is a 422 with that rule). An unknown base unit, item posting group or default warehouse, or a warehouse of
another company, is a 400 with its own code (`base_unit_unknown`, `item_posting_group_unknown`,
`default_warehouse_unknown`, `default_warehouse_other_company`) and no ruleId. Found inside the transaction, all of
these are stored as the key's result, so a replay returns the same status (Build Brief, Errors). Shape
errors (`description_required`, `net_weight_kg_negative`, `net_weight_kg_precision` and the like) are refused before
the transaction and are not stored, so a corrected retry with the same key runs. An unknown enum value such as a
costing method other than `MovingAverage` is refused at binding. The command writes one audit event and one outbox
event per created row: `ITEM_CREATED` with the item's id as aggregate id and `ITEM_COMPANY_CREATED` with the company
row's id (D-44). Sales multiple and minimum order quantity are returned at the base unit's decimals (`10` pieces,
`0.250` kilograms) and the net weight without trailing zeros, on create and on read alike, so both return the same
text whatever scale the client sent (D-37, R-INV-02); the columns themselves are `numeric(20,6)`, and a net weight
with more than six decimals is refused (`net_weight_kg_precision`).

The reads `GET /v1/kernel/companies/{companyId}/items/{id}` and
`GET /v1/kernel/companies/{companyId}/items/by-number/{itemNumber}` need `ITEM.READ` in the company of the route
(D-47); the scope is checked before anything is loaded, so a company the user is not assigned to is 403 even for an
item that exists, and an item without a row in that company is 404 `item_not_found`. Default sales and purchase
units, item updates, status changes, attribute templates, parties, prices and accounts are later steps.

## General ledger

Step 6 brings the general ledger core into the kernel: `kernel.account` (number unique per chart, R-MD-05; type,
control flag and status as in the Specification) and `kernel.posting_profile_rule` (a company's mapping of an account
role to an account, keyed by the role's mapping keys, effective-dated). Both are company configuration, so they come
with `--fixture milestone-1` only, each row with one `SYSTEM.SEED_FIXTURE` audit event and no outbox event: in the
BAS chart, `1460` Lager av handelsvaror (Asset, control) and `TEST-ADJ` "Test placeholder, inventory adjustment
(D-20 open)" (Expense, non-control, letters on purpose so it can never be mistaken for a BAS account), and the rules
INVENTORY for `TRADE_GOODS` to 1460 and INVENTORY_ADJUSTMENT for `TRADE_GOODS` with reason `CountDifference` to
TEST-ADJ, both valid from 2026-01-01 open-ended. No other reason code has a rule, so a Scrap adjustment is rejected
with R-PP-02.

The account roles are the Specification's system-defined list, a catalogue in code (`AccountRoles` in
`Themis.Kernel.Contracts`, no table, D-46) with each role's mapping keys and whether it must map to a control
account. The database constraints are generated from it: the `account_role` check lists the roles; R-PP-06 (a rule
sets only the keys of its role) is a check per role; R-PP-04 is the stored column `expects_control` with a composite
foreign key `(account_id, expects_control)` to the account's `(id, is_control)`, so a control role cannot point at a
non-control account and a referenced account cannot flip its control flag. The no-overlap rule (R-MD-02) is an
exclusion constraint over company, role, the five keys and the half-open validity range; an empty key is coalesced to
a sentinel inside the constraint, so two rules that both leave the same key empty overlap, while the stored columns
stay NULL and mean "any value" for resolution. An integration test probes every role against the database, so a role
added to the catalogue without its migration fails CI. `tax_code_id` exists without a foreign key until the TaxCode
table does; the R-PP-06 check keeps it empty for every milestone 1 role.

`PostingProfileValidator` (`Themis.Kernel.Posting`) is the rule check the seeder runs now and the Phase 0 commands
will run: the account belongs to the company's chart, is Active, R-PP-04 and R-PP-06. `IAccountResolver` (contract in
`Themis.Kernel.Contracts`, implemented in `Themis.Kernel.Posting`) resolves an account for a company, role, posting
date and the line's keys on the command's unit of work, exactly as the Specification's algorithm says: candidates
valid on the date whose non-empty keys equal the line's, only the role's keys compared; ranked by item posting group,
then party posting group or ship-to scope, then tax code, then reason code (R-PP-01, D-19); no candidate is a
rejection with R-PP-02 naming the role, the keys and the date; a winner whose account is not Active is R-PP-02 as
well, and resolution never falls back to a less specific rule (D-49). The result carries the account, the role and
the rule id for the journal line's explainability. Step 8 calls it; nothing posts in step 6.

## Documents

Step 7 brings the document framework into the kernel (D-39, D-40): the registry tables `document_type` and
`document_transition`, the shared tables `document` and `document_line`, the INVENTORY_ADJUSTMENT extension
`inventory_adjustment` (one row per document, keyed by the document id, holding `reason_code`) and `number_series`.
`document_link` and `attachment` wait for Phase 1. The registry is a catalogue in code (`DocumentTypes` in
`Themis.Kernel.Contracts`) that `Themis.Migrator` writes to the two tables on every run under `SYSTEM.SEED_CATALOGUE`,
inserting what is missing and aligning what differs, never removing: INVENTORY_ADJUSTMENT (number on post, not
gapless, posts inventory and the general ledger) with the transitions CREATE, UPDATE, DISCARD and POST and their event
names (`INVENTORY_ADJUSTMENT_CREATED`, `_UPDATED`, `_DISCARDED`, and the business name `INVENTORY_ADJUSTED` for POST,
D-44), and JOURNAL_ENTRY as a numbering-only row so that journal entry series can name it. A numbering-only row posts
nothing, names no party role and has no transitions; the catalogue check and the database check
`ck_document_type_numbering_only` both refuse otherwise, and the migrator exits with 7 if the catalogue breaks a
registry rule. The kernel role only reads the registry; it holds SELECT, INSERT and UPDATE on the other tables and
never DELETE (R-DOC-06, R-MD-03).

The state machine engine (`DocumentStateMachine`, `Themis.Kernel.Documents`) allows a command only if
`document_transition` lists it for the document's type and current status, else 422 with ruleId `R-DOC-01` and code
`transition_not_allowed`; UPDATE checks first that the document is a Draft, else 422 `R-DOC-02` `document_not_draft`.
Guards and effects per type stay in the type's handlers. Company scope is enforced by composite foreign keys: the
header's and each line's warehouse `(warehouse_id, company_id)` to the warehouse, the line's `(document_id, company_id)`
to its document, and the line's `(item_id, company_id)` to the item's ItemCompany row (D-43), for which the step 5
unique index on `item_company` became the alternate key `ak_item_company_item_company`.

The commands (permissions `INVENTORY_ADJUSTMENT.CREATE`, `UPDATE`, `DISCARD`; the preparer and the clerk hold them):

- `POST /v1/kernel/inventory-adjustments` creates a Draft for the company in the body (the scope, R-SEC-01): document
  date, optional reason code, header warehouse, external reference and posting date, and lines. The currency is the
  company's base currency, set by the system; `draft_reference` is the document id as text, set once (R-DOC-04). A
  draft may be incomplete: the POST guards (reason code, at least one Open line, items Stocked and Active, stock
  coverage, period open) run at posting (see Posting below).
- `POST /v1/kernel/inventory-adjustments/{id}/update` with `If-Match` carries the whole draft (R-DOC-02, R-API-02).
  Lines are matched by line number: a new number inserts, an existing one updates when its content differs, a line
  left out becomes Cancelled, a cancelled number given again reopens it with the new values; nothing is deleted. The
  document version advances on every UPDATE; a line's version advances when the line changes.
- `POST /v1/kernel/inventory-adjustments/{id}/discard` with `If-Match` moves a Draft to Discarded; the row and its
  lines stay (R-DOC-06). A second DISCARD finds no transition (R-DOC-01).
- `GET /v1/kernel/companies/{companyId}/inventory-adjustments/{id}` (`INVENTORY_ADJUSTMENT.READ`, D-47) returns the
  header, the reason code and the non-Cancelled lines; the ETag is the version. The scope is the company in the route,
  checked before anything is loaded: another company is 403 `out_of_scope`, a missing draft 404
  `inventory_adjustment_not_found`.

A line names an item set up in the company (D-43), a signed quantity in the item's base unit (positive adds stock,
negative removes it), the warehouse (the header's when left out), the stock status (Available, Blocked) and an optional
entered unit cost. Shape errors are 400 before the transaction and not stored: `line_number_not_positive`,
`line_number_duplicate`, `line_item_required`, `line_quantity_zero`, `line_entered_unit_cost_negative`,
`line_entered_unit_cost_precision`, `external_reference_too_long`, `company_required`. Unknown references and rows of
another company are 400s found inside the transaction and stored as the key's result (Build Brief, Errors):
`warehouse_unknown`, `warehouse_other_company`, `line_item_unknown` (no ItemCompany row in the company),
`line_warehouse_unknown`, `line_warehouse_other_company`, `line_warehouse_required`; every line error names the line in
`details.lineNumber`. A quantity finer than the base unit's decimals is 422 `R-INV-02` `quantity_precision`, as for
items. Lots, units other than the base unit, costs and the POST command are out of step 7. Every command writes one
audit event per changed row (the document, each inserted, changed, cancelled or reopened line, the extension when the
reason code changes; R-AUD-01) and one outbox event per document, named by the registry, whose payload carries the id,
the changed fields and the line changes (D-44).

The outbox carries the event envelope of D-50 since this step: `aggregate_type`, `aggregate_id`, `company_id` (empty
only for installation-scope aggregates such as `ITEM_CREATED`), `occurred_at`, `command_name` and `correlation_id`,
never the idempotency key or the user. Publishers name the aggregate type and company (`IOutboxWriter.Publish`); the
pipeline fills the command name and correlation id. The three NOT NULL columns have no default, so the step 7
migration first checks that `kernel.outbox_event` is empty and stops otherwise; `Themis.Migrator` checks the same
before applying anything and exits with 6 and a message telling the owner to recreate the database. Development and
test databases hold nothing worth keeping before milestone 1 is done (D-50), and CI starts empty.

Numbering (`INumberSeriesService`, implemented in `Themis.Kernel.Documents`; R-DOC-07, R-LED-08): every series, gapless
or not, takes its numbers from its locked `number_series` row, never from a database sequence. A command asks for every
number it needs in one call; the service locks the series rows in one `SELECT ... ORDER BY id FOR UPDATE`, primary key
order, as the last lock class of the lock order, formats each number as the prefix plus the sequence padded to at least
`padding` digits (after `IA-999999` comes `IA-1000000`) and advances the rows. A failed transaction returns its numbers
with the rollback. A series the company does not have is refused with `number_series_missing`; which rule a posting
date in a fiscal year without a JOURNAL_ENTRY series breaks is R-DOC-08 (D-52, see Posting below). The database enforces the
Specification's rules: `fiscal_year_id` exactly when the scope is PerFiscalYear and of the series' company, one series
per company, type and fiscal year (`NULLS NOT DISTINCT`), gapless where the type requires it (the stored
`type_requires_gapless` is tied to the registry by a composite foreign key), JOURNAL_ENTRY series always gapless and
PerFiscalYear. `--fixture milestone-1` seeds two series for SE01 under `SYSTEM.SEED_FIXTURE`: INVENTORY_ADJUSTMENT
continuous, prefix `IA-`, padding 6, not gapless (`IA-000001` first), and JOURNAL_ENTRY per fiscal year 2026, prefix
`JE2026-`, padding 6, gapless (`JE2026-000001` first). Posting is the first caller (see Posting below).

The fixture module's touch command no longer stands in for INVENTORY_ADJUSTMENT.UPDATE: the optimistic concurrency
and ETag tests run against the real command, while the touch command keeps the failure injection under the
`themis_fixture` role on the test-only permission `FIXTURE.TOUCH`, which the integration tests' fixture bootstrap
(`FixtureSchema`) seeds with the role `FIXTURE_TESTER` and the clerk's assignment, outside the production catalogue
(owner's decision on step 7). Two decisions
on the step 7 drafts are tested explicitly: a line sent without a warehouse takes the header's warehouse as it is on
that write, and the stored line value is what posting reads; an UPDATE that repeats the stored draft still advances
the version, with one audit event for the document row and one `INVENTORY_ADJUSTMENT_UPDATED` event whose changed
fields hold only the version.

## Posting

Step 8 brings the posting service into the kernel (R-LED-05, R-LED-07): the ledger tables `journal_entry`,
`journal_line` and `inventory_entry`, insert-only for the kernel role (I-11, R-LED-06; the privilege matrix declares
SELECT and INSERT and nothing else), and the caches `item_cost_state` (quantity, inventory value and the informational
average cost per company and item) and `stock_balance` (on hand and reserved per company, item, warehouse, lot and
stock status; the unique key is `NULLS NOT DISTINCT` because `lot_id` is present but NULL in milestone 1, by check).
Company scope is enforced by composite foreign keys as in the document tables (D-43). Ledger rows carry `command_id`,
`recorded_at` and `recorded_by` and get no audit event of their own (D-51); `inventory_entry` references its journal
entry whenever its cost is not 0 (R-INV-06, by check).

`POST /v1/kernel/inventory-adjustments/{id}/post` with `If-Match` (permission `INVENTORY_ADJUSTMENT.POST`, Sensitive;
the clerk and the poster hold it, the preparer does not, R-SEC-01, R-SEC-02) turns a Draft into Posted. The handler
(`PostInventoryAdjustmentHandler`, `Themis.Kernel.Documents`) runs the guards before any lock, in this order and
each with its rule and code: Draft with the POST transition in the registry (`R-DOC-01` `transition_not_allowed`);
a posting date in an Open period (`R-LED-04` `posting_date_required`, `fiscal_period_not_open`; SoftClosed is refused
too, since `FISCAL_PERIOD.POST_SOFT_CLOSED` does not exist in milestone 1); the guards without a rule of their own
under R-DOC-01 with a code naming the guard: `no_open_lines`, `reason_code_required`, `item_not_stocked`,
`item_not_active` (the item's ItemCompany row in the document's company), `lot_required` (lots are outside milestone
1, so a lot-tracked item cannot be posted yet); and `R-INV-05` `entered_unit_cost_on_outbound`. POST uses each line's
stored warehouse and stock status and never re-defaults them from the header. Every rejection is 422 with the rule id,
stored as the key's result (R-API-03); nothing of the posting exists after it, the version included.

Then the lock order (R-LED-08), at Read Committed and without statement-level retries (D-41): class 1, the document
row by its versioned UPDATE, which is the one version increment of the command (R-API-02); the posting service
(`IPostingService` in Contracts, `InventoryPostingService` in `Themis.Kernel.Posting`) takes class 2, the
`item_cost_state` rows of the lines' items, and class 3, the `stock_balance` rows of the lines' balance keys, each
by one ordered `INSERT ... ON CONFLICT DO NOTHING` for the missing rows followed by one `SELECT ... ORDER BY ... FOR
UPDATE` in key order; then one pass over the Open lines in line order carrying the cost state and the stock from
line to line (R-INV-09): the moving average algorithm (`MovingAverageCost`; steps 1, 2, 3 and 6 of the
Specification, R-INV-05, R-INV-07, with `q x value / quantity` computed exactly and rounded once to the currency's
minor unit, R-MON-02; an inbound line without an entered cost needs stock or a stored average, else `R-INV-05`
`entered_unit_cost_required`), the stock check per balance key (`R-INV-03` `stock_insufficient`; Available stock is
limited by its reservations, 0 in milestone 1, R-INV-04), and for every line with a non-zero cost both accounts
through the resolver (INVENTORY by item posting group, INVENTORY_ADJUSTMENT by item posting group and reason code;
R-PP-01, R-PP-02, D-49). The journal entry is built in memory: one per document, two lines per non-zero-cost line with
the debit line first (E-01 inbound: debit INVENTORY, credit INVENTORY_ADJUSTMENT; E-02 outbound: the reverse), each
carrying `account_role`, `posting_rule_id`, `item_id`, `warehouse_id` and `source_line_id`, amounts signed debit
positive, the transaction amount in the company currency; a zero-cost line writes its inventory entry and no journal
lines, and a document whose lines all have zero cost writes no journal entry (I-02, I-08). Last lock class: the
number series in one statement, the document's series always and the JOURNAL_ENTRY series of the posting date's
fiscal year only when a journal entry is written (D-52); a missing series is `R-DOC-08` `number_series_missing`,
naming the type and the fiscal year. The inventory adjustment number comes first, the journal entry number last
(R-DOC-07, I-09; a failed transaction gives both back). Then the writes, in this order: the journal entry with its
lines, the inventory entries (movement types `AdjustmentIn` and `AdjustmentOut`), the cache updates, the document
(Posted, `document_number`, `posted_at`, `posted_by`; the version stays as the lock left it) and the lines
(`cost_amount` signed like the quantity, `unit_cost` as cost over base quantity to 10 decimals, R-DOC-05). Nothing on
a ledger table is updated afterwards. The whole command is one transaction: a failure after the inventory entries
leaves nothing behind, not even the idempotency record (R-LED-05), and a serialization failure on the first attempt
is retried once as a whole (D-41).

Audit (R-AUD-01 as amended by D-51): one audit event for the document row and one per costed line, all with the
command's id; the ledger rows, the caches and the series counters are traced through them. `audit_event.command_id`
arrived with the step 8 migration as a nullable column plus a `CHECK (command_id IS NOT NULL) NOT VALID`, which stays
NOT VALID: the pipeline fills the column on every event, every seeder uses one command id per call, rows written before
the column (among them the `SYSTEM.MIGRATE` rows of the step 4 and 5 data migrations, which run before the column
exists on any database) keep it empty, and no audit row is ever changed. The outbox event is `INVENTORY_ADJUSTED` (the
business name of POST, D-44) with the envelope of D-50, whose payload carries the id, the changed fields of the
document (`status`, `documentNumber`, `postedAt`, `postedBy`, `version`) and per line `unitCost` and `costAmount`
with the change `Posted`.

The posting service's hook (`IPostingHook`, internal to `Themis.Kernel.Posting` since step 9, called after the ledger
writes and before the document update) exists for the failure-injection tests of the Build Brief; `AddKernelPosting`
registers the production hook, which does nothing, and the integration tests register theirs first (see
`docs/development/test-seams.md`). The 13 reference calculations of the Build
Brief run as unit tests of the algorithm (`MovingAverageCostTests`) and end to end through the pipeline
(`InventoryAdjustmentPostingTests`), which also prove the guards, the transaction boundary, idempotency and versions,
the lock order under concurrency (two adjustments on the same items in opposite line order, and two on new items racing
the cache inserts), the audit and outbox convention, the role model on the new tables (I-14) and the invariants I-01,
I-02, I-03, I-04, I-06, I-08 and I-09 after every posting. Not in step 8: REVERSE, lots, reservations, units other
than the base unit, multi-currency and ROUNDING lines, the outbox relay, the reporting view, property-based runs and
the fiscal year commands.

## Outbox relay

Step 9 brings the outbox relay (D-53; Build Brief, step 9): every committed outbox event is picked up, logged and
marked published. Nothing leaves the system yet; delivery is at least once, and the delivery terms for integrations
(ordering per aggregate, claiming with a lease, retries and back-off, dead letters, retention) stay open under D-50
and D-53, so consumers must be idempotent.

The pass (`OutboxRelayPass`, `Themis.Kernel.Messaging`) is one short transaction on its own connection through the
unit of work as `themis_kernel` (R-DB-01). It is not a command: no permission, Idempotency-Key, If-Match, audit event
or outbox event of its own (D-53). It records the age of the oldest unpublished event, claims the unpublished events
with `SELECT ... ORDER BY occurred_at, id LIMIT batch FOR UPDATE SKIP LOCKED` (the partial index on `occurred_at`
where `published_at IS NULL` serves it), so two passes never hold the same event at the same time, logs each event,
sets `published_at` to the pass's UTC time from the injected `TimeProvider` (R-TIME-01) and `attempts` to
`attempts + 1`, the only two columns the kernel role may update, and commits. `attempts` therefore counts committed
passes and is 1 for every published event in step 9; a pass that fails rolls back its updates with it and the next pass
handles the events again, so a crash loop shows in the age of the oldest waiting event, not in `attempts` (counting
each try needs a committed claim with a lease column, which comes with the D-50 terms). The relay never uses the
command pipeline's whole-command retry (D-41 is about commands). Published rows stay; `themis_kernel` has no DELETE on
`outbox_event`, and retention becomes a migrator or operator task when the D-50 terms are settled.

The background loop (`OutboxRelayHostedService`, `Themis.Host`) runs the pass: after a full batch the next pass runs
at once; after a partial or empty batch, or a failed pass, it waits `PollInterval`. Cancellation ends the loop between
passes; a pass in flight rolls back. The host registers the loop only when a connection string is configured. Its
options are host configuration, not settings-table defaults (Build Brief, Settings; P-11 covers what a company
adjusts): `Outbox:Relay:Enabled` (default true), `Outbox:Relay:PollInterval` (default `00:00:01`) and
`Outbox:Relay:BatchSize` (default 100), validated at every host start, enabled or not; an interval or batch size at or
below zero stops the host with the offending key in the message. The integration tests run with the relay disabled and
call the pass directly, so no test waits on the timer.

Log and telemetry (D-25; Build Brief, Observability): one Information line per published event from
`Themis.Kernel.Messaging.OutboxRelayPass` with the D-50 envelope (event id, type, schema version, aggregate type and
id, company, command name, correlation id, `occurred_at`) and the `attempts` being committed, never the payload and no
user; the event's command name and correlation id are also log scopes, so the line sits next to the command's own
lines in a filter. Lines about a pass itself (start, failure, stop) carry neither. Two instruments on the meter
`Themis.Kernel.Messaging`, exported like the pipeline's: the counter `themis.outbox.published`, incremented after the
commit, and the gauge `themis.outbox.oldest_pending_age` in whole seconds (0 when none), recorded at the start of every
pass before claiming, so a failing pass still updates it. The age gauge is what shows a stuck relay.

The pass has a test seam between the claim and the commit (`IOutboxRelaySeam`, see `docs/development/test-seams.md`).
The relay tests (`OutboxRelayTests`) post an adjustment and run one pass, publish every event type of the milestone
oldest first, run two passes concurrently with the first held after its claim, fail a pass after its claim, and show
that a rolled-back command leaves no event; the host tests prove the loop and the option validation. Not in step 9:
external delivery, a broker or webhooks, the D-50 delivery terms, a counter of retried events, LISTEN/NOTIFY wake-up,
the reporting view (step 10) and property-based runs (step 11).

## Settings

Milestone 2 step 2 brings the settings framework (RR-12, P-11; D-58; Phase 0 Specification, Settings entities):
every configurable default of the Foundation's classification list lives in one catalogue, nothing reads a
configurable default from code, and the idempotency retention is the first setting, which ends D-45.

The catalogue (`SettingCatalog`, `Themis.Kernel.Settings`) follows the `PermissionCatalog` pattern: one definition
per configurable default with its key, value type, documented default, validation (min, max, allowed values), the
levels a value may be written at, whether the caller dates its values, the lock, the backing (Setting, Column, Data or
Planned with its phase and the levels that phase adds) and the rule and decision that make it a default. The
architecture test `SettingCatalogCoverageTests` reads the Foundation's list and the specification's milestone 2
definitions from the snapshots and holds the catalogue to them in both directions. `Themis.Migrator` writes the
catalogue to `kernel.setting_definition` on every run through `SettingDefinitionSeeder`, as it writes the document
type registry (D-40): a missing row is inserted, a differing one aligned, each with an audit event under the system
user; a stored value that a tightened range left outside its definition is printed as a warning and never changed,
and a definition that left the catalogue is marked retired and printed, never deleted, because values reference it.
The kernel role only reads the table.

Values live in `kernel.setting_value`, insert-only: SETTING.SET inserts a Set row and SETTING.CLEAR a Cleared row,
each with the slot's next version (a slot is a definition, a level and the company, site or warehouse the level
needs; a check ties the target columns to the level and composite foreign keys keep a site or warehouse inside its
company). The kernel role holds SELECT and INSERT and no UPDATE or DELETE, which the privilege matrix test proves
(R-SET-01, I-14). Both commands are Sensitive and require If-Match with the slot's current version, 0 for a slot
with no rows yet (R-API-02: 412 when stale, 428 when missing); a write that loses a concurrent race on the unique
version index `ux_setting_value_slot_version` gets the same 412 through a `UniqueConstraintRule` of kind
PreconditionFailed (RR-45), and like every 412 it is not stored as the key's result. An Installation value declares
installation scope (RR-52), so a holder of SETTING.SET with a company-wide assignment in any company changes
installation values for every company; access reviews treat SETTING.SET and SETTING.CLEAR as installation-wide. A
company, site or warehouse value declares its company (and site). Rejections: a value of the wrong type or outside
its validation, an unknown key, a target that does not fit the level and a valid_from on a plain definition are 400;
a locked definition, a level the definition does not allow, and SET or CLEAR on a definition backed by a column, data
or a planned feature are 422 under R-SET-01. CLEAR on a slot with no rows is a 400. Each write has one audit event
(entity `SettingValue`) and one outbox event `SETTING_CHANGED` (aggregate type `SETTING`, the company empty for an
Installation value) carrying key, level, target, previous and new value (D-44, D-50). SETTING.READ returns the
effective value at a target with its source (a level, the default or a column) and the current version of each
allowed level at that target.

The reader (`SettingsReader`, `ISettingsReader` in Contracts) runs inside the caller's transaction: for one
definition, level and target, a plain definition takes the row with the highest version; a history-bearing one the
row with the latest valid_from on or before the business date, ties going to the highest version (R-TIME-01, in the
company's time zone; installation values use the requesting company's date, or the UTC date without a company). A
Cleared row means no value at that level. The effective value is the first found at the warehouse, then its site
(completed from the warehouse row when not given), then its company, then installation, then the definition's
default; a level the definition does not allow is skipped; a Column-backed definition reads its entity's column. No
cache across requests in v1 (D-41). The milestone 2 definitions: `idempotency.retention_days` (7, 1 to 90,
installation only; stamped on the record at insert, so a change applies to records written after it),
`document.max_lines` (1,000, 1 to 10,000, installation or company; inventory adjustment CREATE and UPDATE reject more
lines with 400 `line_limit_exceeded`, stored as the key's result), `inventory.negative_stock_allowed` (false, company
or warehouse, locked until a costing rule for negative quantities exists, D-09), and the Column-backed
`company.rounding_mode` and `company.tax_rounding_level`, readable through SETTING.READ with source Column and changed
only by COMPANY.UPDATE (milestone 2 step 3; D-10 stays open). Every other configurable default of the list is a Data
or Planned definition.

The purge of expired idempotency records (RR-42; R-AUD-01 as amended by D-58) is a pass beside the relay
(`IdempotencyPurgePass`, `Themis.Kernel.Messaging`), run by the host's `IdempotencyPurgeHostedService` on a timer
and like the relay outside the command pipeline: one short transaction as `themis_kernel`, which holds DELETE on the
table for exactly this, deleting one bounded batch of records whose expiry has passed, so it never holds a long lock
and never conflicts with the dispatcher's own removal of an expired record under the advisory lock. It writes no
audit event; its Information log line and the counter `themis.idempotency.purged` on the meter
`Themis.Kernel.Idempotency` are the record. After a full batch the next pass runs at once; after a partial or empty
batch, or a failed pass (logged, the next pass retries), the loop waits the interval. The host options are
`Idempotency:Purge:Enabled` (default true), `Idempotency:Purge:Interval` (default `00:05:00`) and
`Idempotency:Purge:BatchSize` (default 1000), validated at every host start, enabled or not. The integration tests
run with the purge disabled and call the pass directly.

Test seams: the definition seam (`ISettingDefinitionSeam`) lets the integration tests add a history-bearing
definition and one writable at every level beside the catalogue, and the write seam (`ISettingWriteSeam`) pauses a
write after its version check for the race test (docs/development/test-seams.md). Not in step 2: unlocking negative
stock, moving the rounding columns, any Planned definition's feature, a settings cache, COMPANY.UPDATE (step 3).

## Reporting

Step 10 brings the first reporting view, `reporting.stock_by_warehouse` (step 10 brief; D-54, I-15), created by a
migration, owned by `themis_migrator` and running with its owner's rights (Build Brief, Database roles), with SELECT
granted to `themis_reporting` and nothing else: it is the first privilege matrix entry for that role, and the role
still holds no privilege on any `kernel` table (I-14). A BI tool logged in as `themis_reporting` reads current stock
quantity and value per company, item and warehouse from it.

One row per company, item and warehouse with stock on hand, summed over stock statuses and lots (`lot_id` is always
empty in milestone 1); rows with `on_hand_total` 0 are left out. The columns, which BI tools bind to: `company_id`,
`company_code`, `item_id`, `item_number`, `item_description`, `warehouse_id`, `warehouse_code`, `unit_code` (the item's
base unit), `on_hand_total`, `on_hand_available` (the Available stock status), `on_hand_blocked`, `reserved` (0 in
milestone 1; Available minus reservations will be a separate column), `stock_value`, `currency_code` (the company's
base currency) and `item_average_cost` (the item's company-level average, the same on every warehouse row). Money and
quantities are `numeric` (R-MON-01). A test compares the columns and types with a declared list, so any change to the
view is a visible test change.

The view reads the caches only, `stock_balance` for quantities and `item_cost_state` for value, written in the posting
transaction and kept equal to the inventory entries by I-03 and I-04; it never reads journal lines or inventory
entries, and the tests compare it with them. Value per warehouse is the allocation of D-54: the item's
`inventory_value` times the warehouse's `on_hand_total` over the item's `quantity_base`, each share rounded down to
the company currency's minor unit, the rounding difference to the warehouse with the largest `on_hand_total`, then
the lowest `warehouse_id`, so the warehouse values sum to `inventory_value` exactly and none is negative (I-15). It is
one query with window functions, the zero filter in `HAVING` and `NULLIF(quantity_base, 0)` in the division. The
definition is the versioned KPI `docs/kpi/stock-value.md` (version 1, owner the role Finance owner); the view's
comment names it. The version lives in the document, not in the view name: a change of meaning gets a new view name
and version 2, added columns do not.

Operations note: until row-level security is added (Build Brief, Database roles), the view returns every company of
the installation, so the `themis_reporting` credentials are installation-wide and are handed out accordingly. Not in
step 10: the star schema, other KPI definitions, values at a past date, row-level security, a read replica.

## Invariant queries and property-based runs

Step 11 writes every checkable invariant once as a query in `db/invariants` (`I-01.sql` to `I-15.sql`, named by ID;
see the README there): each returns the violating rows, takes no parameters and runs as the table owner
`themis_migrator`. The same files serve the tests now and the production nightly check later (built before Phase 0 is
declared complete). `InvariantQueryTests` proves them: every invariant of the Specification has a query, is held by
the privilege tests (I-11, I-14) or is listed with the phase that makes it checkable (I-05 reservations and I-10
document links in Phase 1; I-07 receivables, I-12 open items and I-13 ROUNDING lines in Phase 2; never faked as
passing); every query is empty on the seeded database; where a constraint makes a violation impossible the test shows
the constraint rejecting the row (`ck_journal_line_amount`, `ck_journal_line_txn_sign`, `ck_inventory_entry_quantity`
for I-02, `ck_inventory_entry_journal_entry` for the entry part of I-08, `ck_item_cost_state_value` for the zero-value
part of I-04, `ux_journal_entry_company_number` for I-09 duplicates), and for every other part the table owner plants
a violating row with its parents inside a rolled-back transaction and the query must report it. Two parts of I-15 are
guaranteed by the view's definition and cannot be planted (the warehouse values always sum to the item value because
the remainder goes to one warehouse; a warehouse without stock never has a row); `StockByWarehouseViewTests` proves
them on real postings.

`InvariantPropertyTests` (trait `Category=Invariant`, in the integration test project because it needs the pipeline
host and PostgreSQL) runs random command sequences through the real pipeline over HTTP with CsCheck: ITEM.CREATE and
INVENTORY_ADJUSTMENT CREATE, UPDATE, DISCARD and POST with random items (also unknown ones), signed quantities, entered
costs present or absent, stock statuses, reason codes (Scrap included), posting dates across the Open periods, the
Closed period 1, none and a date outside every fiscal year, two warehouses, stale versions, the clerk and the preparer,
interleaved with relay passes. A rejected command (400, 403, 404, 412, 422) is a valid outcome; a 500 fails the run.
After every command every query in `db/invariants` must return no rows. Every sequence starts from a fresh database
cloned with `CREATE DATABASE ... TEMPLATE` from a template that the test class migrates and seeds once (the milestone
1 fixture plus the property fixture's second warehouse `PROP-W2`); the clone is dropped afterwards. The run is
deterministic from its seed: the seed is the first sequence and every further sequence's seed is drawn from it. A
failing sequence is shrunk by CsCheck to the smallest failing one and the failure names the shrunk sequence's seed
and commands; setting `THEMIS_PROPERTY_SEED` to that seed reproduces it as the first sequence. The concurrent variant
runs two random lists of posted adjustments on the same three items at once: the invariants hold afterwards and no
command retried after a deadlock (SQLSTATE 40P01, R-LED-08); on failure it logs the seed and both lists and does not
shrink. Settings, all environment variables: `THEMIS_PROPERTY_SEQUENCES` (200 by default, the pull request run),
`THEMIS_PROPERTY_SEED`, `THEMIS_PROPERTY_THREADS` (2), `THEMIS_PROPERTY_SHRINK_ITERATIONS` (100). The test output
lists the seed and the outcomes per command and status. The workflow `.github/workflows/invariants-long-run.yml` runs
the same tests with many more sequences on manual start; a nightly schedule comes when the repository goes public.

```bash
THEMIS_PROPERTY_SEQUENCES=20 dotnet test --project tests/Themis.Integration.Tests --filter-trait "Category=Invariant"
THEMIS_PROPERTY_SEED=77n5PqrBEsc1 dotnet test --project tests/Themis.Integration.Tests --filter-trait "Category=Invariant"   # reproduce a reported sequence
```

## Linting, formatting, the licence scan and the DCO check

```bash
dotnet format Themis.sln                      # fix
dotnet format Themis.sln --verify-no-changes  # what CI runs
dotnet nuget-license -i Themis.sln -t -a eng/licenses/allowed-licenses.json -mapping eng/licenses/license-url-mappings.json -override eng/licenses/package-overrides.json -o Table
```

Warnings are errors. `float`, `double`, `Half` and the static clock (`DateTime.Now`, `DateTime.UtcNow`) do not
compile (R-MON-01, R-TIME-01); see `eng/BannedSymbols.txt`.

Every commit carries a `Signed-off-by` line matching its author (Developer Certificate of Origin, D-36; `git commit
-s`). The workflow `.github/workflows/dco.yml` runs `.github/scripts/dco-check.sh` over a pull request's commits and
fails when one lacks it; dependabot[bot], github-actions[bot] and merge commits are exempt, squash commits on `main`
are not checked. `.github/scripts/dco-check-selftest.sh` proves the script on synthetic repositories. See
`CONTRIBUTING.md`.
