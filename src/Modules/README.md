# Process modules

No process module exists yet. Milestone 1 is kernel only; Purchasing and Sales arrive in Phase 1 (D-30). Until then
the test-only fixture module under `tests/Fixtures/Themis.FixtureModule` proves the boundary rules.

A process module follows one five-project template under `src/Modules/<Name>/`, named `Themis.Modules.<Name>.<Layer>`:

| Project | Holds | May reference outside the module |
|---|---|---|
| `Contracts` | Commands, queries, DTOs, integration events. The only project other modules may reference; the only one with public types. | `Themis.Kernel.Primitives`, `Themis.Kernel.Contracts` |
| `Domain` | Aggregates, value objects, domain rules. No framework references. | the same |
| `Application` | Command and query handlers, orchestration, outbox writes. | the same, plus other modules' `Contracts` |
| `Infrastructure` | Its own data context, schema, role and migrations (D-26, D-41); external adapters. | the same |
| `Api` | Endpoint registration, thin. | the same |

The architecture tests pick a new module up automatically once `Themis.Host` references it; nothing in the tests
needs editing. The database side of a module (role, schema, grants) is described in `db/bootstrap/01-roles.sql`.
