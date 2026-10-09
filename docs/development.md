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
`reporting` schemas and revokes every default. The roles:

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
dotnet run --project src/Themis.Migrator     # verifies the role, then applies the pending kernel migrations
dotnet run --project src/Themis.Host
curl -i http://localhost:5080/health/live     # 200 as soon as the process serves requests
curl -i http://localhost:5080/health/ready    # 200 when PostgreSQL answers as themis_app, 503 otherwise
open http://localhost:5080/scalar             # API reference (development only); the description is at /openapi/v1.json
```

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

The integration tests apply the bootstrap to a fresh database, run `Themis.Migrator` against it and create the
fixture module's table, then attempt the writes the role model must refuse (I-14): the login role writing a table, a
module role writing outside its schema, a module role updating or deleting where only SELECT and INSERT were granted,
the reporting role touching `kernel`, and the login role assuming the migrator role. The privilege matrix test
compares every table privilege in `kernel`, `reporting` and `fixture` with the declared matrix in
`Themis.Kernel.Persistence` (plus the fixture table's own), so a migration cannot grant more than it declares.

The command pipeline tests start an in-memory host (`tests/Themis.Integration.Tests/Pipeline`) wired like
`Themis.Host`, with the fixture module's two commands (`FIXTURE_COUNTER.CREATE`, `FIXTURE_COUNTER.TOUCH`) on
`/v1/fixture/counters` and a header-based test identity: `X-Test-User` (a GUID) and `X-Test-Permissions`
(comma-separated permission codes, or `*`). The fixture commands take a `failure` member that injects a rejection,
an exception, a serialization failure on the first attempt or a write to a kernel table after the business write,
so the tests prove what the pipeline leaves behind in each case.

## The command pipeline

Every command goes through `Themis.Kernel.Pipeline` (brief, One command, one transaction):

| Step | What happens | Rules |
|---|---|---|
| Headers | `Idempotency-Key` is required (400 without it); `If-Match` is required on commands that change an existing record (428 without it) and its value becomes the command's `expectedVersion`, overwriting anything the body supplies; route values such as `{id}` are merged into the body and win over it. Every success returns the record's new version as the `ETag` header, ready for the next `If-Match` | R-API-01, R-API-02 |
| Authorisation | the signed-in user needs the permission named by the command's `[Command("TYPE.COMMAND")]` attribute, else 403 with `ruleId` R-SEC-01; validators run before any statement | R-SEC-01 |
| Transaction | one connection as `themis_app`, one transaction; each statement switches with `SET LOCAL ROLE` to the role that owns the table (the kernel stores to `themis_kernel`, a module's own SQL to its role) | D-41, R-DB-01 |
| Duplicate | a transaction-scoped advisory lock on (user, key) is tried, never waited for: a second request while the first runs gets 409 at once | R-API-01 |
| Replay | a stored record with the same request fingerprint (command type, body and If-Match version) is replayed with the header `Idempotency-Replayed: true`; a different fingerprint is rejected with 422 and R-API-04 | R-API-01, R-API-04 |
| Handler | runs inside a savepoint; a rejection rolls back to the savepoint, so the business writes disappear while the rejection itself is stored as the key's result and committed. Not stored: authorisation and validation outcomes, the 409 duplicate, a missing If-Match (428) and a stale version (412), so a retry with the fresh version runs afresh | R-API-03, R-API-02 |
| Success | the buffered audit events (one per changed entity, with user, time, command, key and correlation id) and outbox rows (`TYPE_VERB` in past tense, schema version 1) are written as `themis_kernel`, then the idempotency record, then commit | R-AUD-01, D-44 |
| Retry | only SQLSTATE 40001 and 40P01 restart the whole command, authorisation included, at most twice; the retries are counted in the metric `themis.command.retries` | D-41 |
| Errors | problem details (RFC 9457) with `code`, `ruleId`, `correlationId` and `errors`; an unhandled exception rolls everything back, stores nothing and returns 500 | R-API-03 |
| Numbers | decimals are JSON strings on output and strings or numbers on input; integers stay numbers; the OpenAPI description declares decimals as `string` with format `decimal`, integers as `integer`, keeps `expectedVersion` out of request schemas and documents the `Idempotency-Key`, `If-Match` and `ETag` headers | D-37, R-API-02 |

Idempotency records expire after 7 days (the specification's default). The owner approved a temporary exception to
P-11: the value stays a constant until the settings framework (review finding RR-12) exists and moves there before
Phase 0 is declared complete. The purge that deletes expired records is later work.

A caller outside HTTP (a background service, a test) dispatches through `ICommandDispatcher` with a `CommandEnvelope`;
when it supplies no correlation id, the pipeline creates one, so audit events always carry it (D-25).

## Linting, formatting and the licence scan

```bash
dotnet format Themis.sln                      # fix
dotnet format Themis.sln --verify-no-changes  # what CI runs
dotnet nuget-license -i Themis.sln -t -a eng/licenses/allowed-licenses.json -mapping eng/licenses/license-url-mappings.json -override eng/licenses/package-overrides.json -o Table
```

Warnings are errors. `float`, `double`, `Half` and the static clock (`DateTime.Now`, `DateTime.UtcNow`) do not
compile (R-MON-01, R-TIME-01); see `eng/BannedSymbols.txt`.
