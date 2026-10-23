# Themis

An open source ERP for distributors of industrial fasteners and components, built backend-first and API-first: a
kernel with organisation, parties, items, documents and immutable ledgers, then purchasing and sales, then finance
through a first financial close. C# on .NET 10, PostgreSQL, a modular monolith with enforced module boundaries.
Apache-2.0.

Status: milestone 2 (kernel complete) step 2b, database defence in depth: a statement runs only under the role that owns it, with the role reset before module code and after every kernel Contracts service and the connection handed to modules only through `ForRoleAsync` (RR-62); triggers that stop even the table owner from changing ledger and audit rows (D-60, RR-63); read-only permission and SoD catalogues (RR-64); fixtures only in Development, Test or Testing (RR-70); 401 before any work (RR-71); route values that win over the body (RR-73); I-09 that skips nothing (RR-77); read-only query transactions (RR-81); overflow mapping in the posting service only (RR-83); and test guards that cannot pass on nothing (RR-84). Steps 1, 2 and 2a are merged. Milestone 1 is complete (tag `milestone-1`, acceptance map in [`docs/milestones/milestone-1.md`](docs/milestones/milestone-1.md)); steps 3 to 14 of milestone 2 are not released yet.

## Documents

The design lives in three documents; the code is built from them and never invents a rule. Snapshots in
[`docs/design/`](docs/design/): the Foundation Design (principles, rules, decision log), the Phase 0 Specification
(entities, state machines, posting profile, invariants) and the Build Brief (stack, layout, database roles, current
milestone). [`CLAUDE.md`](CLAUDE.md) carries the ground rules; [`docs/development.md`](docs/development.md) explains
how to run the stack and the tests.

## Pinned components

Versions are not repeated here, so this page cannot fall behind them (RR-69). Every NuGet package version is held in
one place, [`Directory.Packages.props`](Directory.Packages.props); the other components are pinned where the table
says.

| Component | Where pinned |
|---|---|
| .NET SDK (10.0.1xx band, D-06) | `global.json` |
| PostgreSQL (image `postgres`, D-04) | `compose.yaml`, `tests/Themis.Integration.Tests/PostgresFixture.cs` |
| Keycloak (image `quay.io/keycloak/keycloak`, D-12) | `compose.yaml`, `tests/Themis.Integration.Tests/Identity/KeycloakSmokeTests.cs` |
| Local tools: `dotnet-ef` (migrations) and `nuget-license` (licence scan, D-38) | `.config/dotnet-tools.json` |
| Every NuGet package below | `Directory.Packages.props` |

The packages, by purpose:

- Database: `Npgsql`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`,
  `Microsoft.EntityFrameworkCore.Relational` and `Microsoft.EntityFrameworkCore.Design` (migrations of the kernel data
  context, applied by Themis.Migrator only).
- API: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` (MIT; the API reference page in development) and
  `Microsoft.AspNetCore.Authentication.JwtBearer`.
- Telemetry: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.Console`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore`,
  `OpenTelemetry.Instrumentation.Http` and `OpenTelemetry.Instrumentation.Runtime`.
- Tests: `xunit.v3` on the Microsoft.Testing.Platform runner, `Microsoft.AspNetCore.Mvc.Testing`,
  `Microsoft.AspNetCore.TestHost`, `TngTech.ArchUnitNET` (Apache-2.0), `Testcontainers.PostgreSql`,
  `Testcontainers.Keycloak` and `CsCheck` (Apache-2.0; property-based invariant tests).
- Analysis: `Microsoft.CodeAnalysis.BannedApiAnalyzers`.

An architecture test checks that every package this list names is in `Directory.Packages.props` (RR-69).

Every package version is held in `Directory.Packages.props` and the committed `packages.lock.json` files; CI
restores in locked mode. Dependabot updates the central file and the directly affected lock files only, so the
workflow `.github/workflows/dependabot-lock-files.yml` regenerates the rest on its pull requests and runs CI again
on the result.

## Toolchain notes

- **Architecture tests use ArchUnitNET**, the alternative named in D-26. NetArchTest.Rules was rejected: last
  release May 2021, no licence expression on NuGet. Its maintained fork NetArchTest.eNhancedEdition was checked and
  rejected too: last release June 2025, against ArchUnitNET's August 2026 release under Apache-2.0.
- **Property-based invariant tests use CsCheck** (brief: FsCheck or CsCheck). CsCheck is C# native, Apache-2.0,
  shrinks to the smallest failing case and prints the seed to repeat a run; FsCheck would add an F# runtime dependency
  to a C# code base.

## Quick start

```bash
docker compose up -d                        # or: podman compose up -d
dotnet tool restore
dotnet build
dotnet test
DOTNET_ENVIRONMENT=Development dotnet run --project src/Themis.Migrator -- --fixture milestone-1   # needs THEMIS_MIGRATOR_CONNECTION, see docs/development.md
dotnet run --project src/Themis.Host        # http://localhost:5080/health/ready, API reference at /scalar
```

The compose stack's Keycloak realm (`docker/keycloak/themis-realm.json`) has the API client `themis-api` and the
two test users `clerk` and `preparer` of the milestone 1 fixture; see docs/development.md, Identity, for a token.

## Contributing

Every commit is signed off under the Developer Certificate of Origin (`git commit -s`, D-36); a CI check enforces it
on pull requests. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

Apache License 2.0, see [LICENSE](LICENSE).
