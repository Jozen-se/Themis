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
| PostgreSQL | `postgres:17` | 5432 | superuser `postgres` / `postgres`, database `themis` |
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
`GeneralLedgerFixtureSeeder`, behind the posting profile rule check; a fixture that fails it exits with code 5.

The organisation reads (`IOrganisationReader` in Contracts, implemented in `Themis.Kernel.Organisation`) give the
company, the warehouse with its site and company, the business date of a UTC instant in the company's time zone
(R-TIME-01) and the fiscal period of a business date, rejected with R-LED-04 outside every fiscal year. The company
is also served by `GET /v1/kernel/companies/{id}` (permission `COMPANY.READ`), the first query of the pipeline.

Migrations live in `src/Kernel/Themis.Kernel.Persistence/Migrations` and are generated with the pinned tool:

```bash
dotnet ef migrations add <Name> --project src/Kernel/Themis.Kernel.Persistence --startup-project src/Kernel/Themis.Kernel.Persistence --output-dir Migrations
```

Make the generated classes `internal sealed` (ARCH-07), declare the new tables in `KernelPrivilegeMatrix` and end
`Up()` with the loop over `KernelPrivilegeMatrix.GrantsFor(...)`, as the first migration does. The privilege matrix
test (I-14) fails on any grant the matrix does not declare, and the migrations history table carries no grants at all.

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
| Invariant | `tests/Themis.Invariants.Tests` | nothing; CsCheck properties over the primitives and the JSON number contract |

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
`Themis.Host`, with the fixture module's three commands on `/v1/fixture/counters`, the kernel's own commands and
queries (the company read, `ITEM.CREATE` and the item reads) and the real identity capability
behind the test token issuer: the tests mint bearer tokens for the fixture's users (see Identity below). Until the
real commands exist, the fixture commands stand in for them and take their permissions: create takes `ITEM.CREATE`,
touch `INVENTORY_ADJUSTMENT.UPDATE` and post `INVENTORY_ADJUSTMENT.POST` (step 4 brief). A counter belongs to a
company and optionally a site, which is where its scope comes from. The fixture commands take a `failure` member
that injects a rejection, an exception, a serialization failure on the first attempt or a write to a kernel table
after the business write, so the tests prove what the pipeline leaves behind in each case.

The Keycloak smoke test starts a Keycloak container with the realm export and signs in as the clerk. Without a
container runtime it is skipped; set `THEMIS_TEST_KEYCLOAK` to the base URL of a running Keycloak with the realm
(for example `http://localhost:8080` from the compose stack) to run it against that, or
`THEMIS_TEST_REQUIRE_KEYCLOAK=true` (as CI does) to fail instead of skipping.

## The command pipeline

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
`R-MD-05` and code `item_number_duplicate`. An unknown base unit, item posting group or default warehouse, a warehouse
of another company, or a sales multiple or minimum order quantity finer than the unit's decimals, is a 400 with its
own code (`base_unit_unknown`, `item_posting_group_unknown`, `default_warehouse_unknown`,
`default_warehouse_other_company`, `sales_multiple_precision`, `min_order_qty_precision`) and no ruleId; found inside
the transaction, it is stored as the key's result, so a replay returns the same 400 (Build Brief, Errors). Shape
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

## Linting, formatting and the licence scan

```bash
dotnet format Themis.sln                      # fix
dotnet format Themis.sln --verify-no-changes  # what CI runs
dotnet nuget-license -i Themis.sln -t -a eng/licenses/allowed-licenses.json -mapping eng/licenses/license-url-mappings.json -override eng/licenses/package-overrides.json -o Table
```

Warnings are errors. `float`, `double`, `Half` and the static clock (`DateTime.Now`, `DateTime.UtcNow`) do not
compile (R-MON-01, R-TIME-01); see `eng/BannedSymbols.txt`.
