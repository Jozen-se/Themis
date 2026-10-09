# Themis

An open source ERP for distribution and manufacturing, built backend-first and API-first: a kernel with organisation,
parties, items, documents and immutable ledgers, then purchasing and sales, then finance through a first financial
close. C# on .NET 10, PostgreSQL, a modular monolith with enforced module boundaries. Apache-2.0.

Status: Phase 0 scaffold. No business code yet; the walking skeleton (D-27) is the next pull request.

## Documents

The design lives in two documents; the code is built from them and never invents a rule.

- Themis Foundation Design (principles, kernel model, rules, decision log): snapshot in
  [`docs/design/themis-foundation-design.md`](docs/design/themis-foundation-design.md)
- Themis Phase 0 Specification (entities, state machines, posting profile, invariants): snapshot in
  [`docs/design/themis-phase-0-specification.md`](docs/design/themis-phase-0-specification.md)
- [`CLAUDE.md`](CLAUDE.md): the working model, the rule-ID vocabulary and the commands
- [`docs/development.md`](docs/development.md): running the stack and the tests locally

## Toolchain notes

- **Architecture tests use ArchUnitNET** (`TngTech.ArchUnitNET`, pinned to 0.13.4 in `Directory.Packages.props`
  and `packages.lock.json`), the alternative named in D-26. NetArchTest.Rules was rejected: last release May 2021,
  no licence expression on NuGet. Its maintained fork NetArchTest.eNhancedEdition was checked and rejected too:
  last release June 2025, against ArchUnitNET's August 2026 release under Apache-2.0.

## Quick start

```bash
docker compose up -d                        # PostgreSQL 17 and Keycloak
dotnet tool restore
dotnet build Themis.sln -c Release
dotnet test --solution Themis.sln -c Release --no-build
dotnet run --project src/Host/Themis.Host   # http://localhost:5080/health/ready
```

## Licence

Apache License 2.0, see [LICENSE](LICENSE).
