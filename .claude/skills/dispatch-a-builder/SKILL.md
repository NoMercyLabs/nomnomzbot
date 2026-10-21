---
name: dispatch-a-builder
description: Brief and supervise a subagent building a NomNomzBot slice, and judge what it reports back. Use when handing implementation work to an agent, running several agents in parallel, or deciding whether an agent's "all green" is true.
---

# Dispatch a builder

## Parallelism

At most **2** agents that build or test code and **6** read-only agents at once (the global
rule). Give each agent a **disjoint file set**, or the shared tree thrashes and nobody knows who
owns which file.

Two isolation choices:

- **Shared tree** — fast, but every agent must stage by **explicit pathspec** and must never
  `git stash`. Commit the parent's WIP *before* dispatching anything that formats or sweeps.
- **Worktree** (`isolation: "worktree"`) — for anything sweeping. Check the agent's
  **merge-base**: a stale base silently reverts other work. Verify the agent committed to **its
  own branch**, not the main tree. Never sweep worktrees while an agent is still live; clean up
  only completed ones. Land a finished worktree branch with `scripts/land-worktree.ps1`.

## Before every dispatch: brief-check

Write the brief to the session scratchpad, then run
`sh scripts/brief-check.sh <draft.md> --type <subagent_type> --model <model>`. It names every gap of
every dispatch guard at once, with the full text of each rule the brief must cite. Fix them all and
run it again until it prints PASS. Then dispatch the same text with the same type and model.

## Every brief starts from this skeleton

It passes every dispatch guard as it stands. Fill in the angle brackets and keep every line.

```text
You are one <builder | researcher> worker. Model: <haiku | sonnet | opus>.
Executor: Arc because <why Arc runs it now>      (only with a time budget of 40 minutes or more)
Task (one item): <the slice: its start and finish, in one sentence>.
Work in: <absolute repo or worktree path>. Files you own: <paths>. Touch nothing else.
- Write the failing test, run it, save the failing output.
- <the change>
- <the gate>
Gates before you commit: <scripts/slice-check.ps1 -TestProject ... -Filter ... -Paths ...>. Commit by explicit pathspec. Do not push. Never bypass a hook.
Time budget: <N> minutes.
Context budget 150k: at 100k, write or update a handoff file in the session scratchpad and report its path.
Report: one sentence; details go to a file.

Rules applied:
- <rule id>: <how this brief follows it>
```

- **One item, steps as bullets.** A line that starts with `2.`, `2)`, `b.` or `Item 2` is a second
  work item, and the guard refuses it. Give that item its own builder and run the two in parallel.
- **The details file** holds what changed, the evidence, and the first real error on failure. It
  never holds file dumps.
- **Tests come first, every time.** The test step comes before any production edit. A worker that
  continues a code-first handoff proves failing-first against `git show HEAD:<file>` copies.
- **The testing bar**: assert state changes, emitted events and side effects. Smoke tests
  ("returned non-null", "did not throw", "the mock was called") do not count.
- **A reading budget**: at most ~40k tokens of reading before the first test file. Find a symbol
  with `grep -n` and read only that range. A re-dispatch from a handoff says "do not re-research",
  and carries `Previous run: <model> <N>k.` with a larger budget or a stronger model.
- **C# briefs**: "Explicit types, never `var`" verbatim (`.editorconfig` IDE0008 is an error), and
  the license header on every new source file. **Dashboard (TypeScript/Vue) briefs** say the
  opposite: no annotation the value already has, constant tables `as const satisfies`, and
  `eslint --fix` before the gate.
- **UI briefs**: "load the `sleak` skill before writing the screen", verbatim, plus the three rules
  the work is judged on: **one primary action per group**, **concentric radius**, **scarce accent**.
- **Read-only jobs** go to a read-only type: Explore (search, measure, read-only commands) or
  wf-checker. A general agent with a read-only brief is refused. A brief that orders a write
  ("commit it", "fix the ...") is a writing brief, whatever else it calls read-only.
- **Manager briefs** also need the wait lines and the lanes; brief-check names the exact words.

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

`sleak` (mandatory for UI work) · `build-server` · `run-the-stack` ·
`commit-a-slice` · `watch-ci` · `deploy-and-verify` · `devbox`
