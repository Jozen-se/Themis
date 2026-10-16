# Themis ERP (working brand: Jozen)

Open-source ERP for distributors of industrial fasteners and components. Backend first; the frontend comes in Phase 5.

## Sources of truth (higher wins; report conflicts, never choose)
1. docs/design/themis-foundation-design.md: principles P-xx, rules R-xx (invariant or configurable default), decisions D-xx, phases, build constraints
2. docs/design/themis-phase-0-specification.md: entities, state machines, posting profile, posting events E-xx, invariants I-xx
3. docs/design/themis-build-brief.md: stack, layout, database roles, current milestone
These are dated snapshots of the planning documents. If a snapshot looks wrong or is silent, stop and ask the owner.

## Live planning documents
The live documents are the source of truth; docs/design holds revision-stamped copies. Each snapshot header names the revision it was taken from. If a live document's revision is higher than its snapshot, report it to the owner before relying on the snapshot.
Snapshots are refreshed from the live documents at the revision the planner names in its release line, reading that exact revision even if the live document has moved on. The coder records the revision in each snapshot header and in a pull request labelled docs that lists the revisions read and the IDs added or changed. If the named revision cannot be read, stop and ask the owner.
- Themis Foundation Design: https://claude.ai/code/artifact/51f4caaa-2d53-4cc2-b486-0be0a7366534 (snapshot: docs/design/themis-foundation-design.md)
- Themis Phase 0 Specification: https://claude.ai/code/artifact/237492a8-0f0a-4e9d-a8da-44f65de0a56b (snapshot: docs/design/themis-phase-0-specification.md)
- Themis Build Brief: https://claude.ai/code/artifact/a781c405-11f8-42e1-bf7f-7090dbc9df8b (snapshot: docs/design/themis-build-brief.md)

Reference only (not sources of truth; the documents above win on any conflict):
- Themis Review Remediation Register: https://claude.ai/code/artifact/91610c8f-7465-46a7-9f48-0bf2c5171ede (snapshot: docs/design/themis-review-remediation-register.md; findings RR-xx and the owner's decision on each; accepted items are already in the documents above)
- Themis Architecture Recommendations: https://claude.ai/artifact/Lo9h7fGS2L4hbw7RsFDvDz (background input; the foundation wins where they differ)
- Themis Capability Coverage Map: https://claude.ai/code/artifact/fb7a2e56-0e18-44ef-980b-846a9be72acd (capability checklist with gaps G-xx; nothing in it is in scope until a D-xx entry puts it there)

## Ground rules
- Cite rule IDs: in a comment where a rule is enforced, and in the test name that proves it.
- Rejected commands return problem details with ruleId and code (R-API-03).
- No double or float for money, quantity, cost or rates. C# decimal, PostgreSQL numeric, JSON strings (R-MON-01, D-37).
- Ledger and audit tables are insert-only. Corrections are reversals (R-LED-01, R-LED-06).
- Only the kernel posting service writes inventory and journal entries, in the command's single transaction (R-LED-05, R-LED-07).
- Master data the ledgers reference lives in the kernel (P-02). Process modules start in Phase 1.
- Modules reference only Themis.Kernel.Primitives, Themis.Kernel.Contracts and other modules' Contracts projects (D-26).
- One connection, one transaction per command, SET LOCAL ROLE to the owning role, no statement-level retries; whole-command retry only as D-41 allows (D-41, R-DB-01).
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
