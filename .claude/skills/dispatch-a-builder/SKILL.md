---
name: dispatch-a-builder
description: Brief and supervise a subagent building a NomNomzBot slice, and judge what it reports back. Use when handing implementation work to an agent, running several agents in parallel, or deciding whether an agent's "all green" is true.
---

# Dispatch a builder

## Parallelism

Cap it at about **three** concurrent agents. Beyond that the shared tree thrashes and the
orchestrator loses track of who owns which file. Give each agent a **disjoint file set**.

Two isolation choices:

- **Shared tree** — fast, but every agent must stage by **explicit pathspec** and must never
  `git stash`. Commit the parent's WIP *before* dispatching anything that formats or sweeps.
- **Worktree** (`isolation: "worktree"`) — for anything sweeping. Check the agent's
  **merge-base**: a stale base silently reverts other work. Verify the agent committed to **its
  own branch**, not the main tree. Never sweep worktrees while an agent is still live; clean up
  only completed ones.

## Every brief must carry

1. **The exact slice** — start and finish, the files it owns, and nothing else.
2. **Explicit types, never `var`.** `.editorconfig` flags IDE0008 as an **error**. This has been
   silently violated before; it goes in every C#-writing brief, verbatim.
3. **The license header** for every new source file (AGPL-3.0, NoMercy Labs).
4. **For any brief that writes UI: "load the `sleak` skill before writing the screen", verbatim.**
   Then name the three rules the agent will be judged against, because an agent that only reads
   `frontend-design-system.md` gets correct tokens and no hierarchy: **one primary action per group**
   (a row of equal-weight buttons is a defect — siblings outline/ghost, destructive distinct),
   **concentric radius** (nested rounded container never reuses the parent radius when padding > 0),
   **scarce accent** (full chroma marks one task per page). This is the design equivalent of the
   `var` rule above: it has been silently skipped before, so it goes in the brief every time.
5. **The gate it must run before committing** — `scripts/slice-check.ps1` with its own
   `-TestProject`, `-Filter` and `-Paths`; frontend: `jvmTest` **and** `compileKotlinWasmJs`.
6. **The testing bar**: assert state changes, emitted events, side effects. Smoke tests
   ("returned non-null", "did not throw", "the mock was called") are void and do not count.
7. **The report shape** you want back: what changed, the evidence, the first real error on
   failure — not file dumps.
8. **Do not push.** Pushing is the orchestrator's decision.

## Judging what comes back

- **A filtered green is not a tree green.** Before accepting a body of work, run
  `scripts/verify-tree.ps1` yourself on the combined tree. One session's "all green" agent
  reports hid an ungated endpoint, a save-blocking registry bug, five unscoped domain events and
  a null content-type.
- **Verify a claimed finding before acting on it.** An agent once reported "these pickers are
  label-only"; the sweep it justified turned out to be wrong — the pickers were already rich.
  Check the claim, then brief the work.
- **A UI claim needs the rendered client**, not a 200. See `run-the-stack`. Look at the screenshot
  against the Sleak rules while you are there — "it renders and the buttons work" is not the bar, and
  a flat wall of equal-weight actions passes every test we run.
- An agent reporting a blocker is not a reason to stop: close what you can, dispatch the next
  slice, keep the queue saturated.

## Related playbooks

`sleak` (mandatory for UI work) · `build-server` · `build-app` · `run-the-stack` ·
`commit-a-slice` · `watch-ci` · `deploy-and-verify` · `devbox`
