# Local development

## Toolchain

- .NET SDK as pinned in `global.json` (10.0.x, the current LTS release, D-06). `dotnet --version` must print 10.0.1xx.
- Docker (or Podman with the Docker socket) for the local stack and for the integration tests, which start
  PostgreSQL through Testcontainers.
- No other tools: the licence scanner is a local dotnet tool restored by `dotnet tool restore`.

## The local stack

`docker compose up -d` starts:

| Service | Image | Port | Credentials (development only) |
|---|---|---|---|
| PostgreSQL | `postgres:17` | 5432 | superuser `postgres` / `postgres`, database `themis` |
| Keycloak | `quay.io/keycloak/keycloak:26.8` | 8080 | admin console `admin` / `admin`; realm `themis`, client `themis-api` (secret `themis-dev-secret`), user `dev` / `dev` |

On first start PostgreSQL runs `db/init/01-roles.sql`, which creates the Themis roles and the `kernel` schema. The
roles are:

| Role | Login | Purpose |
|---|---|---|
| `themis_owner` | yes, password `themis` | Owns schemas and tables; used by migrations only |
| `themis_app` | yes, password `themis` | The application's connection; no table privileges; `SET LOCAL ROLE` to a module role inside each transaction (R-DB-01, D-41) |
| `themis_kernel` | no | The kernel module's role; privileges in schema `kernel` only |

To reset the database, `docker compose down -v` removes the volume and the next start re-runs the script.
Keycloak uses its own file database in development; the realm is imported from `docker/keycloak/themis-realm.json`.

## Running the host

```bash
dotnet run --project src/Host/Themis.Host
curl -i http://localhost:5080/health/live     # 200 as soon as the process serves requests
curl -i http://localhost:5080/health/ready    # 200 when PostgreSQL answers as themis_app, 503 otherwise
```

Configuration comes from `appsettings.json`, `appsettings.Development.json` and the environment. The connection
string key is `ConnectionStrings:Themis` (environment variable `ConnectionStrings__Themis`). Secrets never go into
the repository; use the environment or `dotnet user-secrets` in development.

Telemetry (D-25) is collected always and exported only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, for example
`http://localhost:4317` for a local collector. Every response carries `X-Correlation-Id`; send one to trace an
integration's own call through the logs.

## Tests

| Category | Project | Needs |
|---|---|---|
| Architecture | `tests/Themis.ArchitectureTests` | nothing; checks ARCH-01 to ARCH-07 and proves each rule against fixtures |
| Unit | `tests/Themis.UnitTests` | nothing; the host starts in memory without a database |
| Integration | `tests/Themis.IntegrationTests` | Docker, or `THEMIS_TEST_CONNECTION` pointing at a PostgreSQL superuser |

```bash
dotnet test --solution Themis.sln -c Release
dotnet test --solution Themis.sln -c Release --filter-trait "Category=Integration" --ignore-exit-code 8
```

`--ignore-exit-code 8` tolerates test projects in which the trait filter matches nothing.

The integration tests apply `db/init/01-roles.sql` to a fresh database and then attempt the writes the role model
must refuse (I-14): the login role writing a table, a module role writing outside its schema, a module role
updating or deleting a table that only grants `SELECT` and `INSERT`, the login role assuming the owner role.

## Linting, formatting and the licence scan

```bash
dotnet format Themis.sln                      # fix
dotnet format Themis.sln --verify-no-changes  # what CI runs
dotnet nuget-license -i Themis.sln -t -a eng/licenses/allowed-licenses.json -mapping eng/licenses/license-url-mappings.json -override eng/licenses/package-overrides.json -o Table
```

Warnings are errors. `float`, `double`, `Half` and the static clock (`DateTime.Now`, `DateTime.UtcNow`) do not
compile (R-MON-01, R-TIME-01); see `eng/BannedSymbols.txt`.
