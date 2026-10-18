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
`Themis.Host`, with the fixture module's two commands on `/v1/fixture/counters`, the kernel's own commands and
queries (the company read, `ITEM.CREATE` and the item reads, the inventory adjustments) and the real identity
capability behind the test token issuer: the tests mint bearer tokens for the fixture's users (see Identity below).
Create shares `ITEM.CREATE` with the real command and touch runs under the test-only permission `FIXTURE.TOUCH` (see
Documents below); the post stand-in of step 4 retired in step 8, whose permission tests run against the real
`INVENTORY_ADJUSTMENT.POST`. A counter belongs to a company and optionally a site, which is where its scope comes from.
The fixture commands take a `failure` member that injects a rejection, an exception, a serialization failure on the
first attempt or a write to a kernel table after the business write, so the tests prove what the pipeline leaves
behind in each case. The fixture module also implements the posting service's hook (`IPostingHook`, see Posting
below): `PostingFailureSwitch` names the failure the hook raises after the ledger writes of a posting.

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

The posting service's hook (`IPostingHook` in Contracts, called after the ledger writes and before the document
update) exists for the failure-injection tests of the Build Brief; `Themis.Host` registers the production hook, which
does nothing, and the integration tests register the fixture module's. The 13 reference calculations of the Build
Brief run as unit tests of the algorithm (`MovingAverageCostTests`) and end to end through the pipeline
(`InventoryAdjustmentPostingTests`), which also prove the guards, the transaction boundary, idempotency and versions,
the lock order under concurrency (two adjustments on the same items in opposite line order, and two on new items racing
the cache inserts), the audit and outbox convention, the role model on the new tables (I-14) and the invariants I-01,
I-02, I-03, I-04, I-06, I-08 and I-09 after every posting. Not in step 8: REVERSE, lots, reservations, units other
than the base unit, multi-currency and ROUNDING lines, the outbox relay, the reporting view, property-based runs and
the fiscal year commands.

## Linting, formatting and the licence scan

```bash
dotnet format Themis.sln                      # fix
dotnet format Themis.sln --verify-no-changes  # what CI runs
dotnet nuget-license -i Themis.sln -t -a eng/licenses/allowed-licenses.json -mapping eng/licenses/license-url-mappings.json -override eng/licenses/package-overrides.json -o Table
```

Warnings are errors. `float`, `double`, `Half` and the static clock (`DateTime.Now`, `DateTime.UtcNow`) do not
compile (R-MON-01, R-TIME-01); see `eng/BannedSymbols.txt`.
