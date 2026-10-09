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
dotnet run --project src/Themis.Migrator     # verifies the role and the schemas; applies migrations once they exist
dotnet run --project src/Themis.Host
curl -i http://localhost:5080/health/live     # 200 as soon as the process serves requests
curl -i http://localhost:5080/health/ready    # 200 when PostgreSQL answers as themis_app, 503 otherwise
```

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
| Invariant | `tests/Themis.Invariants.Tests` | arrives with milestone 1 step 2 |

```bash
dotnet test
dotnet test --filter-trait "Category=Integration" --ignore-exit-code 8
THEMIS_TEST_CONNECTION="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres" dotnet test --filter-trait "Category=Integration" --ignore-exit-code 8
```

`--ignore-exit-code 8` tolerates test projects in which the trait filter matches nothing.

The integration tests apply the bootstrap to a fresh database, then attempt the writes the role model must refuse
(I-14): the login role writing a table, a module role writing outside its schema, a module role updating or deleting
where only SELECT and INSERT were granted, the reporting role touching `kernel`, and the login role assuming the
migrator role. The privilege matrix test compares every table privilege in `kernel`, `reporting` and `fixture` with
the declared matrix in `Themis.Kernel.Persistence`, so a migration cannot grant more than it declares.

## Linting, formatting and the licence scan

```bash
dotnet format Themis.sln                      # fix
dotnet format Themis.sln --verify-no-changes  # what CI runs
dotnet nuget-license -i Themis.sln -t -a eng/licenses/allowed-licenses.json -mapping eng/licenses/license-url-mappings.json -override eng/licenses/package-overrides.json -o Table
```

Warnings are errors. `float`, `double`, `Half` and the static clock (`DateTime.Now`, `DateTime.UtcNow`) do not
compile (R-MON-01, R-TIME-01); see `eng/BannedSymbols.txt`.
