# Interface Specification — Pipeline Control Flow

**Status:** Implementable. Code the owner writes from this should compile first-try. **Extends `commands-pipelines.md`** (the pipeline engine + `PipelineStep` H.2); it adds nesting + control-flow constructs, it does not replace the engine.
**Sources of truth:** Streamer.bot's sub-action control flow (If/Else, **Switch**, **While**, **Break**, **Run Action** inline/independent, weighted **Random Action** — the ecosystem reference). Corpus: `commands-pipelines.md` (§1 `Pipeline` H.1 / `PipelineStep` H.2 / `PipelineStepCondition` H.3; §3.3 `IPipelineEngine.ExecuteAsync(PipelineRequest)`; §3.13 `ICommandAction`/`ActionContext`; the existing per-step `condition`, `{{if.*}}` template, `random` condition, `stop` action, `NamedCounter` `{{count.*}}`); `bounded-and-allow` rule (cap loops/recursion, then allow); `scaling-qos.md` (tier-scaled limits); `platform-conventions.md`. Locked schema `2026-06-16-database-schema.md` (Domain H — extends H.2 `PipelineStep`).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; UUIDv7 `Guid` PKs; `BroadcasterId Guid` tenant scope; Newtonsoft.Json.

> **Why.** Pipelines today are linear-with-per-step-conditions: there's `If/Else` (condition + `{{if.*}}`), a `stop`, and an *unweighted* `random`. The four power-user constructs every serious bot has are missing: **multi-case `switch`**, **loops**, **calling another pipeline** (reuse), and **weighted random** selection. This spec adds them by making `PipelineStep` a **tree** (block steps own child steps) and adding the constructs as block-kinds + actions — **bounded** (iteration / recursion / total-action / runtime caps) so any combination always terminates. This is the foundation that turns pipelines into real automation and makes the Automation API's "run any pipeline" far more useful.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **`PipelineStep` becomes a tree.** Add `BlockKind` + `BlockConfigJson` (`ParentStepId` already exists). A null `BlockKind` = a leaf **action** step (today's behavior). A non-null `BlockKind` (`if`/`switch`/`switch_case`/`loop`/`random_branch`/`random_case`) is a **block** that owns ordered child steps. The engine walks the tree depth-first; leaf actions execute via the existing `ICommandAction` path. **One nesting model:** the pre-existing `PipelineStep.Branch` (`then`/`else`) is **folded in** — it is now simply the slot a child occupies under an `if` block; the legacy if/else nesting is not a second model. (no-backwards-compat: clean structural extension.) |
| D2 | **`if` / `switch`** — binary branching is an **`if`** block (condition in `BlockConfigJson`) whose children carry the existing `Branch` (`then`/`else`) slot. Multi-case is a **`switch`** block: it evaluates a value (template) against ordered `switch_case` children (each a match value + comparison reusing the §6.2 condition operators) plus an optional default; the **first** matching case's children run, then control leaves the switch. |
| D3 | **`loop`** — a `loop` block runs its children repeatedly per `Mode`: **`foreach`** (iterate a list var — CSV or JSON array — binding `{{loop.item}}`/`{{loop.index}}`), **`repeat`** (a fixed count), **`while`** (a condition, re-evaluated each pass). Bounded by **`MaxIterations`** (tier-scaled hard ceiling). **`break`** exits the enclosing loop; **`continue`** skips to the next iteration. |
| D4 | **`run_pipeline`** — an action that invokes another of the channel's pipelines: **`inline`** (runs within the current run — shares the variable bag, merged on return) or **`detached`** (an independent run with its own context; optional `Wait`). Args via a template list. Bounded by **`MaxRecursionDepth`** (a pipeline cannot infinitely call itself / cycle). |
| D5 | **`random_branch`** — a block whose `random_case` children each carry a `Weight` (decimal, auto-normalized); exactly **one** case's children run, chosen by weighted random using the run's CSPRNG. Generalizes the unweighted `random` condition / `random_response`. |
| D6 | **Global termination budget — every run is bounded.** Each execution carries `MaxTotalActions`, `MaxRecursionDepth`, `MaxIterations` (per loop), and `MaxRuntime` (tier-scaled, safe baseline + headroom). Exceeding any cap **aborts the run cleanly** (typed result, journaled) — so no loop/recursion/switch combination can hang or run away. Per the bounded-and-allow rule. |
| D7 | **Schema:** extend **H.2 `PipelineStep`** (`ParentStepId`/`BlockKind`/`BlockConfigJson`) — no new table. New actions `run_pipeline`, `break`, `continue`; new block-kinds. No new role keys (editing the tree is `pipelines:write`). |

---

## 1. Entities (schema delta)

Extend **`PipelineStep` (H.2)** — the only schema change:

| Table | Schema ref | Change | Fields (type) |
|---|---|---|---|
| **`PipelineStep`** | **H.2 (columns add)** | add | `BlockKind string(20)?` **[VC:enum]** (`if`\|`switch`\|`switch_case`\|`loop`\|`random_branch`\|`random_case`; null = leaf action step). `BlockConfigJson text?` **[VC:JSON]** (block params: `if`/`while` condition; switch value/case-match + comparison; loop mode/list-var/count; case weight). The existing `Order` becomes **order within the parent**. |

`ParentStepId` (self-FK) and `Branch` (`then`\|`else`) **already exist** (the legacy if/else nesting); they are reused — `ParentStepId` is the tree edge and `Branch` is the slot a child takes under an `if` block. A leaf action step keeps its existing columns (`ActionType`, `ConfigJson`, the per-step `condition` via H.3). No new table; no per-run state table (loop/recursion counters are in-memory per run).

---

## 2. Domain events

None. Control flow is internal to a pipeline run; the run is already recorded (`PipelineExecution` / `CommandLogEntry`). A budget-abort sets the execution's outcome to `aborted_budget` with the cap that tripped.

---

## 3. Engine & service deltas (`commands-pipelines.md`)

No new top-level interface — the existing `IPipelineEngine.ExecuteAsync(PipelineRequest)` gains **tree execution** with the §0 caps; `IPipelineService.ValidateAsync` gains **tree validation**.

```csharp
// Engine behavior (extends IPipelineEngine — commands-pipelines §3.3):
//  • Walk PipelineStep depth-first by (ParentStepId, Order). Leaf (BlockKind=null) → ICommandAction.ExecuteAsync.
//  • switch       → evaluate value, run the first matching switch_case's children (else default), then continue.
//  • loop         → run children per Mode; bind {{loop.item}}/{{loop.index}}/{{loop.count}}; honor break/continue;
//                   hard-stop at MaxIterations.
//  • random_branch→ pick one random_case by normalized Weight (run CSPRNG), run its children.
//  • run_pipeline → inline (same ExecutionContext, depth+1) or detached (new run); reject when depth > MaxRecursionDepth.
//  • Every executed leaf decrements the run's action budget; budget/runtime/depth breach → ExecutionOutcome.AbortedBudget.

public sealed record PipelineExecutionLimits(int MaxTotalActions, int MaxRecursionDepth, int MaxIterations, TimeSpan MaxRuntime); // tier-scaled
```

`IPipelineService.ValidateAsync` additionally checks: block steps have legal child kinds (`switch`→`switch_case`+default; `random_branch`→`random_case` with weights; `loop` has a valid `Mode`), `run_pipeline` targets resolve and the static call graph has **no unbounded cycle**, and the tree depth is within `MaxRecursionDepth`.

---

## 4. Pipeline actions, block-kinds, template vars

**New actions** (`ICommandAction`, §3.13):

| Action `Type` | Parameters | Behavior |
|---|---|---|
| **`run_pipeline`** | `{ string Pipeline (id/name), string Mode (inline\|detached), IReadOnlyList<string>? Args, bool Wait }` | invoke another pipeline; depth-capped (D4). |
| **`break`** | — | exit the enclosing `loop` (no-op outside a loop). |
| **`continue`** | — | skip to the enclosing loop's next iteration. |

**New block-kinds** (`PipelineStep.BlockKind`, configured via the editor, not standalone actions): `if` (then/else via the existing `Branch` slot), `switch` + `switch_case`, `loop`, `random_branch` + `random_case` (D2/D3/D5).

**New template vars:** `{{loop.item}}`, `{{loop.index}}` (0-based), `{{loop.count}}`, `{{switch.value}}` — scoped to the enclosing block, resolved from the run bag (no I/O).

---

## 5. REST surface

**None.** The step tree is part of the pipeline definition — created/edited through the existing `commands-pipelines.md` pipeline CRUD (`pipelines:write`); `ValidateAsync` (existing endpoint) now also reports tree/cycle/cap errors. No new endpoints, no new Gate-2 keys.

---

## 6. DI & testing

The new actions (`run_pipeline`/`break`/`continue`) auto-discover into the pipeline action registry (`commands-pipelines.md`); the engine tree-walk + caps live in the existing engine; `PipelineExecutionLimits` resolves tier-scaled from config. No new module DI beyond registering the three actions.

**Tests (prove behavior):** a `switch` runs only the first matching case (and the default when none match), never two; a `foreach` over a 3-item list runs its body 3× with `{{loop.index}}` = 0/1/2 and `{{loop.item}}` bound, and `break` stops it early while `continue` skips an iteration; a `while` that never falsifies is **hard-stopped at `MaxIterations`** with outcome `aborted_budget` (not a hang); `repeat N` runs exactly N times; `random_branch` over cases weighted 1/1/2 selects case C ~50% across many seeded runs (distribution within tolerance); `run_pipeline inline` shares and merges variables into the caller while `detached` does not; a pipeline that calls itself is rejected at validation (static cycle) and, if forced at runtime, aborts at `MaxRecursionDepth`; a tree whose total executed actions would exceed `MaxTotalActions` aborts cleanly mid-run and journals the cap that tripped; `ValidateAsync` rejects a `switch` with a non-`switch_case` child and a `random_case` missing a weight.

---

## 7. Decisions (resolved)

`PipelineStep` tree via `ParentStepId`/`BlockKind`/`BlockConfigJson` (D1); `switch`/`switch_case` + default = multi-case incl. if/else (D2); `loop` foreach/repeat/while with `break`/`continue`, iteration-capped (D3); `run_pipeline` inline/detached, recursion-depth-capped (D4); weighted `random_branch`/`random_case` via CSPRNG (D5); global per-run termination budget aborts runaways cleanly (D6); schema delta = H.2 `PipelineStep` columns + three actions + block-kinds, no new table/endpoints/roles (D7).
