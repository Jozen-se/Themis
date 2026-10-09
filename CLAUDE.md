# Themis ERP (working brand: Jozen)

Open-source ERP for distributors of industrial fasteners and components. Backend first; the frontend comes in Phase 5.

## Sources of truth (higher wins; report conflicts, never choose)
1. docs/design/themis-foundation-design.md: principles P-xx, rules R-xx (invariant or configurable default), decisions D-xx, phases, build constraints
2. docs/design/themis-phase-0-specification.md: entities, state machines, posting profile, posting events E-xx, invariants I-xx
3. docs/design/themis-build-brief.md: stack, layout, database roles, current milestone
These are dated snapshots of the planning documents. If a snapshot looks wrong or is silent, stop and ask the owner.

## Ground rules
- Cite rule IDs: in a comment where a rule is enforced, and in the test name that proves it.
- Rejected commands return problem details with ruleId and code (R-API-03).
- No double or float for money, quantity, cost or rates. C# decimal, PostgreSQL numeric, JSON strings (R-MON-01, D-37).
- Ledger and audit tables are insert-only. Corrections are reversals (R-LED-01, R-LED-06).
- Only the kernel posting service writes inventory and journal entries, in the command's single transaction (R-LED-05, R-LED-07).
- Master data the ledgers reference lives in the kernel (P-02). Process modules start in Phase 1.
- Modules reference only Themis.Kernel.Primitives, Themis.Kernel.Contracts and other modules' Contracts projects (D-26).
- One connection, one transaction per command, SET LOCAL ROLE to the owning role, no retrying execution strategy (D-41, R-DB-01).
- Every command: permission TYPE.COMMAND, Idempotency-Key, If-Match on changes, audit events, outbox event in the same transaction.
- Only Themis.Migrator applies migrations; the application never calls Migrate().
- Every new table or view gets explicit grants; the privilege matrix test must pass.
- Configurable defaults live in settings tables, never in code (P-11).
- New dependencies must pass the licence scan; no copyleft (D-38).
- Never change a rule or decision in code. Write "Proposed change to R-xx" in the pull request for the owner.
- No model identifiers in anything pushed. Commits and pull requests carry the session link as traceability.

## Commands
- Start dependencies: docker compose up -d (or podman compose up -d)
- Migrate: dotnet run --project src/Themis.Migrator
- Build: dotnet build
- All tests: dotnet test
- Run: dotnet run --project src/Themis.Host

## Current milestone
Milestone 1, walking skeleton. Scope and acceptance criteria are in docs/design/themis-build-brief.md. Do not start work outside that scope.
