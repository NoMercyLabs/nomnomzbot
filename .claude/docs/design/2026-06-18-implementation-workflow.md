# Implementation Workflow — How We Build NomNomzBot

_Date: 2026-06-18_

How a single capability goes from picked to committed, who owns it, and who holds the
standard at each step. This is the "how we build" layer that sits above the feature specs.

---

## Principle

One capability at a time, taken **end-to-end** — picked → grounded → built → proven →
checked → validated → committed — before the next one starts. No scatter, no pile-up.
**The orchestrator owns the slice; specialists own steps.**

---

## Who holds the standard

**The orchestrator (main loop) owns every slice end-to-end and holds the bar at every gate.**
The work is split across single-scoped specialists, but accountability is not — a relay where
each agent does its step and drops it has no owner, and the outcome falls between the cracks.
One throat to choke.

The orchestrator never delegates:
- **Capability selection** — picks ONE thing; refuses to open a second slice until the first
  commits. This single rule is what prevents the 381-changed-files failure state.
- **Sequencing** — drives the lifecycle below, gate by gate.
- **All git** — commits, branches, gates.
- **Validation** — build + full test + endpoint probe, with evidence captured.
- **The settle/go interface with Stoney.**

A slice is "done" only when the orchestrator commits it, and it only commits on green evidence.
There is no separate "conductor agent" — it would need git, validation, and the Stoney
interface, all deliberately kept by the orchestrator. The orchestrator *is* the conductor.

---

## Roles

### Standing agents (defined as `.claude/agents/*.md`; fire across slices)

| Agent | Cadence | Job |
|---|---|---|
| **Context Packer** | every slice (first) | Assembles the task brief from the aitm-indexed specs: §3 interface, §5 endpoint rows, §7.1 action keys, schema delta, events — plus the canonical exemplar and the Constitution. Read-only. The anti-fly-blind gate; nothing starts without its pack. |
| **Slice Implementer** | every slice | Builds ONE capability end-to-end into its module from the brief. Does **not** write its own tests (independence). |
| **Test Author** | every slice | Behavior-proving tests, independent of the implementer: assert state change + events emitted + side effects. Smoke tests are void. |
| **Slice Reviewer** | every slice | Adversarial audit vs the slice DoD + house rules (placement, namespace, header, no-cruft). Reports a verdict; does **not** fix. |
| **Schema Steward** | schema-touching slices only | Sole hand on the single living schema; owner of the one clean end-of-build migration. |
| **Platform Engineer** | rare (heavy at Phase 0) | The engine + any new shared plumbing (`Result<T>`, `IUnitOfWork`, event bus, module contract, auto-discovery, base controller/test harness). The **only** agent allowed in shared plumbing. Standing by definition, one-off by cadence. |

### One-off dispatch (no permanent definition)

- **Restructure pass** — a single general-purpose agent with a precise move-brief: relocate
  existing `server/src` into its taxonomy home. Pure structure, **no behavior change**. Runs
  once during setup, then gone. (Purely phase-temporary → not a roster seat.)

---

## Knowledge layers — every build agent is educated, not blind

1. **Constitution** — house rules distilled to one brief: license header, file-scoped namespace,
   `Result<T>`, `IUnitOfWork`, async-all-the-way, no MediatR/Roslyn, taxonomy placement,
   the testing standard, no-cruft.
2. **Canonical exemplar** — one fully-built reference slice the others mimic for consistency.
3. **Per-task brief** — the Context Packer's spec pack for that specific capability.

---

## The slice lifecycle (the factory line)

Owner = orchestrator at every gate. A gate that isn't green sends the slice **back**, not forward.

0. **Pick** — orchestrator selects ONE capability. No second slice opens until this one commits.
1. **Ground** — Context Packer assembles the brief. _Gate: brief complete._
2. **Define done** — orchestrator turns the brief into this slice's Definition of Done (below),
   specialized to the capability.
3. **Build** — Slice Implementer implements to the brief.
4. **Schema** (if touched) — Schema Steward updates the living schema. _Gate._
5. **Prove** — Test Author writes behavior tests; orchestrator runs them.
   _Gate: green AND tests assert state / events / side-effects._
6. **Check** — Slice Reviewer scores against the DoD + house rules. _Gate: pass, or back to Build._
7. **Validate** — orchestrator runs build + full test + endpoint probe. _Gate: all green, evidence captured._
8. **Commit** — orchestrator commits the one validated capability (one capability = one commit),
   on Stoney's standing go.

---

## Definition of Done — the written standard the orchestrator gates on

A slice is done only when **all** hold, with evidence:

- [ ] Matches the brief: interface, endpoint(s), action keys, events.
- [ ] State change persisted and **shaped** correctly (fields / types / invariants) — not "returned non-null".
- [ ] Events / messages emitted as specified.
- [ ] Behavior tests present, independent, and able to **fail for the right reason**; green.
- [ ] House rules: placement, file-scoped namespace, license header, `Result<T>`,
      `IUnitOfWork` where multi-write, async-all-the-way, no-cruft.
- [ ] Slice Reviewer verdict: **pass** (audited independently of the implementer).
- [ ] Build green; full test green; endpoint probe matches spec — **output captured**.
- [ ] Schema (if touched) reflected in the living schema.

Nothing is called "done" or committed until this is green. The standard is held by the
orchestrator, **enforced by structure** — independent audit + captured evidence + withheld
commit — not by goodwill.

---

## Reorganization sequence (skeleton-first)

1. **Restructure pass** moves current `server/src` into taxonomy homes (one-off, structure only).
2. **Platform Engineer** stands up the engine skeleton + the canonical exemplar slice.
3. Orchestrator writes the **Constitution**.
4. Standing agents defined as `.claude/agents/*.md`.
5. **Feature factory** begins: one capability slice at a time through the lifecycle above.

All of the above is gated behind Stoney's explicit go — no code until specs / scopes are settled.
