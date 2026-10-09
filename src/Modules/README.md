# Business and platform modules

No module exists yet. Phase 0 is the kernel alone (D-30); Purchasing and Sales arrive in Phase 1.

A module is a set of five projects under `src/Modules/<Name>/`, named `Themis.Modules.<Name>.<Layer>`, with the
same layering as the kernel:

| Project | Holds | May reference |
|---|---|---|
| `Themis.Modules.<Name>.Contracts` | Commands, queries, DTOs, integration events. The only project other modules may reference. The only project with public types. | `Themis.Kernel.Contracts` |
| `Themis.Modules.<Name>.Domain` | Aggregates, value objects, domain rules. No framework references. | own Contracts, `Themis.Kernel.Contracts` |
| `Themis.Modules.<Name>.Application` | Command and query handlers, orchestration, outbox writes. | own Domain and Contracts, `Themis.Kernel.Contracts`, other modules' Contracts |
| `Themis.Modules.<Name>.Infrastructure` | Persistence in the module's own schema and database role (D-26, D-41), external adapters, migrations. | own Application, Domain and Contracts, `Themis.Kernel.Contracts` |
| `Themis.Modules.<Name>.Api` | Endpoint registration, thin. | own Application and Contracts, `Themis.Kernel.Contracts` |

The architecture tests in `tests/Themis.ArchitectureTests` pick a new module up automatically once `Themis.Host`
references it; nothing in the tests needs editing. The database side of a module (role, schema, grants) is
described in `db/init/01-roles.sql`.
