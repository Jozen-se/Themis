## Task brief

- **Step brief:** <!-- the step brief and the release line it came from, e.g. "Milestone 2, step 2a (final), released 2026-10-22 23:59; amendment 1, released 2026-10-23 18:05" -->
- **Review tier (D-59):** <!-- full or light, and why (the files changed) -->
- **Goal:** 
- **Rule IDs:** 
- **Entities touched:** 
- **Acceptance (invariants and scenarios):** 
- **Out of scope:** 

## What changed

## Deviations and out-of-scope findings

<!-- Every deviation from the brief and every change the brief does not ask for (RR-91); "None" when there is none. -->

## Checklist

- [ ] One task, one pull request; the title and commits name the rule IDs they implement
- [ ] No rule invented: every behaviour traces to a rule or decision in the Foundation or the Phase 0 specification; a rule gap or a proposed change goes to the planner, who brings it to the owner, not a guess (CLAUDE.md, Ground rules; D-68)
- [ ] CI is green on the head commit (tick only after the checks passed)
- Local results (local, not CI): 
- [ ] Tests cover the acceptance above (invariant tests for I-nn where ledgers or stock are touched)
- [ ] No secrets, no model identifiers, no generated files edited by hand

## Owner checklist (ticked by the owner before the squash merge, D-59)

- [ ] The required checks are green on the head commit
- [ ] Full tier: the reviewer's summary names that head commit
- [ ] Every pull request: the planner's conformance result names that head commit
- [ ] Every conversation is resolved
