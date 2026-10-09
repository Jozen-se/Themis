# Themis

An open source ERP for distributors of industrial fasteners and components, built backend-first and API-first: a
kernel with organisation, parties, items, documents and immutable ledgers, then purchasing and sales, then finance
through a first financial close. C# on .NET 10, PostgreSQL, a modular monolith with enforced module boundaries.
Apache-2.0.

Status: milestone 1 step 1, the repository and pipeline. No business code yet; the walking skeleton (D-27) follows.

## Documents

The design lives in three documents; the code is built from them and never invents a rule. Snapshots in
[`docs/design/`](docs/design/): the Foundation Design (principles, rules, decision log), the Phase 0 Specification
(entities, state machines, posting profile, invariants) and the Build Brief (stack, layout, database roles, current
milestone). [`CLAUDE.md`](CLAUDE.md) carries the ground rules; [`docs/development.md`](docs/development.md) explains
how to run the stack and the tests.

## Pinned versions

| Component | Version | Where pinned |
|---|---|---|
| .NET SDK | 10.0.112, roll forward to the latest patch of the 10.0.1xx band | `global.json` |
| PostgreSQL | 17 (image `postgres:17`) | `compose.yaml`, `tests/Themis.Integration.Tests` |
| Keycloak | 26.8 (image `quay.io/keycloak/keycloak:26.8`) | `compose.yaml` |
| Npgsql | 10.0.3 | `Directory.Packages.props` |
| Entity Framework Core and Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.12 / 10.0.3 (migrations of the kernel data context, applied by `Themis.Migrator` only) | `Directory.Packages.props`, `.config/dotnet-tools.json` (dotnet-ef 10.0.12) |
| Microsoft.AspNetCore.OpenApi and Scalar.AspNetCore | 10.0.12 / 2.17.14 (MIT); API description and reference page in development | `Directory.Packages.props` |
| OpenTelemetry | 1.19.x | `Directory.Packages.props` |
| xunit.v3 | 4.0.1 on the Microsoft.Testing.Platform runner | `Directory.Packages.props`, `global.json` |
| TngTech.ArchUnitNET | 0.13.4 (Apache-2.0) | `Directory.Packages.props` |
| Testcontainers.PostgreSql | 4.16.0 | `Directory.Packages.props` |
| CsCheck (property-based invariant tests) | 4.9.1 (Apache-2.0) | `Directory.Packages.props` |
| nuget-license (licence scan, D-38) | 4.0.18 | `.config/dotnet-tools.json` |

Every package version is held in `Directory.Packages.props` and the committed `packages.lock.json` files; CI
restores in locked mode.

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
dotnet run --project src/Themis.Migrator -- --fixture milestone-1   # needs THEMIS_MIGRATOR_CONNECTION, see docs/development.md
dotnet run --project src/Themis.Host        # http://localhost:5080/health/ready, API reference at /scalar
```

The compose stack's Keycloak realm (`docker/keycloak/themis-realm.json`) has the API client `themis-api` and the
two test users `clerk` and `preparer` of the milestone 1 fixture; see docs/development.md, Identity, for a token.

## Licence

Apache License 2.0, see [LICENSE](LICENSE).
