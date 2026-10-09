# Documentation

| Path | Content |
|---|---|
| `design/themis-foundation-design.md` | Snapshot of the Themis Foundation Design: principles, kernel domain model, the core rules, the decision log |
| `design/themis-phase-0-specification.md` | Snapshot of the Themis Phase 0 Specification: entity attributes, state machines, posting profile, invariants |
| `design/themis-build-brief.md` | Snapshot of the Themis Build Brief: stack, repository layout, database roles, the current milestone |
| `development.md` | Local development: toolchain, the compose stack, migrator, tests, database roles, telemetry |
| `../CLAUDE.md` | Ground rules for the build session, taken from the brief |
| `../src/Modules/README.md` | How a process module is laid out and what it may reference |
| `../db/bootstrap/01-roles.sql` | The database role model, with its reasoning in the file header |

The snapshots under `design/` are generated from the live documents and are not edited by hand. The live documents
are authoritative; a snapshot is refreshed in its own pull request (label `docs`) whenever the live document changes
in a way the code depends on, and the pull request names the decisions that changed.
