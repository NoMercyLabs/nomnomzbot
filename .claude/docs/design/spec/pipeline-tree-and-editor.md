# Interface Specification — Pipeline Tree Model & Authoring Editor

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Extends:** `pipeline-control-flow.md` (D1–D7: `PipelineStep` tree, `if`/`switch`/`loop`/`random_branch`/`run_pipeline`,
termination budget) and `commands-pipelines.md` (engine, actions §6.1, conditions §6.2, templates §6.3, §10 deferred
one-shot scheduling). **This spec does not redefine anything already decided there** — it adds condition TREES, N
triggers per pipeline, wait-for-event/resume-later persisted runs, sub-pipeline args+return, execution caps for the
new constructs, the primitive set that closes the old-bot gap list, and the block-list editor. Every fact about
existing behaviour is cited `path:line`.
**Grounding corpus:** `old-bot-pipeline-capability-analysis.md` (26 behaviours, 22-item gap list, 5 can't-be-generic
cases — all mapped below), `PRODUCT-ALIGNMENT.md` D1–D12, `CLAUDE.md` consequence-visibility law.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable
enable`; explicit types — never `var`; async all the way; `Result<T>`; Repository + `IUnitOfWork`; UUIDv7 `Guid` PKs;
`BroadcasterId Guid` tenant scope; Newtonsoft.Json.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| E1 | **N triggers per pipeline.** `Pipeline` gains a child table `PipelineTrigger` (was: one `TriggerKind` enum column on `Pipeline`). A pipeline has 1..N trigger rows, each independently a `command`\|`event`\|`timer`\|`manual`\|`webhook` binding. Fixes gap #17 (Fight/Hug alias hack). |
| E2 | **Boolean condition TREE.** `PipelineStepCondition` (H.3, flat list ANDed today) becomes a **tree** via a new self-FK `ParentConditionId` + `GroupOp` (`and`\|`or`) on group nodes and `Negate` on leaves (already exists). A branch's gate is the root condition-tree node. Fixes gap #1. |
| E3 | **Wait-for-event / resume-later.** New action **`wait_for_event`** suspends the *current run* until a matching event arrives or a timeout elapses; the run's full state (variable bag, tree position, loop/switch cursors) persists to a new table `PipelineRunState` so a run survives process restart. Fixes gap #2 and can't-be-generic #3 (Lucky Feather two-hop chain becomes one long-lived run). |
| E4 | **Sub-pipeline arguments + return value.** `run_pipeline` (control-flow D4) gains typed **named** args (already `IReadOnlyList<string> Args`, positional — this spec adds name binding) and a **return value**: the callee's `return_value` action sets a value the caller receives into `{{call.result}}`. |
| E5 | **Recursion depth limit** already capped by `MaxRecursionDepth` (control-flow D4/D6) — this spec sets the concrete default (**8**) and the **static-cycle validation rule** for the new call graph incl. `wait_for_event`-suspended calls. |
| E6 | **Loop execution caps** already capped by `MaxIterations`/`MaxRuntime` (control-flow D6) — this spec adds the **per-iteration wait duration from loop state** (template arithmetic, gap #6) and **loop timeout guard** distinct from the whole-run `MaxRuntime` (a loop can be capped tighter than the run). |
| E7 | **Primitive set closes all 22 gap-list items** (§3) — no bespoke block anywhere; every old-bot behaviour (§2) is expressed by composing E1–E6 + the existing control-flow/action/condition/template surface. |
| E8 | **Editor = nested block list**, drag reorder + drag into/out of branches, condition-tree sub-editor, consequence/blast-radius panel wired in from day one (§4) — not a node graph. |
| E9 | **Migration.** Flat legacy pipelines (`Branch` then/else only, `PipelineStepCondition` flat-AND list, `Pipeline.TriggerKind` single column) upcast losslessly into the tree model at read time via a **compatibility view**, and are rewritten to native tree rows on first save by the new editor (§6). Older-client saves (pre-tree client) are rejected at the API boundary once the tree columns are non-null-defaulted (§6.3) — never silently downgraded. |

---

## 1. Data model — the tree

### 1.1 `PipelineStep` (H.2) — unchanged shape, reused as designed in `pipeline-control-flow.md` D1/D7

No new columns beyond control-flow's `BlockKind`/`BlockConfigJson`. This spec adds two **new block-kinds** to the
enum already declared open-ended there (`if`\|`switch`\|`switch_case`\|`loop`\|`random_branch`\|`random_case`):

| New `BlockKind` | `BlockConfigJson` shape | Semantics |
|---|---|---|
| `try` | `{}` | owns two child slots via `Branch`: `body` (`then` slot, reused) and `catch` (`else` slot, reused) — see §5.1 |
| `detached_step` | `{}` | wraps exactly one leaf action child; the engine dispatches that one action fire-and-forget instead of the whole block (§3, gap #14) |

`if`'s `BlockConfigJson` condition field (today: a single `PipelineStepCondition` id) becomes **a pointer to a
condition-tree root** (§1.2) — same column, richer payload. No breaking rename.

### 1.2 `PipelineStepCondition` (H.3) — becomes a tree (E2)

| Table | Schema ref | Change | Fields (type) |
|---|---|---|---|
| **`PipelineStepCondition`** | H.3 (columns add) | add | `ParentConditionId Guid?` (self-FK; null = root of this step's condition tree). `GroupOp string(3)?` **[VC:enum]** (`and`\|`or`; set only on a **group node** — a row with children and no `ConditionType`). `Order int` (already exists — order within parent). |

A condition-tree **node** is either:
- **Leaf** — `ConditionType` set (`user_role`\|`random`\|`var_compare`\|`cooldown`, unchanged from §6.2 of
  `commands-pipelines.md`), `GroupOp` null, no children. `Negate` (existing column) inverts it.
- **Group** — `ConditionType` null, `GroupOp` set, 1..N children (leaves or nested groups) ordered by `Order`.
  `Negate` on a group inverts the group's combined result (gives `NOT (...)` without a third `GroupOp` value).

Evaluation: post-order — evaluate every child, combine by `GroupOp` (`and`=all true, `or`=any true), apply `Negate`.
**`(A and B) or (C and not D)`** = root `or` group → child 1: `and` group {leaf A, leaf B} → child 2: `and` group
{leaf C, leaf D with `Negate=true`}. Arbitrary depth, no cap beyond the existing tree-depth validation
(`ValidateAsync` in control-flow D2/§3, reused here — a condition tree depth > 6 is rejected, `condition_tree_too_deep`,
matching the §6.3 template recursion-depth-8 convention minus 2 for headroom).

A **leaf-only step keeps working unmodified** — a step with one `ConditionType` row and no `ParentConditionId` is a
1-node tree; this is exactly today's flat-AND-list shape when several leaves share `ParentConditionId=null` with an
implicit `GroupOp=and` root synthesized at read time for legacy rows (§6.1).

### 1.3 `PipelineTrigger` (NET-NEW, E1)

| Table | Schema ref | Change | Fields (type) |
|---|---|---|---|
| **`PipelineTrigger`** | H — new child of H.1 | add table | `Id Guid` PK; `PipelineId Guid` FK→Pipelines; `BroadcasterId Guid`; `Kind string(20)` **[VC:enum]** `command`\|`event`\|`timer`\|`manual`\|`webhook`; `Order int` (display order, unique `(PipelineId,Order)`); `ConfigJson Dictionary<string,object?>` **[VC:JSON]** — kind-specific: `command` → `{ string Name, List<string>? Aliases, string PrefixMode, string? CustomPrefix, string MatchMode, string? MatchPattern }` (mirrors `Command`'s own trigger columns — a pipeline-tier command's `N` trigger phrases live here, not on `Commands`, closing gap #17 without a code-level alias hack); `event` → `{ string EventType }`; `timer` → `{ Guid TimerId }`; `webhook` → `{ Guid EndpointId }`; `manual` → `{}`; `IsEnabled bool`. |

`Pipeline.TriggerKind` (H.1) is **deprecated in place, not dropped** — kept as a **denormalized summary** column
(`"command"` when all triggers are `command`, `"mixed"` when triggers span kinds) for list-view display only; the
`PipelineTrigger` rows are truth. Migration §6.1.

A **T2/T3 `Command`** (per `commands-pipelines.md` §1 `Command.PipelineId`) still owns its **own** single trigger
phrase row directly on `Command` (`Name`/`Aliases`/`PrefixMode`/`MatchMode`) — that path is unchanged. `PipelineTrigger`
is for pipelines that are **not** wrapped by exactly one `Command` row: a pipeline fired by several independent chat
phrases (Fight+Hit), or by a mix of a command **and** an event **and** a timer, registers each as its own
`PipelineTrigger`. **Rule (avoids double-registration):** when a `Command.PipelineId` points at a pipeline, the
dispatcher's chat-trigger resolution goes through `ICommandService.ResolveAsync` (`commands-pipelines.md` §3.2.1) as
today; `PipelineTrigger` rows of `Kind=command` are a **second, independent** registration path used only when a
pipeline has no wrapping `Command` (a bare multi-trigger pipeline, e.g. authored straight from the pipeline editor
without a Commands-list entry). The editor enforces this at save: a pipeline cannot carry both a wrapping `Command`
and its own `Kind=command` `PipelineTrigger` rows (`ambiguous_trigger_ownership`).

### 1.4 `PipelineRunState` (NET-NEW, E3 — persisted long-lived runs)

| Table | Schema ref | Change | Fields (type) |
|---|---|---|---|
| **`PipelineRunState`** | H — new, tenant-scoped, no soft-delete | add table | `Id Guid` PK (= the run's `ExecutionId`, matches `PipelineExecution.Id`-adjacent — see below); `BroadcasterId Guid`; `PipelineId Guid`; `Status string(20)` **[VC:enum]** `running`\|`suspended`\|`completed`\|`failed`\|`cancelled`\|`expired`; `SuspendedAtStepId Guid?` (the `wait_for_event` step currently blocking); `WaitingForEventType string(100)?`; `WaitingForMatchJson Dictionary<string,object?>?` **[VC:JSON]** (correlation predicate, e.g. `{ "payload.RewardId": "{{trigger.RewardId}}" }` resolved at suspend time into literal values — never re-templated at resume, so a later unrelated event with the same shape can't accidentally resolve a stale token); `ResumeDeadline DateTimeOffset?` (suspend timeout); `VariablesJson Dictionary<string,string>` **[VC:JSON]** (the run's full variable bag at suspend time); `LoopCursorsJson List<LoopCursor>?` **[VC:JSON]** (`{ StepId, Mode, Index, ListSnapshot? }` per currently-open loop, innermost last — lets a resumed run re-enter the right loop iteration); `CallStackJson List<CallFrame>?` **[VC:JSON]** (`{ PipelineId, ReturnStepId, CallerRunStateId }` per open `run_pipeline inline` frame — supports suspending **inside** a called sub-pipeline); `TriggeredByUserId Guid`; `TriggeredByDisplayName string(255)`; `CreatedAt`; `SuspendedAt DateTimeOffset?`; `ResumedAt DateTimeOffset?`; `CompletedAt DateTimeOffset?`. Indexes: `(Status, ResumeDeadline)` for the timeout sweep; `(BroadcasterId, WaitingForEventType, Status)` for event-match lookup. |

A run that never hits `wait_for_event` **never gets a `PipelineRunState` row** — it executes exactly as today
(in-memory, fire-and-forget, `PipelineExecution` append-only log at the end). `PipelineRunState` is created **only**
the first time a run suspends, and is the durable resume point; `PipelineExecution` (H.4) remains the append-only
audit log and gets one terminal row per run **regardless** of how many suspend/resume cycles it went through
(`StepLogsJson` accumulates across resumes, capped as today).

### 1.5 Migration losslessness (E9) — see full detail in §6

The three additive changes (E1 trigger table, E2 condition-tree columns, E3 run-state table) are **all additive
columns/tables** — no column is removed or retyped, so every existing `Pipeline`/`PipelineStep`/`PipelineStepCondition`/
`Command` row keeps working unmodified until touched by the new editor. §6 gives the exact upcast.

---

## 2. Execution semantics

### 2.1 Evaluation order

Unchanged from `pipeline-control-flow.md` §3: depth-first walk of `PipelineStep` by `(ParentStepId, Order)`. This
spec's additions plug into the same walk:
- An `if` step's condition-tree root (§1.2) is evaluated **before** descending into `then`/`else` children — same
  point in the walk where the old single-`PipelineStepCondition` check happened, just richer.
- `try` (§1.1) walks its `body` (`then` slot) children; on any child's `ActionResult.Success=false` or an unhandled
  exception, it **stops the body** and walks `catch` (`else` slot) children instead, then continues past the `try`
  block. A `try` with no `catch` children behaves as "swallow the failure, continue past the block" (still logs
  `PipelineStepFailedEvent`).
- `detached_step` dispatches its single child action via `Task.Run` scoped to a background-tracked handle (not
  awaited), immediately continues to the next sibling. The detached action's own failures are logged
  (`PipelineStepFailedEvent`) but never fail the parent run and are never awaited by it.
- `wait_for_event` (§2.3) is a **leaf action** that, when reached, either resolves immediately (a matching event is
  already queued — see §2.3.2) or suspends the run.

### 2.2 Variable scopes

Four scopes, each backed by an existing or newly-named store — **no new storage mechanism**, this section just names
the ladder so pipeline authors and the engine agree on lookup order and lifetime:

| Scope | Backing | Lifetime | Template namespace |
|---|---|---|---|
| **Run** | `ActionContext.Variables` (in-memory bag; persisted into `PipelineRunState.VariablesJson` only across a suspend) | one execution (incl. all its suspend/resume cycles and its `inline` sub-pipeline calls, which **share** this bag — control-flow D4) | unnamespaced `{{myvar}}` (author `set_variable` keys) + the seeded `user.*`/`channel.*`/`stream.*`/`args.*`/`loop.*`/`switch.*`/`call.*` namespaces |
| **User** | `ViewerDatum` (G.14, `per-viewer-data.md`) via `set_viewer_data`/`adjust_viewer_data` | forever (per broadcaster+viewer), until explicitly overwritten or GDPR-erased | not template-namespaced directly — read back via a fresh `set_viewer_data`-adjacent lookup action into a run variable (existing actions, §6.1 of `commands-pipelines.md`) |
| **Channel** | `NamedCounter` (G.4) via `set_counter`/`adjust_counter` | forever (per broadcaster), until explicitly overwritten | `{{count.<name>}}` |
| **Global** | none — **deliberately not offered.** A cross-channel variable would violate tenant isolation (every table is `BroadcasterId`-scoped by design, `platform-conventions.md`). "Global" in old-bot terms (e.g. a bot-wide setting) is config, not a pipeline variable — authors needing cross-channel state use `run_pipeline` against a shared pipeline definition template, not a shared variable. |

A `run_pipeline detached` call gets a **fresh Run scope** (new `ActionContext.Variables`, seeded only from `Args`,
per control-flow D4) — it cannot read the caller's Run-scope variables, only User/Channel scope and its own args.
`inline` calls **share** the caller's Run scope (existing behavior, unchanged) plus receive `Args` bound into named
parameters (§2.5).

### 2.3 Wait-for-event / resume-later (E3)

**New action `wait_for_event`:**

| Action `Type` | Parameters | Behavior |
|---|---|---|
| `wait_for_event` | `{ string EventType, Dictionary<string,string>? MatchOn, int TimeoutSeconds, string? OnTimeout (continue\|abort) }` | suspends the run until a matching event fires or `TimeoutSeconds` elapses (max 24h, same clamp as §10 scheduling) |

#### 2.3.1 Suspend

When the engine reaches a `wait_for_event` step in a run that has not already resolved a matching queued event
(§2.3.2): it **resolves `MatchOn`'s templated values against the current Run scope immediately** (so `{{trigger.RewardId}}`
captured at suspend time, never re-evaluated later against a different context), snapshots the run (`VariablesJson`,
`LoopCursorsJson` for every currently-open loop the walk is inside, `CallStackJson` for every open `inline` frame),
upserts a `PipelineRunState` row (`Status=suspended`, `SuspendedAtStepId`=this step, `WaitingForEventType`,
`WaitingForMatchJson`=the resolved literal values, `ResumeDeadline = now + TimeoutSeconds`), and **releases the
concurrency slot** the run was holding (`IPipelineEngine.GetActiveCountForChannel` — a suspended run does not count
against the channel's active-run cap, unlike `wait`'s in-memory hold per `commands-pipelines.md` §10's own rationale
for why `wait` can't do this job).

#### 2.3.2 Resume

**Resume trigger — the same event bus every `EventResponse`/EventSub translator already publishes to
(`IEventBus`, `platform-conventions.md`).** A new `PipelineRunResumeListener` (Infrastructure, hosted, subscribes to
`IEventBus`) checks every published event against `(BroadcasterId, EventType, Status=suspended)` rows; for each
candidate row it string/value-matches the event's fields against `WaitingForMatchJson` (same `IValuePredicate`
equality comparator as §6.2's `var_compare` — exact match per key, all keys must match). On a match: loads the
`PipelineRunState`, restores `Variables`/loop cursors/call stack into a fresh `ActionContext`, re-admits the run
through the **normal concurrency gate** (a resume can be deferred by admission control same as a fresh run — resume
is not exempt from `MaxTotalActions`/`MaxRuntime`, which both **continue accumulating from where they left off**,
persisted alongside the snapshot), marks the row `Status=running`, `ResumedAt=now`, and continues the depth-first
walk from the step **after** `SuspendedAtStepId`. Binds the resuming event's fields into `{{event.*}}` for that one
step's continuation (mirrors the `webhook.*`/`payload.*` seeding pattern, §6.3 of `commands-pipelines.md`).

If a matching event is **already sitting** on the bus's short-lived recent-event buffer at the instant a run reaches
`wait_for_event` (race: event arrived a few ms before the step executed) — the listener's own subscription means this
race is closed by construction: the run always creates its `PipelineRunState` row (suspend) **before** any resume
lookup can find it, so "immediate resolve" is not a separate code path, it's just a suspend immediately followed by
a resume on the next event-bus tick. No special-cased fast path, no duplicate logic.

#### 2.3.3 Restart

On process restart, in-memory runs that had **not yet** hit `wait_for_event` are lost (unchanged from today — a
fire-and-forget run mid-execution at crash time is not recoverable, matching `pipeline-control-flow.md`'s existing
"no per-run state table" note for non-suspended runs). Runs that **had** suspended are durable: `PipelineRunState`
rows with `Status=suspended` survive in the database untouched; `PipelineRunResumeListener` re-subscribes on boot and
resumes normally the next time a matching event arrives — **no explicit "replay on restart" step needed**, because
the row itself IS the persisted wait state (this is the direct fix for old-bot gap #7's "replays 'already live' state
on bot restart" requirement — the state was never only in-memory to begin with).

#### 2.3.4 Timeout

A **5-second sweeper** `PipelineRunTimeoutService` (Infrastructure, `BackgroundService`, same shape as
`ScheduledPipelineExpiryService` in `commands-pipelines.md` §10) ticks over `Status=suspended AND ResumeDeadline <
now` rows: `OnTimeout=continue` resumes the run from the step **after** `wait_for_event` with `{{event}}` unbound
(empty), so the author can branch on "did it actually resolve" via an `if` checking a sentinel variable the author
sets right after; `OnTimeout=abort` (default) marks the row `Status=expired`, journals `PipelineExecutionCompletedEvent`
with `Outcome=TimedOut`, and does not continue the walk.

#### 2.3.5 Cancellation

`IPipelineEngine.CancelAllForChannelAsync` (existing, control-flow-adjacent) additionally marks every
`Status=suspended` row for that channel `Status=cancelled` (a stream-offline cancel must not leave a Lucky-Feather
cycle waiting forever for an event that will never come once the channel resets). Manual cancel: `DELETE
…/pipelines/runs/{runStateId}` (§4.6's blast-radius surface links here for a live-run kill).

### 2.4 Loop iteration scope (E6)

A loop iteration sees: the enclosing Run-scope bag (loops do **not** get their own variable scope — `set_variable`
inside a loop body mutates the shared Run bag, matching today's linear-step behavior) plus the loop-local read-only
bindings `{{loop.item}}`/`{{loop.index}}`/`{{loop.count}}` (control-flow §4, unchanged) and, new here,
**`{{loop.previous_item}}`** (the prior iteration's `{{loop.item}}`, empty on iteration 0) — the concrete primitive
that closes gap #3's "per-iteration wait duration computed from loop state" (Raid's `{{loop.item}} - previousItem}}`
becomes `{{expr:{{loop.item}} - {{loop.previous_item}}}}` via the arithmetic template function, §3.3).

**Loop timeout guard (E6):** `loop`'s `BlockConfigJson` gains `MaxLoopRuntimeSeconds int?` (nullable = inherit the
whole-run `MaxRuntime`) — lets an author cap one loop tighter than the run (e.g. a countdown loop capped at 120s even
inside a run whose `MaxRuntime` is 300s for other reasons). Breach aborts the **run** (not just the loop) with
`Outcome=AbortedBudget`, `Reason=loop_runtime_exceeded` — consistent with control-flow D6's "any cap breach aborts
the run cleanly," no partial-loop continuation semantics to keep the termination guarantee simple.

### 2.5 Sub-pipeline argument passing and return value (E4)

`run_pipeline`'s `Args` (control-flow §4, today `IReadOnlyList<string>?`, positional) gains **named binding**: the
callee pipeline declares its expected parameter names on `Pipeline.ParameterNamesJson List<string>?` **[VC:JSON]**
(new nullable column on H.1, additive) — when set, `Args[i]` binds into the callee's Run-scope variable
`ParameterNamesJson[i]` (so the callee references `{{amount}}` instead of a positional `{{args.1}}`); when the
callee declares no parameter names, `Args` bind positionally into `{{args.1}}..{{args.N}}` exactly as a chat-command
invocation does today (one consistent binding rule, no special pipeline-call-only syntax).

**Return value — new action `return_value`:**

| Action `Type` | Parameters | Behavior |
|---|---|---|
| `return_value` | `{ string Value }` | renders `Value` (template) and sets it as the current run's return value; **implicitly also `stop`s** the pipeline (a return ends execution — matches every language's `return` semantics, avoids an author forgetting a trailing `stop`) |

The caller receives it in `{{call.result}}`, bound **only** for the step immediately following the `run_pipeline`
step that produced it (same lifetime pattern as `{{event.*}}` after a resume, §2.3.2) — and, if the caller wants it
kept, an explicit `set_variable` copies it into a durable Run-scope name. `detached` calls **never** populate
`{{call.result}}` (no return channel to an independent run — matches control-flow D4's "independent run" semantics;
an author needing a detached call's outcome uses `wait_for_event` against a domain event the detached pipeline
itself publishes, or `Wait=true` to block for the detached run's `PipelineExecution` outcome via a new lightweight
poll the engine performs internally, not exposed as a separate primitive).

### 2.6 Recursion depth limit (E5)

`MaxRecursionDepth` (control-flow D4/D6, tier-scaled) defaults to **8** for the free/self-host baseline (matches the
§6.3.2 template `all`/`any` cap-of-8 convention and the §6.3 recursion-depth-8 convention — one consistent "8" ceiling
across the whole spec family, easy to remember, generous for any real automation). Static-cycle validation
(`ValidateAsync`, control-flow §3) additionally walks the `wait_for_event`-suspended call graph: a pipeline that
calls itself **through** a suspend/resume boundary (A calls B inline, B suspends, on resume B calls A inline) is
still a static cycle in the **call graph** (which pipeline can call which), independent of the runtime suspend state,
so it is caught at save time exactly like a same-tick cycle — the suspend doesn't hide it.

### 2.7 Global termination budget interplay

`MaxTotalActions`/`MaxRuntime` (control-flow D6) **persist across suspend/resume** (§2.3.2) — a run that suspends for
3 minutes waiting on an event does not get its wall-clock or action budget reset on resume; `MaxRuntime` is measured
as **cumulative running time** (`SuspendedAt`→`ResumedAt` gaps excluded from the clock — a run "waiting" isn't
"running", so a Lucky Feather cycle that legitimately waits 5 minutes doesn't burn its runtime budget sitting idle;
only the time actually executing steps counts). This is the one runtime-semantics fork the owner would want stated
explicitly: **wall-clock while suspended does not count against `MaxRuntime`**, only active execution time does —
otherwise no long-lived automation could ever exist under a sane cap.

---

## 3. The primitive set — full catalogue mapped to the gap list

### 3.1 New block-kinds (this spec, beyond control-flow's five)

| `BlockKind` | Closes gap # |
|---|---|
| `try` (§1.1, §2.1) | #13 (per-block error handling) |
| `detached_step` (§1.1, §2.1) | #14 (detached single action, not a whole sub-pipeline) |

### 3.2 New actions (this spec, beyond control-flow's three)

| Action `Type` | Params | Closes gap # |
|---|---|---|
| `wait_for_event` (§2.3) | `{ EventType, MatchOn, TimeoutSeconds, OnTimeout }` | #2 |
| `return_value` (§2.5) | `{ Value }` | #4 (sub-pipeline return, needed for Todo/SongRequest/Sus-style helper decomposition) |
| `update_reward` (NET-NEW) | `{ string RewardRef, string? Title, long? Cost, string? Prompt, bool? IsPaused, string? Description }` — templated, typed failure (`RewardNotFound`\|`RewardNotManaged`\|`RateLimited`), broker-pattern (no token in config, resolves via the tenant's Helix client) | #9 (Lucky Feather's live price/prompt rewrite) |
| `record_add` / `record_list` / `record_update` / `record_remove` (NET-NEW, §3.4) | see §3.4 | #8 (per-user/channel list-of-records) |
| `list_find` (NET-NEW) | `{ string ListVar, string Field, string Op (contains\|equals\|iequals\|startswith\|matches), string Value, string ResultVar }` | #19 (fuzzy/contains lookup over a list variable — Voice's partial-name search) |

### 3.3 Template function additions (§6.3 of `commands-pipelines.md` — new function families, same resolver)

| Function | Form | Closes gap # |
|---|---|---|
| JSON-path extraction | `{{json.<path>:<expr>}}` — e.g. `{{json.settings.osConfig.win95.timings.glitch:{{widget.settings}}}}`, dot/bracket path into a JSON-string-valued variable, unknown path ⇒ empty string (fail-closed, non-fatal, matches every other unknown-token rule) | #5 |
| Arithmetic | `{{expr:<infix expression over resolved tokens>}}` — `+ - * / min max clamp()`, e.g. `{{expr:{{count.score}} + {{random.number:-5:5}}}}`, `{{expr:clamp({{count.score}}, 0, 100)}}`; numeric-only, non-numeric operand ⇒ the whole expression renders empty (fail-closed) | #6 |
| Weighted/filtered random over a list variable | `{{random.from:<list-var>}}` (uniform pick) / `{{random.weighted:<list-var>:<weight-field>}}` (weighted pick, list of `{value, weight}` objects) — distinct from the existing static-array `random_response` action; this is the **template-function** form usable inline, e.g. inside a `set_variable` picking a filtered JSON array's surviving element | #4 |
| `wait_until` | new **action** (not template fn) `{ string TimestampExpr }` — waits until the templated absolute instant (`{{expr:{{run.startedat}} + 90s}}` style; instant arithmetic reuses `{{expr:…}}` on duration-normalized instants) rather than a relative duration; distinct from `wait` | #7 |
| String-manipulation functions | `{{str.upper:<expr>}}`, `{{str.lower:<expr>}}`, `{{str.alternate:<expr>}}` (per-character alternating case), `{{str.reverse:<expr>}}`, `{{str.truncate:<expr>:<n>}}`, `{{str.length:<expr>}}` (returns a number, usable inside `{{expr:…}}` or an `if.*` numeric predicate), `{{str.regexreplace:<expr>:<pattern>:<replacement>}}` (via the shared `IRegexMatcher`, NonBacktracking, same ReDoS policy as §6.4) | #15 (BSOD's length-conditional SSML rewrite, Mock's alternating case) |
| Templated wait duration | `wait`'s existing `{ int? Seconds, int? Milliseconds }` (§6.1) is **relaxed** to accept a template string coercible to int (`{ string? SecondsExpr }` alt form) — so `wait { SecondsExpr: "{{expr:{{tts.durationms}} / 1000}}" }` feeds a prior action's `Output`/`ResultVariable` straight into the wait. No new action; existing `wait` widened. | #16 |
| Multiple named placeholders | already satisfied by existing `set_variable` (§6.1, unchanged) + `random_response`'s template substitution reading **any** Run-scope variable, not just fixed context fields — **no gap, confirmed by inspection**: `random_response`'s `Messages` are rendered through the same `ITemplateEngine.Render(string, ActionContext)` (§6.3) that resolves arbitrary `{{myvar}}`/`{{myvar2}}` keys set by prior `set_variable` steps. | #18 (confirmed satisfied, not a build item) |
| Registry introspection | new **read-only pseudo-namespace** `{{commands.list:<separator>}}` (comma/newline-joined list of trigger names the **caller's own role** can currently fire — dispatcher pre-seeds via `ICommandDispatcher`'s existing resolution path filtered by the triggering user's level, same seed-before-render pattern as every Helix/economy token) + `{{commands.describe:<name>}}` (that command's `Description`) — both **data**, not a new action, consumed by an authored `!commands`/`!help` pipeline exactly like any other token | #20 |

### 3.4 Per-user/per-channel list-of-records primitive (gap #8)

New entity **`ViewerRecordList`** (Domain — schema owner: this spec extends `per-viewer-data.md`'s `ViewerDatum`
family with a list-shaped sibling rather than overloading the scalar `ViewerDatum`):

| Table | Change | Fields |
|---|---|---|
| **`ViewerRecordList`** | add table (tenant+viewer scoped, soft-delete) | `Id Guid`; `BroadcasterId Guid`; `ViewerUserId Guid?` (null = **channel-scoped** list, e.g. banned-songs; set = **per-user** list, e.g. Todo items); `ListKey string(50)` (author-chosen name, unique `(BroadcasterId, ViewerUserId, ListKey)`); `ItemsJson List<Dictionary<string,object?>>` **[VC:JSON]** (ordered array of structured records — each item an author-defined field bag); `ConfigSchemaVersion int` |

Actions (§3.2, category `data`):

| `Type` | Params | Behavior |
|---|---|---|
| `record_add` | `{ string ListKey, Dictionary<string,object?> Fields, string? Target }` | appends one item to the (viewer- or channel-scoped) list; returns the item's **0-based index** as `Output` |
| `record_list` | `{ string ListKey, string ResultVar, string? Target, string? FilterField, string? FilterValue }` | reads the list (optionally filtered) into `ResultVar` as a JSON-array-string variable, consumable by `loop foreach` / `list_find` / `{{json.*}}` |
| `record_update` | `{ string ListKey, int Index, Dictionary<string,object?> Fields, string? Target }` | merges `Fields` into the item at `Index` (author resolves the index via `record_list` + position-in-filtered-list, matching old-bot Todo's exact numbering-≠-DB-id shape at `Todo.cs:96-260`) |
| `record_remove` | `{ string ListKey, int Index, string? Target }` | removes the item at `Index`, **compacts** (later indexes shift down — matches old-bot's position-based semantics, not an id-stable delete) |

This single table + four actions covers Todo (#11: per-user list, CRUD, position numbering), Voice history (#12),
Lurk/Unlurk id-set (#18: a channel-scoped list with one field `{userId}`, membership test via `list_find`),
song-request history (#13), banned-song list (#23) — five behaviours, one primitive, exactly the "generic composable
primitive, not five bespoke tables" rule.

### 3.5 Typed action failure reasons (gap #12 — BUG fix)

Every provider-calling action's `ActionResult` (§4.4 of `commands-pipelines.md`, unchanged record shape) gains a
**`FailureCode string?`** field (additive — `ActionResult(bool Success, string? Output, string? ErrorMessage,
string? FailureCode, IReadOnlyDictionary<string,string>? VariablesSet, bool StopPipeline)`). Every provider action
(Twitch raid/ban/timeout/shoutout, `update_reward`, music, OBS-adjacent — wherever such actions land per their owning
subsystem spec) **must** populate a **declared enum** of failure codes (e.g. `raid`'s: `AlreadyRaiding`\|
`TargetNotFound`\|`SelfRaid`\|`RateLimited`\|`Unknown`) rather than a free-text `ErrorMessage` a condition would have
to substring-match. A new condition operand form on `var_compare`: `LeftOperand = "{{last.failurecode}}"` (the prior
step's `FailureCode`, seeded into Run scope automatically after every action executes, like `{{call.result}}`) lets
an author branch on it (`equals AlreadyRaiding`) — **never** on `ErrorMessage` text. This is a **direct requirement
on every provider-action spec**, not new plumbing here; this spec names the contract and the seeded variable.

### 3.6 Debounce/latch primitive (gap #11)

**No new primitive needed** — closes by composition of existing/this-spec primitives, confirmed by walking the
concrete case: Lucky Feather's "only the first steal in a window starts the hold" = `adjust_counter` (existing,
atomic) on a per-cycle flag key + an `if` gate (`var_compare` `equals 0` before incrementing) wrapping the
`wait_for_event`/`schedule_pipeline` chain-start. **Named pattern, not a new block**: the editor's block palette
(§4.2) ships this as a **documented recipe** (`if {{count.<key>}} equals 0 → set_counter <key> 1 → start the
timed chain`), the same way Streamer.bot ships "sub-action patterns" without a dedicated language construct. No
schema/action delta.

### 3.7 Full 26-behaviour → primitive map

| # | Behaviour | Primitives used | Status |
|---|---|---|---|
| 1 | Fight/Hug 5-way | `PipelineTrigger` ×2 (E1), condition-tree `if`/`switch` (E2, D2), `record_list`/live-lookup existing action, `random_response`, TTS action (existing per `commands-pipelines.md` — confirm exists, out of this spec's scope) | covered |
| 2 | Hug | same as #1 | covered |
| 3 | BSOD | `{{json.*}}` (§3.3), `{{random.from:}}`/`{{random.weighted:}}` (§3.3), `{{str.regexreplace}}`/`{{expr:}}` (§3.3), templated `wait` (§3.3), `try`/`catch` (§1.1/§2.1) for refund-on-failure, Spotify pause/resume (existing music actions per `commands-pipelines.md` §6.1 — pause/resume additions are that subsystem's, referenced not owned here) | covered (pending music subsystem's own pause/resume action additions — flagged, not this spec's gap) |
| 4 | Raid | `wait_until` (§3.3), `loop foreach` + `{{loop.previous_item}}` (§2.4), `detached_step` (§1.1) for OBS switch, `FailureCode` (§3.5) for "already raiding" | covered |
| 5 | Lucky Feather steal | `update_reward` (§3.2), `record_list`-adjacent "latest record" via `record_list`+filter, multi-placeholder `random_response` (confirmed satisfied §3.3) | covered |
| 6 | Lucky Feather config-change | **new trigger kind** — `PipelineTrigger.Kind=event` bound to reward-lifecycle event types (`reward.enabled`/`reward.disabled`/`reward.paused`/`reward.resumed` — event-catalogue additions owned by the rewards subsystem, referenced here as the `event` trigger kind already generic in E1/H.1) | covered (reward-lifecycle event *types* are the rewards subsystem's own addition; the *trigger kind* to bind them is generic and already exists) |
| 7 | Lucky Feather timer cycle | `wait_for_event` (E3) **or** two chained `schedule_pipeline` calls (existing §10) — this spec's `wait_for_event` is the generalized fix control-flow's can't-be-generic #3 called for | covered |
| 8 | Voice Swap | `record_update` ×2 (§3.4) for the cross-user swap, `schedule_pipeline` with `DedupeKey` (existing §10, already spec'd — confirmed wired) | covered |
| 9 | `!sus` | `{{expr:}}` (§3.3) for the score formula, nested `if`/`switch` (existing D2) for tier buckets, `random_response` | covered |
| 10 | `!stats` | existing parallel DB-read actions (economy/analytics tokens, `commands-pipelines.md` §6.3) — no new primitive | covered |
| 11 | Todo | `record_add`/`record_list`/`record_update`/`record_remove` (§3.4), `switch` on `{{args.1}}` (existing D2) | covered |
| 12 | Voice | `record_list`, `list_find` (§3.2/§3.4) for fuzzy match, `loop foreach` (existing D3) for per-locale grouping | covered |
| 13 | SongRequest | `record_add` (history), existing music actions, `{{str.*}}` (§3.3) for URL-vs-search parsing via `{{expr:}}`+string fns | covered |
| 14 | `!ratio` | `{{expr:}}` (§3.3), nested `if`/`switch` (existing D2), `random_response` | covered |
| 15 | `!quote` | existing index-based random DB read — no new primitive (a data-access detail inside an existing `random_response`-adjacent read action, out of scope) | covered |
| 16 | ~20 TTS-bit commands | `{{random.number:min:max}}` (existing per CLAUDE.md), `random_response`, single canonical `speak`/TTS action (existing, confirm one canonical action per `commands-pipelines.md` — not a gap this spec introduces) | covered |
| 17 | `!mock` | `record_list` (last message — needs the chat-history read action, existing/adjacent), `{{str.alternate}}` (§3.3) | covered |
| 18 | Lurk/Unlurk | `record_add`/`record_remove`/`list_find` (§3.4) as a channel-scoped id-set | covered |
| 19 | `!wrongsong` | `record_list` (most-recent filter), `record_remove`, existing music skip action | covered |
| 20 | `!leaderboard` | existing economy leaderboard tokens/actions — no new primitive | covered |
| 21 | `!setpronoun` | existing `set_viewer_data` (G.14) + a new **allow-list validation condition** `var_compare` against a `record_list`-backed allow-list (no raw SQL; the old-bot shortcut, gap-adjacent to #19, closed by `list_find`) | covered |
| 22 | `!followage` | existing Helix follow-lookup token (`{{target.followage}}`, §6.3 of `commands-pipelines.md`) — no new primitive | covered |
| 23 | Banger/BanSong | `record_add`/`list_find` (§3.4) for the ban-list, existing music playlist actions | covered |
| 24 | `!skip` | existing permission-scoped condition (`user_role`) + `record_list` filtered by requester | covered |
| 25 | `!commands` | `{{commands.list:}}` (§3.3) | covered |
| 26 | `!help` | `{{commands.describe:}}` (§3.3) | covered |

**26/26 covered.** No bespoke block anywhere in the map — every row composes primitives from control-flow.md,
commands-pipelines.md §6, or this spec's §3.

---

## 4. The editor — nested block-list interaction design

### 4.1 Canvas shape

A pipeline renders as a **vertically stacked, indented list** — exactly the shape of a code editor's outline view,
not a canvas. Each row is one `PipelineStep`. Indentation level = tree depth (`ParentStepId` chain length). A block
step (`if`/`switch`/`loop`/`random_branch`/`try`/`detached_step`) renders as a **header row** (its `BlockKind` +
summary of `BlockConfigJson`, e.g. `if user.ismod`) followed by its indented children, followed by a **footer row**
(a thin "end if" cap, collapsible — clicking the header collapses the whole subtree to one line for deep-nesting
readability, gap addressed explicitly by the owner's "how deep nesting stays readable" requirement). Multiple
`PipelineTrigger` rows (E1) render as a **fixed header strip above the tree** — a chip per trigger with an add
(`+`) affordance, never inline in the block list (triggers are not steps).

### 4.2 Add / remove / reorder

- **Add:** a `+` affordance at the end of any block's child list (or the root) opens a **block palette** — grouped by
  category (`flow`: if/switch/loop/random_branch/try/detached_step/run_pipeline/wait_for_event/return_value; `chat`:
  send_message/send_reply/…; `data`: set_variable/record_*/…; `moderation`: ban/timeout/…; `music`; `provider`).
  Selecting an item inserts a default-configured step at that position, immediately opens its **inspector panel**
  (right-side, not modal — keeps the tree visible) for parameter entry.
- **Remove:** a per-row overflow menu (`⋯`) with **Delete** — deleting a block step deletes its entire subtree; the
  confirm dialog states the **count** of descendant steps being removed (e.g. "delete this `if` and 6 steps inside
  it?") per the consequence-visibility law (§4.6).
- **Reorder:** drag the row's leading grip handle. Dropping **between** two siblings at the same indent reorders
  (`Order` renumber, transactional). Dropping **onto** a block header (highlighted drop-zone while dragging) moves
  the step **into** that block as its last child — indent changes to match. Dropping at the **left edge** of an
  indent guide (a thin vertical line per nesting level, standard code-editor affordance) moves the step **out** one
  level, becoming a sibling of its former parent. Both cross-level moves are one drag gesture; the drop-target
  highlight shows the resulting indent live before release (no separate "confirm move" step — reversible via undo,
  §4.5).
- **Switch/random_branch children:** `switch_case`/`random_case` rows render as sub-headers directly under their
  parent (not draggable **out** of the switch/random_branch — a case has no meaning outside its parent, so the drop
  affordance for those rows only accepts reordering among siblings or a delete, never an out-of-block drag).

### 4.3 Condition-tree sub-editor (E2)

Opened from an `if`/`switch_case`(guard)/loop-`while` step's inspector as an **inline expandable panel**, not a
separate screen. Renders the same nested-indent idiom as the block list, one level deeper in visual weight (a
lighter card background) so it reads as "inside this step," never mistaken for the outer tree:

- A **group row** shows its `GroupOp` as a toggle chip (`AND`/`OR`, click to flip — applies to the group's own
  children only). A `+ Add condition` / `+ Add group` pair at the end of each group's child list.
- A **leaf row** shows `ConditionType` + operands as inline editable fields (matches the existing flat-condition
  editor's field set — `user_role` picker, `var_compare` operator dropdown + operand text with template-token
  autocomplete, etc.) plus a `NOT` toggle.
- **Grouping/re-grouping:** multi-select two or more sibling rows (checkbox on hover/select-mode) → **Group**
  action wraps them in a new group node at that position with a chosen `GroupOp`; **Ungroup** on a group with the
  cursor inside it lifts its children up to the parent's level and deletes the now-empty group node. This is the
  concrete answer to "groupable/re-groupable" — two explicit commands, not free-form drag-to-nest (conditions nest
  shallow enough — depth ≤6 — that drag-based regrouping would be more error-prone than click-to-group for this
  specific sub-editor, unlike the block list where drag is the primary and only sane input for deep step trees).
- Live-rendered **plain-language summary** above the tree (`"runs when: (A and B) or (C and not D)"`) recomputed on
  every edit — the per-control "what does this do" requirement (§4.6) applied specifically to conditions, since a
  condition tree is the hardest artifact to read back from raw structure.

### 4.4 Readability at depth

- **Collapse/expand per block** (§4.1) — collapsed state persists per-pipeline in the editor's local UI state (not
  persisted server-side; a fresh load starts fully expanded).
- **Indent guides** — a thin vertical line per nesting level (standard code-editor convention), colored by depth
  modulo a small palette so depth 1/4/7 share a color but adjacent levels never do (visually separable without a
  rainbow at depth 8).
- **Minimap-adjacent step counter** — the pipeline header shows total step count and current `MaxStepCount`/
  `MaxTotalActions` cap (from the tier entitlement, §3.13a of `commands-pipelines.md`) as a live `N / cap` — ties
  directly into the consequence-visibility law (an author sees they're approaching the cap before hitting a save
  error).
- **Breadcrumb on the inspector panel** — when editing a deeply nested step's params, the inspector header shows the
  ancestor chain (`Pipeline → if user.ismod → loop foreach songs → send_message`) so context is never lost purely
  from indentation, which becomes hard to visually count past ~4 levels.

### 4.5 Keyboard support

| Key | Action |
|---|---|
| `↑`/`↓` | move focus to prev/next visible row (respects collapsed state) |
| `→`/`←` | expand/collapse the focused block row |
| `Enter` | open the focused row's inspector panel |
| `Delete`/`Backspace` | delete the focused row (with the same descendant-count confirm as the mouse path) |
| `Ctrl+↑`/`Ctrl+↓` | reorder the focused row among its current siblings (no cross-level move — keyboard reorder stays same-level for predictability; cross-level moves are drag-only or via the inspector's explicit "move into/out of" menu item, §4.2's Delete-menu sibling) |
| `Tab`/`Shift+Tab` (on a non-block leaf row) | move the focused step one level deeper into the preceding sibling block (if it's a block) / one level out to the parent's level — the keyboard equivalent of the drag-to-nest gesture |
| `Ctrl+D` | duplicate the focused row (and its subtree, if a block) as the next sibling |
| `Ctrl+Z`/`Ctrl+Shift+Z` | undo/redo — **every** structural edit (add/remove/reorder/nest) and every inspector field change is one undo step, client-side history, cleared on save (matches the design system's existing undo conventions where present, no new mechanism invented) |
| `Ctrl+F` | search steps by action type / config text / template-token substring, jumps focus + auto-expands ancestors |

### 4.6 Consequence-visibility law — wired into the editor, not a later pass

Per the binding law: every control states what it does and what changes; destructive/wide saves show a
real-data blast radius; dependents are named before save; disabled controls give a reason; dry-run wherever
possible.

- **Per-step plain-language line.** Every step row, in its collapsed one-line form, renders a **generated
  description** from its `Type`+`ConfigJson` (e.g. `send_message: "Hey {{user.name}}!"`, `wait_for_event: waits up
  to 5m for a redemption on Lucky Feather`) — never a bare enum name. Block headers do the same
  (`if: user is a moderator`, from the condition-tree's plain-language summary, §4.3).
- **Save-time blast radius (real data, not a guess).** Before committing a save that **changes a `PipelineTrigger`**
  (renaming/removing a trigger phrase) or **deletes/renames a Run-scope variable name referenced elsewhere** or
  **changes a `ListKey`/`NamedCounter` key an authored step still reads**, the save flow calls the existing
  `ValidateAsync` dry-run (control-flow §3/§6) **plus a new usage-count query**: `GET
  …/pipelines/{id}/usages` (NET-NEW, `pipelines:read` floor) returning `{ CommandsReferencing:
  List<CommandSummary>, TimersReferencing: List<TimerSummary>, EventResponsesReferencing:
  List<EventResponseSummary>, ScheduledPendingCount: int, SuspendedRunCount: int }` — counted from real rows
  (`Command.PipelineId`, `Timer.PipelineId`, `EventResponse.PipelineId`, `ScheduledPipelineTask` §10,
  `PipelineRunState` §1.4), never a static estimate. The save confirm dialog names each dependent **by name**
  (`"3 commands use this pipeline: !fight, !hit, !attack"`) before the author confirms a change that could break
  them, and separately flags **live suspended runs** (`"2 runs are currently waiting on this pipeline — they will
  resume against the NEW version"`, since resume always loads the current step tree, never a frozen snapshot —
  named here explicitly as the behavior, not left implicit).
- **Disabled-control reasons.** A block-palette item disabled by a tier cap (`MaxStepCount` reached, or a
  `record_*` action disabled because the deployment has no `ViewerRecordList` migration applied — never happens
  post-ship, listed for completeness) shows a tooltip naming the exact reason and, where applicable, the upgrade
  path (mirrors `tier_limit_reached`'s upsell payload, §3.13a of `commands-pipelines.md`).
- **Dry-run.** The existing `POST …/pipelines/validate` (§5 of `commands-pipelines.md`, unchanged) is called
  **on every structural edit** (debounced), not just before save — the editor surfaces validation errors
  (unreachable `switch_case`, missing `switch` default, `random_case` with no weight, condition-tree depth
  exceeded, cycle detected) inline on the offending row **as the author edits**, not only at save time. A
  **"run once, dry"** button (new, per-pipeline) executes the tree against a synthetic `ActionContext` seeded with
  the author's own identity and placeholder trigger values, **suppressing every side-effecting action** (chat
  send, ban, reward mutation, HTTP egress, sound playback — each such action's `ICommandAction` implementation
  checks a `DryRun bool` flag on `ActionContext`, new field, and returns a **simulated** `ActionResult.Ok` describing
  what *would* have happened instead of doing it) and shows the resulting step-by-step trace (which branches were
  taken, what each step *would* have sent) in a side panel — the closest a tree-shaped automation gets to a true
  preview without live side effects.

---

## 5. Failure + safety

### 5.1 Step failure mid-tree

Unchanged fail-closed default (control-flow, `commands-pipelines.md` §0 migration note: unknown action/condition ⇒
abort; action exception ⇒ stop, `Status=failed`) **except** inside a `try` block (§1.1/§2.1): a failure inside
`try`'s `body` children is caught, walks `catch` children instead, and the run **continues** past the `try` block
rather than aborting — this is the only opt-in exception to fail-closed, and it is explicit (an author must add a
`try` wrapper; the default for every other step remains hard-abort).

### 5.2 Partial execution

`PipelineExecution.StepsExecuted`/`StepsSkipped` (H.4, existing) already record exactly how far a run got.
`PipelineRunState` (§1.4) additionally lets an operator **see** a currently-suspended run's position
(`SuspendedAtStepId`) via the dashboard — no new column, same fields exposed read-only.

### 5.3 Retries

**No automatic retry** — an old-bot behaviour never asked for one, and blind retry of a side-effecting action (ban,
chat send, reward mutation) risks duplicate effects. The generic primitive for "try, and do something else on
failure" is `try`/`catch` (§5.1); an author who wants a bounded retry composes it explicitly: `loop repeat N` body =
`try { action } catch { if attempt < N: continue }` — expressible today with existing primitives, so no dedicated
`retry` block is added (keeps the primitive count minimal per the generic-primitives rule; a `retry N` sugar could
be added later as pure editor sugar over this exact composition without a schema change, but is **not** part of this
spec's decided scope — deliberately decided **not** to add it, not deferred: the compositional path already covers
it losslessly).

### 5.4 Guards against a runaway bad pipeline harming a live stream

All from control-flow D6, reused, plus this spec's additions:

| Guard | Scope | Value (baseline) |
|---|---|---|
| `MaxTotalActions` | whole run | tier-scaled (control-flow D6) |
| `MaxRecursionDepth` | `run_pipeline inline` call chain | **8** (§2.6) |
| `MaxIterations` | per `loop` | tier-scaled (control-flow D6) |
| `MaxRuntime` | whole run, **active time only** (§2.7) | tier-scaled (control-flow D6) |
| `MaxLoopRuntimeSeconds` | per `loop`, optional tighter cap | author-set, default = inherit `MaxRuntime` (§2.4) |
| `ResumeDeadline` clamp | `wait_for_event` | **[1s, 24h]**, same clamp as §10 scheduling |
| Concurrency admission | per-channel active-run cap | existing (`IPipelineEngine.GetActiveCountForChannel`) — a suspended run does **not** hold a slot (§2.3.1), so a pipeline that legitimately waits a long time cannot starve the channel's concurrency budget the way an in-memory `wait` would |
| `detached_step` isolation | one action | a detached action's own exception never aborts the parent run (§2.1) — bounds the blast radius of Raid's fire-and-forget OBS switch to itself |
| `HttpEgressAllowlist`/broker pattern | provider actions | unchanged (`commands-pipelines.md` §6.1) — no new egress surface introduced by this spec |

---

## 6. Migration + compatibility

### 6.1 Existing flat pipelines → tree (lossless upcast)

Every existing row keeps its columns; **nothing is deleted or retyped**. The upcast is a **read-time compatibility
view** used only until the editor's first save rewrites the pipeline natively:

1. **`Pipeline.TriggerKind` → `PipelineTrigger`.** On first read by the new editor, a single synthetic
   `PipelineTrigger` row is materialized in-memory (not yet persisted) from `Pipeline.TriggerKind` +, for
   `TriggerKind=command`, the wrapping `Command`'s own `Name`/`Aliases`/`PrefixMode`/`MatchMode` (§1.3's "still owns
   its own trigger phrase" rule) — the editor shows this as one trigger chip. Saving the pipeline **persists** the
   real `PipelineTrigger` row(s) and leaves `Pipeline.TriggerKind` as the now-summary column (§1.3). Until the first
   save, `Pipeline.TriggerKind` remains authoritative for the dispatcher — **no behavior change** pre-save.
2. **`PipelineStepCondition` flat-AND list → condition tree.** For a step whose conditions today are N flat rows
   with `ParentConditionId=null` (the only shape that exists pre-migration, since the column is net-new), the
   read-time view synthesizes an **implicit root `and` group** wrapping all N as its children, `Order`-preserved —
   this is **semantically identical** to today's "every condition must pass" evaluation, so no behavior changes for
   any existing pipeline. A pipeline with **zero** conditions on a step continues to mean "always true," unchanged.
   Saving through the new editor persists the (possibly still-flat) tree explicitly; a pipeline never touched by the
   new editor keeps working forever on the implicit-root reading — **there is no forced migration pass**, no
   backfill job, no risk window.
3. **No `PipelineRunState` for legacy pipelines** until one of their runs first hits a `wait_for_event` step — which
   cannot happen for a pipeline authored before this spec (the action didn't exist to place). Legacy pipelines are
   therefore **never** affected by the new suspend/resume machinery unless an author explicitly adds a
   `wait_for_event` step through the new editor.

**Net effect:** a legacy pipeline, read and re-run without ever being opened in the new editor, executes **byte-for-
byte identically** to today. The tree model is additive-only until an author opts a specific pipeline into the new
constructs by editing it.

### 6.2 The owner's imported legacy pipelines specifically

The old-bot behaviours (§3.7) are **not** imported as pipeline rows today (they're hand-written C#, per the analysis
doc's framing — "hand-written C# standing in for what the new tree must do declaratively"). There is therefore no
literal database row to migrate for those 26 behaviours; §3.7's map is the **authoring target** for re-implementing
them declaratively in the new system, not a migration of existing `Pipeline` rows. Any `Pipeline` rows that **do**
already exist in the current NomNomzBot database (authored during earlier development against the flat model) follow
exactly the §6.1 upcast — the "owner's imported legacy pipelines must survive" requirement is satisfied by §6.1's
losslessness, and separately, the 26 old-bot behaviours get **freshly authored** trees using the primitive catalogue
in §3, not migrated bytes (there's nothing byte-shaped to migrate — they were C#).

### 6.3 Older-client saves

The tree columns (`BlockKind`/`BlockConfigJson` on H.2 per control-flow D1, `ParentConditionId`/`GroupOp` on H.3 per
E2, the `PipelineTrigger` table per E1) are **all nullable-or-additive** — an older client that only knows the flat
`Branch`/single-condition shape can still **read** a tree-authored pipeline (it will render nonsensically in an old
UI — nested blocks show as a flat list — but the API does not reject the read) and can still **write** a
flat-shaped pipeline (no tree columns touched, defaults apply). The compatibility boundary the owner decision (E9)
draws is at the **write** path for constructs an old client cannot express: `ICommandConfigValidator` (existing,
`commands-pipelines.md` §3.6) rejects a save that would **orphan** a tree structure an old client's payload doesn't
account for — concretely, if the persisted pipeline already has `BlockKind`-typed steps or a multi-node condition
tree, and an incoming `PUT` payload's `PipelineGraphDto` (§4.3 of `commands-pipelines.md`) omits those steps
entirely (an old client serializing only what it understands), the save is rejected with `stale_client_tree_payload`
rather than silently deleting the parts the old client didn't send — **never silently downgraded**, per E9. A
same-or-newer client always sends the full tree it read, so this only fires for a genuinely stale client attempting
a lossy write.

---

## 7. Decisions (resolved)

`PipelineTrigger` child table for N triggers (E1); `PipelineStepCondition` self-FK + `GroupOp` condition tree (E2);
`PipelineRunState` + `wait_for_event` action + `PipelineRunResumeListener` + `PipelineRunTimeoutService` for
wait-for-event/resume-later (E3); named-parameter `run_pipeline` args + `return_value` action + `{{call.result}}`
(E4); `MaxRecursionDepth=8` baseline + call-graph cycle validation across suspend boundaries (E5); per-loop
`{{loop.previous_item}}` + `MaxLoopRuntimeSeconds` (E6); full 22-item gap list closed by `try`/`detached_step` block-
kinds, `wait_for_event`/`return_value`/`update_reward`/`record_*`/`list_find` actions, and `{{json.*}}`/`{{expr:}}`/
`{{random.from:}}`/`{{random.weighted:}}`/`wait_until`/`{{str.*}}`/`{{commands.*}}` template functions, with the two
remaining gaps (#18, #11-debounce) confirmed satisfied by existing composition, not new primitives (E7); nested
block-list editor with drag reorder/nest, condition-tree group/ungroup sub-editor, keyboard parity, collapse-at-depth,
and the consequence-visibility law wired in as save-time real-data blast-radius + dry-run + per-step plain-language
descriptions (E8); lossless read-time upcast of every existing flat pipeline via an implicit-root condition group and
a synthesized single `PipelineTrigger`, no forced backfill, stale-client writes rejected not silently downgraded (E9).

---

## Owner/CTO decisions — SETTLED 2026-08-25

The three items raised during authoring are decided; none remain open.

| # | Question | Decision | Reason |
|---|---|---|---|
| 1 | `MaxRecursionDepth` baseline | **8** | Matches the existing "8" ceilings elsewhere in the spec family; consistency beats a novel number. |
| 2 | Dedicated `retry N` primitive? | **No** | It composes from `loop` + `try`. A bespoke retry block would violate the generic-primitives-not-bespoke-features rule. |
| 3 | Does suspended wall-clock count against `MaxRuntime`? | **No** | The only coherent reading: otherwise a pipeline waiting 20 minutes for a redeem dies on its runtime budget and wait-for-event is useless. The runtime budget measures WORK, not waiting. |
