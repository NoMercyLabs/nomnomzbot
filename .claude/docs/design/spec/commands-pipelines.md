# Interface Specification — `commands-pipelines` subsystem

**Status:** implementable. Code from this directly. Source of truth: locked DB schema
`docs/design/2026-06-16-database-schema.md` (G.2, G.2a, G.3, H.1–H.7, I.1, I.2, M.5), execution-model decision
`docs/design/2026-06-16-custom-command-execution.md`, stack `docs/design/2026-06-16-stack-and-dependencies.md`,
defaults `docs/design/2026-06-16-decisions-pending-confirmation.md`.

**Namespace:** `NomNomzBot.*`. **.NET 10 / C# 14 / EF Core 10.** File-scoped namespaces, `Nullable`
enabled, async all the way, `Result<T>` (`NomNomzBot.Application.Common.Models`) over exceptions/null. App JSON =
**Newtonsoft.Json** (per schema §1.4 + conventions). Surrogate PKs = `Guid` via `Guid.CreateVersion7()`. Tenant key
`BroadcasterId` is **`Guid`** (FK→`Channels.Id`). Soft-delete via `IsDeleted`+`DeletedAt` global filter.

> **Scope of this subsystem (owns):** authored `Commands` (T1 template / T2 pipeline / T3 code-trigger), built-in
> command enable/disable+override (`ChannelBuiltinCommands`), the normalized pipeline model
> (`Pipelines`/`PipelineSteps`/`PipelineStepConditions`) + execution engine + `ICommandAction` blocks + condition
> evaluators + template variables (`VariableResolver`), per-command cooldowns (`CommandCooldownStates` +
> `ICooldownManager`), `Timers`, `EventResponses`, and the per-run telemetry (`PipelineExecutions`, `CommandUsage`).
>
> **Out of scope (consumed via existing interfaces, NOT redefined here):** the T3 sandbox itself
> (`IScriptExecutor` / Wasmtime+Jint, owned by the sandbox-execution subsystem — this subsystem only references
> `CodeScripts.CurrentVersionId` from a `run_code` step and calls `IScriptExecutor`), chat send / moderation
> (`IChatProvider`), music (`IMusicService`), tenant resolution (`ICurrentTenantService`), event bus
> (`IEventBus`/`IDomainEventDispatcher`), HTTP egress vault (`HttpEgressAllowlist` rows are owned here as config but
> the `http_request` action's SSRF-hardened client is the sandbox subsystem's), token vault, crypto.

---

## 0. Migration note — existing surface this spec REPLACES (do not duplicate)

The repo currently has **two** parallel pipeline stacks plus `int`-keyed entities. This spec consolidates onto the
canonical one and widens keys to the locked schema. Concrete deltas the implementer must apply:

| Existing (today) | Action |
|---|---|
| `NomNomzBot.Application.Pipeline.*` (ICommandAction/ActionContext/ActionResult/PipelineContext/CommandActionRegistry/VariableResolver/PipelineDefinition) **and** `NomNomzBot.Infrastructure.Pipeline.*` (ICommandAction/ICommandCondition/PipelineExecutionContext/ActionDefinition/PipelineEngine) | **Collapse to ONE** action/condition contract in `NomNomzBot.Application.Pipeline` (the names below). Delete the Infrastructure duplicates `ICommandAction`/`ICommandCondition`/`ActionResult`/`PipelineExecutionContext`. Infrastructure actions re-target the Application contract. |
| `Domain/Interfaces/IPipelineEngine.cs` — `PipelineRequest.BroadcasterId : string`, `PipelineJson : string` (inline JSON), `int` step indexes | Keep `IPipelineEngine` name; change `BroadcasterId`→`Guid`, replace `PipelineJson` with `PipelineId : Guid` (normalized model is truth; `GraphJsonCache` is cache only). Add fail-closed fields below. |
| `PipelineEngine` fail-OPEN semantics (unknown condition ⇒ true, unknown action ⇒ skip, action throw ⇒ continue) | **Reverse to fail-CLOSED** (must-fix #4): unknown action/condition ⇒ hard fail the run; action exception ⇒ stop with `Status=failed`. |
| `Command.Id : int`, `Command.BroadcasterId : string(50)`, `Command.Type`, `Command.Response/Responses/PipelineJson` | Re-key to `Guid`; `BroadcasterId : Guid`; rename `Type`→`Tier` (`template`/`pipeline`/`code`); `Response`→`TemplateResponse`, `Responses`→`TemplateResponses`, drop inline `PipelineJson` for `PipelineId : Guid?`. Add `NameNormalized`, `UserCooldownSeconds`, `UseCount`, `LastUsedAt`, `ConfigSchemaVersion`, `MinPermissionLevel:int`. |
| `Pipeline.Id : int`, `GraphJson`, no steps | Re-key `Guid`; add `TriggerKind`, `MaxStepCount`, `TriggerCount`, `LastTriggeredAt`, `GraphJsonCache`; introduce child `PipelineSteps`/`PipelineStepConditions`. |
| DTOs `CommandDto/PipelineDto/TimerDto/EventResponseDto` with `int Id` | Widen all `Id`/`PipelineId` to `Guid`; update as in §4. |
| `ICommandService/IPipelineService/IEventResponseService` (`string broadcasterId`, `int id`) | Keep names; change `broadcasterId`→`Guid`, `id`→`Guid`. Add **net-new** `ITimerService`, `IBuiltinCommandService`, `ICommandDispatcher`, `IPipelineCompiler`, `ICommandConfigValidator`. |
| `VariableResolver` (regex `{{\w+}}`, no namespaced vars, no `{{args.N}}`) | Replace with namespaced resolver (§6) supporting `{{user.name}}`, `{{args.1}}`, `{{random.number:1:100}}`, etc. Keep the `static partial` + `GeneratedRegex` shape. |
| `ICooldownManager` (in-memory only, `string commandName`) | Keep interface; back it with `CommandCooldownStates` (G.3) write-through for durability across restart/multi-instance. Signatures change to `Guid` (§3). |

---

## 1. Entities (locked schema — owned by this subsystem)

All defined in `docs/design/2026-06-16-database-schema.md`. **Do not redefine columns**; this lists ownership, the
EF entity class to create in `NomNomzBot.Domain/Entities/`, key fields, and the converter/enum flags. Every row carries
`BroadcasterId : Guid` (FK→`Channels.Id`, `ITenantScoped`) unless noted. `[VC:JSON]` = `ValueConverter`+`ValueComparer`
over Newtonsoft.Json; `[VC:enum]` = enum↔text converter. `ConfigSchemaVersion : int` (default 1) on every
app-interpreted-JSON config table is the per-row upcast anchor.

| # | Entity (`Domain/Entities/*.cs`) | Base | PK | Key fields / types |
|---|---|---|---|---|
| **G.2** | `Command` | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId:Guid`; `Name:string(100)`; `NameNormalized:string(100)` (unique `(BroadcasterId,NameNormalized)`); `PrefixMode:string(20)`[VC:enum] `Default`\|`Custom`\|`None`; `CustomPrefix:string(8)?` (when `Custom`); `MatchMode:string(20)`[VC:enum] `StartsWith`\|`Exact`\|`Contains`\|`Regex` (default `StartsWith`); `MatchPattern:string(200)?` (required when `MatchMode=Regex`, else null); `Tier:string(20)`[VC:enum] `template`\|`pipeline`\|`code`; `Description:string(500)?`; `Aliases:List<string>`[VC:JSON]; `TemplateResponse:string(2000)?`; `TemplateResponses:List<string>?`[VC:JSON]; `ConfigSchemaVersion:int`; `PipelineId:Guid?` (FK→Pipelines); `MinPermissionLevel:int`[VC:enum, community ladder]; `CooldownSeconds:int`; `UserCooldownSeconds:int` (0=off); `CooldownPerUser:bool`; `IsEnabled:bool`; `IsPlatform:bool`; `UseCount:long`; `LastUsedAt:DateTime?` |
| **G.2a** | `ChannelBuiltinCommand` | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId:Guid`; `BuiltinKey:string(100)` (unique `(BroadcasterId,BuiltinKey)`); `IsEnabled:bool`; `ConfigSchemaVersion:int`; `OverridesJson:BuiltinCommandOverrides?`[VC:JSON] (POCO shape in §4.5: `Enabled:bool` toggle + `CustomResponseTemplate`/`CooldownSeconds`/`MinRole`, each nullable=inherit) |
| **G.3** | `CommandCooldownState` | *(append-ish, no soft-delete)* | `Id : long` | `CommandId:Guid` (FK→Commands); `BroadcasterId:Guid`; `UserId:Guid?` (null=global per-command); `LastInvokedAt:DateTime`; `ExpiresAt:DateTime` (TTL sweep); unique `(CommandId,UserId)` |
| **G.4** | `NamedCounter` | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId:Guid`; `Key:string(50)` (unique `(BroadcasterId,Key)`); `Value:long` — persistent cross-command counter backing `{{count.<name>}}` + `set_counter`/`adjust_counter`. Owned by this subsystem. |
| **H.1** | `Pipeline` | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId:Guid`; `Name:string(200)`; `Description:string(500)?`; `TriggerKind:string(30)`[VC:enum] `command`\|`event`\|`timer`\|`manual`\|`webhook`; `IsEnabled:bool`; `MaxStepCount:int`; `TriggerCount:long`; `LastTriggeredAt:DateTime?`; `GraphJsonCache:string?`[VC:JSON, **cache only** — rows below are truth] |
| **H.2** | `PipelineStep` | `BaseEntity` | `Id : Guid` | `PipelineId:Guid`; `BroadcasterId:Guid`; `ParentStepId:Guid?` (branch nesting); `Branch:string(10)?`[VC:enum] `then`\|`else`; `Order:int` (unique `(PipelineId,Order)`); `ActionType:string(60)` (snake_case registry key; **unknown ⇒ save-fail**); `ConfigJson:Dictionary<string,object?>`[VC:JSON, **MUST NOT** carry tenant/credential/url — enforced by `ICommandConfigValidator`]; `ConfigSchemaVersion:int`; `CodeScriptId:Guid?` (FK→CodeScripts, **only** when `ActionType="run_code"`); `IsEnabled:bool` |
| **H.3** | `PipelineStepCondition` | `BaseEntity` | `Id : Guid` | `PipelineStepId:Guid`; `BroadcasterId:Guid`; `ConditionType:string(40)`[VC:enum] `user_role`\|`random`\|`var_compare`\|`cooldown` (**unknown ⇒ fail-closed**); `Operator:string(20)?`[VC:enum]; `LeftOperand:string(500)?`; `RightOperand:string(500)?`; `Negate:bool`; `Order:int` |
| **H.4** | `PipelineExecution` | **[APPEND-ONLY]** (`CreatedAt`/`StartedAt` only) | `Id : long` | `PipelineId:Guid`; `BroadcasterId:Guid`; `TriggeredByUserId:Guid?`; `TriggerKind:string(30)`; `Status:string(20)`[VC:enum] `success`\|`failed`\|`timeout`\|`denied`; `HostCallCount:int`; `DurationMs:int`; `ErrorMessage:string(1000)?`; `StepLogsJson:List<StepExecutionLog>?`[VC:JSON, bounded, PII-excluded, TTL-purged]; `StartedAt:DateTime`; `CompletedAt:DateTime?` |
| **H.5** | `CodeScript` *(referenced; T3 owns lifecycle)* | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId`; `Name:string(100)` (unique `(BroadcasterId,Name)`); `Language:string(20)` `typescript`; `CurrentVersionId:Guid?`; `IsEnabled:bool`; `AuthorUserId:Guid?`; `LastRuntimeError:string?`; `LastRanAt:DateTime?` — **this subsystem reads `CurrentVersionId` from a `run_code` step; it does not author scripts.** |
| **H.6** | `CodeScriptVersion` *(referenced)* | **[APPEND-ONLY]** | `Id : Guid` | `CodeScriptId`; `Version:int`; `SourceCode`; `CompiledJs:string?`; `CompiledHash:string(64)`; `ValidationStatus:string(20)` `valid`\|`rejected`\|`pending`; `DeclaredCapabilitiesJson`[VC:JSON]; `PublishedAt:DateTime?` — read-only here. |
| **H.7** | `HttpEgressAllowlist` *(config owned here, used by `http_request`)* | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId`; `Fqdn:string(253)` (unique `(BroadcasterId,Fqdn)`); `ApprovedByUserId:Guid?`; `IsEnabled:bool`; `MaxResponseBytes:int`; `MaxRequestBytes:int` (default 8192; outbound request-body cap — **reject, not truncate**, when exceeded); `AllowRequestBody:bool` (default false; GET/HEAD never carry a body); `AllowedMethods:string(100)` (CSV of permitted HTTP methods, default `GET`; request method must be in it); `PathPrefix:string(255)?` (optional path-prefix restriction; null = any path on the FQDN) |
| **I.1** | `Timer` | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId`; `Name:string(100)`; `Messages:List<string>`[VC:JSON]; `ConfigSchemaVersion:int`; `PipelineId:Guid?`; `IntervalMinutes:int`; `MinChatActivity:int`; `IsEnabled:bool`; `LastFiredAt:DateTime?`; `NextMessageIndex:int` |
| **I.2** | `EventResponse` | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId`; `EventType:string(100)` (index `(BroadcasterId,EventType)`); `ResponseType:string(50)`[VC:enum] `chat_message`\|`overlay`\|`pipeline`\|`none`; `Message:string(2000)?`; `PipelineId:Guid?`; `MetadataJson:Dictionary<string,string>`[VC:JSON]; `ConfigSchemaVersion:int`; `IsEnabled:bool` |
| **M.5** | `CommandUsage` | **[APPEND-ONLY]** | `Id : long` | `BroadcasterId`; `CommandId:Guid?`; `CommandNameSnapshot:string(100)`; `ViewerProfileId:Guid`; `ViewerUserId:Guid` [PII via id]; `ArgsSnapshot:string(500)?` **[PII-scrub]**; `WasSuccessful:bool`; `CreatedAt` (index `(BroadcasterId,ViewerUserId)`) |

> **Note (key widening, repo-wide, must do now):** existing `Command`/`Pipeline`/`Timer`/`EventResponse` entities use
> `int Id` + `string(50) BroadcasterId`. Per locked schema §1.1 all become `Guid` and `ITenantScoped.BroadcasterId`
> widens `string`→`Guid`. This is the deliberate one-time rebuild; do not migrate incrementally.

---

## 2. Domain events

In `NomNomzBot.Domain/Events/`, inheriting the canonical **`DomainEventBase`** (platform-conventions §2.0 — supplies
`Guid EventId`, `DateTimeOffset OccurredAt`, `Guid BroadcasterId`; events add only payload fields, never redeclaring the base members). **Existing events to KEEP** (already correct shape — reuse, do
not recreate): `BeforeCommandExecutedEvent`, `AfterCommandExecutedEvent`, `CommandExecutedEvent`, `CommandFailedEvent`.

**Net-new events to add** (one responsibility each; all `sealed`, init-only):

| Event | Payload (fields : types) | Emitted when |
|---|---|---|
| `PipelineExecutionStartedEvent : DomainEventBase` | `ExecutionId:string`, `PipelineId:Guid`, `TriggerKind:string`, `TriggeredByUserId:Guid?` | engine accepts a run past admission/concurrency gates |
| `PipelineExecutionCompletedEvent : DomainEventBase` | `ExecutionId:string`, `PipelineId:Guid`, `Outcome:PipelineOutcome`, `Duration:TimeSpan`, `StepsExecuted:int`, `StepsSkipped:int`, `HostCallCount:int` | run reaches a terminal outcome (success/failed/timeout/denied/stopped) |
| `PipelineStepFailedEvent : DomainEventBase` | `ExecutionId:string`, `PipelineId:Guid`, `StepId:Guid`, `Order:int`, `ActionType:string`, `Reason:string` | a step throws or returns failure under fail-closed semantics |
| `PipelineExecutionDeniedEvent : DomainEventBase` | `PipelineId:Guid`, `TriggeredByUserId:Guid?`, `DenyReason:string` (`concurrency`\|`rate_limit`\|`host_call_budget`\|`step_cap`\|`disabled`\|`unknown_action`\|`unknown_condition`) | run rejected pre/at execution (fail-closed audit) |
| `CommandCooldownBlockedEvent : DomainEventBase` | `CommandId:Guid`, `CommandName:string`, `UserId:Guid`, `RemainingSeconds:int`, `Scope:string` (`global`\|`per_user`) | invocation suppressed by an active cooldown |
| `TimerFiredEvent : DomainEventBase` | `TimerId:Guid`, `Name:string`, `MessageIndex:int`, `FiredPipeline:bool` | a timer tick passes its activity gate and dispatches |
| `BuiltinCommandToggledEvent : DomainEventBase` | `BuiltinKey:string`, `IsEnabled:bool`, `ChangedByUserId:Guid?` | a built-in is enabled/disabled or overridden |
| `EventResponseTriggeredEvent : DomainEventBase` | `EventResponseId:Guid`, `EventType:string`, `ResponseType:string`, `DispatchedPipelineId:Guid?` | an inbound platform event matches an enabled `EventResponse` |

---

## 3. Service interfaces

All in `NomNomzBot.Application` (interfaces in `Services/` or `Common/Interfaces/`; impls in
`NomNomzBot.Infrastructure/Services/...` unless noted). `broadcasterId` is `Guid` everywhere. Repositories via
`IUnitOfWork` + `IApplicationDbContext`; never raw `DbContext` in controllers.

### 3.1 `ICommandService` (EXTEND existing — `Services/ICommandService.cs`)

Management CRUD + runtime resolution for authored commands. **Widen `broadcasterId`→`Guid`.** Identify by name (route)
or id.

```csharp
Task<Result<PagedList<CommandListItem>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
Task<Result<CommandDto>>                 GetAsync(Guid broadcasterId, string commandName, CancellationToken ct = default);
Task<Result<CommandDto>>                 CreateAsync(Guid broadcasterId, CreateCommandDto request, CancellationToken ct = default);
Task<Result<CommandDto>>                 UpdateAsync(Guid broadcasterId, string commandName, UpdateCommandDto request, CancellationToken ct = default);
Task<Result>                             DeleteAsync(Guid broadcasterId, string commandName, CancellationToken ct = default);
Task<Result<CommandResolution>>          ResolveAsync(Guid broadcasterId, string trigger, CancellationToken ct = default);
```
- `CreateAsync` — validates name uniqueness (`NameNormalized`) + tier-specific config via `ICommandConfigValidator`; for pipeline/code tiers requires an existing `PipelineId`; **enforces the tier authoring-count caps** (§3.13a) — `custom_commands` against the live command count, and `response_variations_per_trigger` against the new `TemplateResponses` length — before persisting `Command` (UUIDv7) + `SaveChangesAsync`. Returns persisted `CommandDto`. **Reject at save** if name collides with a built-in key or an alias already in use.
- `UpdateAsync` — partial update; re-validates config + uniqueness; **re-checks `response_variations_per_trigger`** when the update grows `TemplateResponses` (add-time only — never truncates); bumps `UpdatedAt`. Tier change re-runs validation.
- `DeleteAsync` — soft-delete (`DeletedAt`), clears cooldown states for the command. No hard delete.
- `ResolveAsync` — resolves a chat trigger (name or alias) to a `CommandResolution` (tier, `PipelineId?`, `TemplateResponse(s)`, cooldown config, min level) for the dispatcher. Read-through cache by `(BroadcasterId, version)`; no side effects.

### 3.2 `ICommandDispatcher` (NET-NEW — `Common/Interfaces/ICommandDispatcher.cs`)

The single runtime entry: a chat message arrives → decide if it's a command → gate → execute (template/pipeline/code)
→ record usage. Owns the order-of-checks (enabled → permission → cooldown → execute).

```csharp
Task<Result<CommandDispatchResult>> DispatchAsync(CommandDispatchRequest request, CancellationToken ct = default);
```
- Resolves the trigger (authored via `ICommandService.ResolveAsync`, else a built-in via `IBuiltinCommandService`). Unknown trigger ⇒ `Result.Success` with `Matched=false` (not an error). Disabled ⇒ no-op result. Permission below `MinPermissionLevel` ⇒ denied result, no chat side effect unless configured. Cooldown active ⇒ emits `CommandCooldownBlockedEvent`, returns blocked result.
- On pass: T1 renders `TemplateResponse(s)` via `ITemplateEngine` and sends through `IChatProvider`; T2/T3 builds a `PipelineRequest` and calls `IPipelineEngine.ExecuteAsync`. Sets cooldown via `ICooldownManager`, increments `Command.UseCount`/`LastUsedAt`, appends `CommandUsage` (append-only), emits `Before/AfterCommandExecutedEvent` + `CommandExecutedEvent`/`CommandFailedEvent`.

#### 3.2.1 Prefix + match resolution (how an inbound message becomes a command hit)

Both authored `Commands` and built-ins carry the per-command trigger model from the locked schema (`Commands.PrefixMode`,
`CustomPrefix`, `MatchMode`; built-ins default `PrefixMode=Default`, `MatchMode=StartsWith`). The dispatcher resolves a hit
per message as follows — the channel's `Channels.DefaultCommandPrefix` (default `!`) is loaded once per dispatch:

1. **Effective prefix** for a candidate command:
   - `PrefixMode=Default` → the channel `DefaultCommandPrefix`.
   - `PrefixMode=Custom` → `CustomPrefix` (the per-command override, e.g. `?`, `+`).
   - `PrefixMode=None` → no prefix (empty string).
2. **Match** the raw message against `<effective-prefix><Name>` per `MatchMode`:
   - `StartsWith` (default) — message **starts with** `<effective-prefix><trigger>` followed by end-of-string or whitespace (the rest is the args string); the classic `!command args` form.
   - `Exact` — the **whole message** equals `<effective-prefix><trigger>` (no args; e.g. a no-prefix `Exact` keyword command).
   - `Contains` — `<effective-prefix><trigger>` appears **anywhere** in the message as a whitespace-delimited keyword (keyword-trigger / auto-response style); first such match wins.
   - `Regex` — the **message** matches the command's `MatchPattern` via `IRegexMatcher.IsMatch` (§6.4) — `RegexOptions.NonBacktracking`, time-bounded, no prefix is prepended (the author's pattern is authoritative). First such match wins.
3. **Ordering / precedence** — **reserved data-subject-rights commands** (`!forgetme` / `!gdpr …`, `gdpr-crypto.md` §9) resolve **first of all**, ahead of authored `Commands` and built-ins, and are **un-shadowable / un-disable-able** (the GDPR rights floor is always-on, per opt-in/default-deny — a streamer cannot register a command that masks them). Then authored `Commands` resolve before built-ins (`ICommandService.ResolveAsync` then `IBuiltinCommandService`); `StartsWith`/`Exact` prefix-anchored matches take precedence over `Contains` keyword matches; among equal modes, the longest `<effective-prefix><trigger>` wins (so `!foo` beats `!f`). Name/alias matching stays case-insensitive (`NameNormalized`).
4. No match under any candidate ⇒ `Matched=false` (not an error).

`{{command.prefix}}` renders the firing command's effective prefix (step 1); `{{bot.prefix}}` always renders the channel
`DefaultCommandPrefix`. **`Regex` is a first-class `MatchMode`**, made ReDoS-safe by .NET's own `NonBacktracking` engine —
**no Wasmtime/Jint sandbox is involved** (see §6.4); `MatchMode`'s shipped enum is `StartsWith`\|`Exact`\|`Contains`\|`Regex`.

### 3.3 `IPipelineEngine` (EXTEND existing — `Domain/Interfaces/IPipelineEngine.cs`)

Executes a normalized pipeline. **Replace inline `PipelineJson` with `PipelineId`; widen ids to `Guid`; add
fail-closed + DoS-control fields.**

```csharp
Task<PipelineExecutionResult> ExecuteAsync(PipelineRequest request, CancellationToken ct = default);
Task                          CancelAllForChannelAsync(Guid broadcasterId, CancellationToken ct = default);
int                           GetActiveCountForChannel(Guid broadcasterId);
```
- `ExecuteAsync` — loads the compiled pipeline (`IPipelineCompiler`), seeds variables, runs steps **fail-closed**: unknown `ActionType` or unknown `ConditionType` ⇒ abort run with `Status=denied`/`failed` (never skip/treat-true); any action exception ⇒ stop with `Status=failed`. Enforces global + per-channel concurrency admission, per-execution **host-call budget** and **wall-clock-including-host watchdog** (seconds timeout, cumulative `Wait` cap, step-count cap). Persists one `PipelineExecution` (append-only) with bounded `StepLogsJson`; emits started/completed/denied/step-failed events. Branch steps (`ParentStepId`/`Branch`) recurse `then`/`else` (linear-with-branch, not DAG). **Steps form a tree** — block steps (`PipelineStep.BlockKind` = `switch`/`loop`/`random_branch`/…) own ordered child steps via `ParentStepId`, walked depth-first under iteration/recursion/total-action/runtime caps; control-flow tree execution is owned by `pipeline-control-flow.md`.
- `CancelAllForChannelAsync` — cancels every active run for the tenant (stream-offline path); best-effort.

`PipelineRequest` (Domain) — **changed shape:** `BroadcasterId:Guid`; `PipelineId:Guid`; `TriggerKind:string`;
`TriggeredByUserId:Guid`; `TriggeredByDisplayName:string`; `MessageId:string?`; `RedemptionId:string?`;
`RewardId:string?`; `RawMessage:string=""`; `Args:IReadOnlyList<string>` (pre-split); `InitialVariables:IDictionary<string,string>`;
`TaintedVariables:IReadOnlyDictionary<string,string>?` (attacker-authored bag, seeded into `ActionContext.TaintedVariables` — webhooks.md §7.1);
`EventType:string?` and `JournalEventId:Guid?` (the triggering event's type/journal id — seeded onto `ActionContext` for `send_webhook`, §4.4).

> **Non-user (system-actor) triggers — binding contract.** Not every trigger has a Twitch user: a `TriggerKind=webhook`
> run (owner `webhooks.md` §3.2.2) and a `TriggerKind=manual`/system run (both shipped enum values, H.1) have **no**
> `Users` row, and the same contract binds every system-actor trigger kind. The reserved
> sentinel **`WebhookSystemActor.UserId = Guid.Empty`** is passed as `TriggeredByUserId` for these; `TriggeredByDisplayName`
> carries the wire-source label (e.g. the provider name). The engine treats `TriggeredByUserId == Guid.Empty` as a
> **system trigger**: it is **never** dereferenced as a `Users` FK and **skips per-user permission/cooldown gates**
> (there is no user to gate), while **global + per-channel concurrency admission still applies**. Define the constant
> once (`NomNomzBot.Domain.Constants.WebhookSystemActor`); both this engine and the webhook dispatcher use it.
`PipelineExecutionResult` (Domain) — `ExecutionId:string`; `Outcome:PipelineOutcome`; `Duration:TimeSpan`;
`StepsExecuted:int`; `StepsSkipped:int`; `Total:int`; `HostCallCount:int`; `ErrorMessage:string?`;
`StepLogs:IReadOnlyList<StepExecutionLog>`. `PipelineOutcome` enum gains `Denied` (alongside existing
`Completed`/`Stopped`/`Failed`/`TimedOut`/`Cancelled`).

### 3.4 `IPipelineService` (EXTEND existing — `Services/IPipelineService.cs`)

Management CRUD for the normalized pipeline model. **Widen `int id`→`Guid id`, `broadcasterId`→`Guid`.**

```csharp
Task<Result<PagedList<PipelineListItemDto>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
Task<Result<PipelineDto>>                    GetAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);
Task<Result<PipelineDto>>                    CreateAsync(Guid broadcasterId, CreatePipelineDto request, CancellationToken ct = default);
Task<Result<PipelineDto>>                    UpdateAsync(Guid broadcasterId, Guid id, UpdatePipelineDto request, CancellationToken ct = default);
Task<Result>                                 DeleteAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);
Task<Result<PipelineValidationResult>>       ValidateAsync(Guid broadcasterId, PipelineGraphDto graph, CancellationToken ct = default);
```
- `Create/UpdateAsync` — accepts the editor `PipelineGraphDto` (steps + conditions), runs **validate-on-save** through `ICommandConfigValidator` (unknown action ⇒ reject; step-count > `MaxStepCount` ⇒ reject; tenant/credential/url params ⇒ reject), normalizes into `Pipeline`+`PipelineStep`+`PipelineStepCondition` rows in one transaction, refreshes `GraphJsonCache`. Returns the persisted graph.
- `DeleteAsync` — soft-delete pipeline + cascade soft-delete child steps/conditions; rejected if a `Command`/`Timer`/`EventResponse` still references it (`Result.Failure` with `pipeline_in_use`).
- `ValidateAsync` — dry-run validation (no persist) for the editor's live feedback.

### 3.5 `IPipelineCompiler` (NET-NEW — `Application/Pipeline/IPipelineCompiler.cs`)

Turns persisted rows into an in-memory executable plan, cached per `(BroadcasterId, PipelineId, version)` for
no-restart hot reload.

```csharp
Task<CompiledPipeline> GetAsync(Guid broadcasterId, Guid pipelineId, CancellationToken ct = default);
void                   Invalidate(Guid broadcasterId, Guid pipelineId);
```
- `GetAsync` — loads steps+conditions ordered by `(Order)`, resolves each `ActionType`→`ICommandAction` and `ConditionType`→`IConditionEvaluator` from the registries (**unknown ⇒ throws compile error**, surfaced as fail-closed at execution), builds the branch tree. Cached; `Invalidate` called by `IPipelineService` on save.

### 3.6 `ICommandConfigValidator` (NET-NEW — `Application/Pipeline/ICommandConfigValidator.cs`)

Save-time, fail-closed validator. The capability-broker invariant lives here (must-fix #4/#5; broker-pattern §4).

```csharp
Result ValidateCommand(CommandTier tier, CreateCommandDto request);
Result ValidatePipeline(PipelineGraphDto graph, int maxStepCount);
Result ValidateStepConfig(string actionType, IReadOnlyDictionary<string, object?> configJson);
```
- All return `Result.Failure(code)` for: unknown `actionType`/`conditionType`, step count over cap, and **any config key naming a tenant/credential/url** (`broadcaster_id`, `channel_id`, `access_token`, `client_secret`, raw `url`/`uri` on non-`http_request` actions, peer-channel ids). Enforced as an architecture-test invariant too.
- **Tainted-payload guard (webhook triggers — `webhooks.md` §7.1).** When validating a pipeline whose `TriggerKind=webhook`, `ValidatePipeline` **fails closed** (`tainted_payload_in_sensitive_param`) if any **security-sensitive action parameter** binds a `{{payload.*}}` token: `ban`/`timeout` `UserRef`, `shoutout` `TargetChannel`, `http_request` `Fqdn`/`Path`/`Method`, `send_webhook` `OutboundWebhookEndpointId`. `payload.*` is attacker-authored; it may feed display sinks but never the *target* of a moderation/egress action. The validator additionally surfaces a **warning** whenever a `webhook`-triggered pipeline references `payload.*` anywhere (author is told the namespace is untrusted). Enforced as an architecture-test invariant.
- `ValidateCommand` additionally enforces the trigger model: `PrefixMode=Custom` requires a non-empty `CustomPrefix` (≤8 chars); `PrefixMode∈{Default,None}` requires `CustomPrefix` null/empty (`invalid_custom_prefix`). `MatchMode` must be one of `StartsWith`/`Exact`/`Contains`/`Regex`. When `MatchMode=Regex`, `MatchPattern` is **required** (non-empty, ≤200 chars) and must pass `IRegexMatcher.ValidateAndCompile` (§6.4) — an unsupported construct or over-length pattern ⇒ `Result.Failure("unsupported_regex_construct")` / `Result.Failure("invalid_match_pattern")`; for any non-`Regex` mode `MatchPattern` must be null/empty (`invalid_match_pattern`).

### 3.7 `ITimerService` (NET-NEW — `Services/ITimerService.cs`)

CRUD + scheduling state for rotating timers (I.1). Backed by a `BackgroundService` tick guarded by `IRunOnceGuard`
(no-op lite / advisory-lock SaaS) so multi-instance does not double-fire.

```csharp
Task<Result<PagedList<TimerListItem>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
Task<Result<TimerDto>>                 GetAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);
Task<Result<TimerDto>>                 CreateAsync(Guid broadcasterId, CreateTimerDto request, CancellationToken ct = default);
Task<Result<TimerDto>>                 UpdateAsync(Guid broadcasterId, Guid id, UpdateTimerDto request, CancellationToken ct = default);
Task<Result>                           DeleteAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);
Task<Result>                           FireDueAsync(Guid broadcasterId, long sessionMessageCount, CancellationToken ct = default);
```
- `CreateAsync` — **enforces the `timers` tier authoring-count cap** (§3.13a) against the live timer count before persisting a new `Timer`. `response_variations_per_trigger` is **not** applied to `Timer.Messages` (a timer's message list is a rotation schedule, not response variations on a single trigger fire).
- `FireDueAsync` — called by the scheduler per channel: for each enabled due timer (`IntervalMinutes` elapsed **and** `sessionMessageCount ≥ MinChatActivity`), sends `Messages[NextMessageIndex]` via `IChatProvider` (or dispatches `PipelineId`), advances `NextMessageIndex` (rotating), sets `LastFiredAt`, emits `TimerFiredEvent`. Mutating-state writes are persisted.

### 3.8 `IEventResponseService` (EXTEND existing — `Services/IEventResponseService.cs`)

CRUD for per-event reactions (I.2). **Widen `broadcasterId`→`Guid`; add a runtime trigger.**

```csharp
Task<Result<PagedList<EventResponseListItem>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
Task<Result<EventResponseDto>>                 GetByEventTypeAsync(Guid broadcasterId, string eventType, CancellationToken ct = default);
Task<Result<EventResponseDto>>                 UpsertAsync(Guid broadcasterId, string eventType, UpdateEventResponseDto request, CancellationToken ct = default);
Task<Result>                                   DeleteAsync(Guid broadcasterId, string eventType, CancellationToken ct = default);
Task<Result>                                   TriggerAsync(Guid broadcasterId, string eventType, IReadOnlyDictionary<string, string> eventVariables, CancellationToken ct = default);
```
- `UpsertAsync` — when `ResponseType="pipeline"` requires a valid `PipelineId` (else `Result.Failure`); validates `MetadataJson` shape. **Enforces the tier authoring-count caps** (§3.13a): `event_responses` against the live trigger count **when creating a new** `(BroadcasterId, EventType)` row, and `response_variations_per_trigger` against the response's variation list (the `random_response` action's `Messages` on the bound pipeline) — add-time only, never truncated. **Channel-point reward-redemption responses route through here** (authored as an `EventResponse` keyed on `channel.channel_points_custom_reward_redemption.add`), so the reward variation cap is enforced by this same path — the `Rewards` table is a Twitch mirror and owns no separate variation list. Creates-or-updates one row per `(BroadcasterId, EventType)`.
- `TriggerAsync` — looks up the enabled response for the inbound event, renders `Message` (template) or dispatches `PipelineId`, emits `EventResponseTriggeredEvent`. No-op if disabled/missing.

### 3.9 `IBuiltinCommandService` (NET-NEW — `Services/IBuiltinCommandService.cs`)

Owns the seed catalog + per-channel enable/disable/override of built-ins (G.2a). **Closes the "commands show 0 /
seeding skipped" known issue**: built-ins are catalog-defined, never per-channel-seeded rows, so a fresh channel always
lists them.

```csharp
Task<Result<IReadOnlyList<BuiltinCommandDto>>> ListAsync(Guid broadcasterId, CancellationToken ct = default);
Task<Result<BuiltinCommandDto>>                SetEnabledAsync(Guid broadcasterId, string builtinKey, bool isEnabled, CancellationToken ct = default);
Task<Result<BuiltinCommandDto>>                SetOverridesAsync(Guid broadcasterId, string builtinKey, BuiltinCommandOverridesDto overrides, CancellationToken ct = default);
Task<Result<BuiltinResolution>>                ResolveAsync(Guid broadcasterId, string trigger, CancellationToken ct = default);
```
- `SetEnabledAsync`/`SetOverridesAsync` — upsert the `ChannelBuiltinCommand` toggle row (default state = catalog default; absent row = enabled-with-catalog-defaults), emit `BuiltinCommandToggledEvent`.
- `ResolveAsync` — merges the static `IBuiltinCommandCatalog` definition with the channel's toggle/override row into a runtime `BuiltinResolution` for `ICommandDispatcher`. Returns `Matched=false` when no built-in owns the trigger.

### 3.10 `IBuiltinCommandCatalog` + `IBuiltinCommand` (NET-NEW — `Application/Commands/Builtin/`)

Static registry of code-defined built-ins (`followage`, `uptime`, `shoutout`, `stats`, …). One class per built-in; no DB
seed rows.

**`stats` (alias `profile`) built-in** (`StatsBuiltin`, `BuiltinKey="stats"`, owned by `per-viewer-data.md`) — renders the
caller's (or `@target`'s) headline stats (messages, watch-time, points + rank, streak, first-seen) by composing
`IViewerAnalyticsService.GetProfileAsync` + `ICurrencyAccountService` + `IEconomyLeaderboardService` (no new projection —
parity with the legacy `Stats` command). Output text is template-customizable via the `BuiltinCommandOverrides.CustomResponseTemplate`
override (§4.5), like every other built-in.

```csharp
public interface IBuiltinCommand
{
    string BuiltinKey { get; }                       // e.g. "followage"
    int DefaultCooldownSeconds { get; }
    int DefaultMinPermissionLevel { get; }
    Task<Result<string>> ExecuteAsync(BuiltinCommandContext context, CancellationToken ct = default);
}

public interface IBuiltinCommandCatalog
{
    IReadOnlyCollection<IBuiltinCommand> GetAll();
    IBuiltinCommand? Get(string builtinKey);
}
```

### 3.11 `ICooldownManager` (EXTEND existing — `Common/Interfaces/ICooldownManager.cs`)

Keep the interface; **widen `string commandName`→`Guid commandId`, ids→`Guid?`; back with `CommandCooldownStates`**
(G.3) write-through so cooldowns survive restart and are correct multi-instance.

```csharp
Task<bool>      IsOnCooldownAsync(Guid broadcasterId, Guid commandId, Guid? userId = null, CancellationToken ct = default);
Task<TimeSpan?> GetRemainingAsync(Guid broadcasterId, Guid commandId, Guid? userId = null, CancellationToken ct = default);
Task            SetCooldownAsync(Guid broadcasterId, Guid commandId, TimeSpan duration, Guid? userId = null, CancellationToken ct = default);
Task            ClearAsync(Guid broadcasterId, Guid commandId, Guid? userId = null, CancellationToken ct = default);
Task            ClearAllForChannelAsync(Guid broadcasterId, CancellationToken ct = default);
```
- `SetCooldownAsync` — upserts `CommandCooldownState (CommandId, UserId)` with `LastInvokedAt`/`ExpiresAt`; L1 cache write-through. `IsOnCooldownAsync` checks both global (`UserId=null`) and per-user rows.

### 3.12 Registries (EXTEND existing — `Application/Pipeline/`)

Keep `ICommandActionRegistry` (`GetAction`/`GetAll`/`Register`) and `IConditionEvaluatorRegistry`
(`GetEvaluator`/`Register`) as-is. **Add** `IReadOnlyCollection<IConditionEvaluator> GetAll()` to the condition registry
(needed by `IPipelineCompiler`). Both are singletons populated from DI at startup.

### 3.13 `ICommandAction` / `IConditionEvaluator` (canonical contract — `Application/Pipeline/`)

The **single** consolidated contract (delete the Infrastructure duplicates). `ICommandAction` stays self-describing
for the editor.

```csharp
public interface ICommandAction
{
    string Type { get; }            // snake_case registry key, matches PipelineStep.ActionType
    string Category { get; }        // editor grouping
    string Description { get; }     // editor copy
    Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct = default);
}

public interface IConditionEvaluator
{
    string Type { get; }            // matches PipelineStepCondition.ConditionType
    Task<bool> EvaluateAsync(CompiledCondition condition, ActionContext context, CancellationToken ct = default);
}
```

### 3.13a Tier authoring-count enforcement (shared rule for §3.1 / §3.7 / §3.8)

`ICommandService`, `ITimerService`, and `IEventResponseService` cap the **count** of author-created content per the
`TierLimit` mechanism (`monetization-billing.md` §8 — the authoritative owner; this is the consumer-side wiring). No new
infrastructure: each create/update path reads the tenant entitlement through **`IBillingTierService.GetEntitlementAsync`**
(the single tier-aware source — `IFeatureGateService` is **not** forked) and compares the relevant `LimitValue` to the
current count **before persisting**.

- **Keys consumed here:** `custom_commands` (live `Command` count, §3.1 `CreateAsync`), `timers` (live `Timer` count,
  §3.7 `CreateAsync`), `event_responses` (live `EventResponse` trigger count, §3.8 `UpsertAsync` on new trigger), and
  `response_variations_per_trigger` (length of `Command.TemplateResponses` in §3.1, and of the `random_response` action's
  `Messages` on the event-response/reward pipeline in §3.8). `LimitValue = -1` ⇒ unlimited.
- **Failure shape (never silently truncate):** when `limit != -1 && currentCount >= limit`, return
  `Result.Failure("tier_limit_reached", new { LimitKey, Limit = limit, CurrentTier = entitlement.TierKey, Current = currentCount })`
  — the upsell payload drives the dashboard's in-context upgrade prompt. `BaseController` maps `tier_limit_reached` onto the
  403 arm beside `BILLING_LIMIT`.
- **Self-host = unlimited:** for `DeploymentProfile.Mode = self_host_*`, `GetEntitlementAsync` resolves every key to `-1`,
  so this exact check runs and always passes. No self-host billing, no separate code path.
- **Grandfather on downgrade:** the cap gates **adding**, never **keeping**. Existing over-limit variations/triggers are
  not deleted and continue to fire after a tier drop; only new additions are blocked until back under the cap.
- **Meter quantity, never expressiveness:** the full template language (`{{if.*}}`, nesting, pronouns, `random_response`) is
  available to **all** tiers including free and self-host — only the *volume* of authored content is tiered.

---

## 4. DTOs / contracts

All `sealed record`, in `NomNomzBot.Application` (`DTOs/...` for transport, `Pipeline/`/`Contracts/` for engine).
**All `Id`/`PipelineId`/`CommandId` are `Guid`.** Request DTOs carry DataAnnotations (`.NET 10 AddValidation()`).

### 4.1 Commands (`DTOs/Commands/CommandDtos.cs` — replace existing)
```csharp
sealed record CommandDto(Guid Id, string Name, string PrefixMode, string? CustomPrefix, string MatchMode,
    string? MatchPattern, string Tier, int MinPermissionLevel, bool IsEnabled,
    string? TemplateResponse, List<string>? TemplateResponses, Guid? PipelineId,
    int CooldownSeconds, int UserCooldownSeconds, bool CooldownPerUser, string? Description,
    List<string> Aliases, long UseCount, DateTime? LastUsedAt, DateTime CreatedAt, DateTime UpdatedAt);

sealed record CommandListItem(Guid Id, string Name, string PrefixMode, string? CustomPrefix, string MatchMode,
    string Tier, int MinPermissionLevel, bool IsEnabled,
    int CooldownSeconds, string? Description, List<string> Aliases, long UseCount, DateTime CreatedAt);

sealed record CreateCommandDto {
    [Required, MaxLength(100)] required string Name;
    [RegularExpression("^(Default|Custom|None)$")] string PrefixMode = "Default";
    [MaxLength(8)] string? CustomPrefix;             // required when PrefixMode=Custom
    [RegularExpression("^(StartsWith|Exact|Contains|Regex)$")] string MatchMode = "StartsWith";
    [MaxLength(200)] string? MatchPattern;           // required when MatchMode=Regex; must pass IRegexMatcher.ValidateAndCompile (§6.4)
    [Required, RegularExpression("^(template|pipeline|code)$")] string Tier = "template";
    [AllowedValues(0,2,4,6,10,20,30,40)] int MinPermissionLevel;  // unified A+B ladder LevelValue (roles-permissions.md §1: CommunityStanding {Everyone 0,Subscriber 2,Vip 4,Artist 6,Moderator 10} ∪ ManagementRole {Moderator 10,SuperMod 20,Editor 30,Broadcaster 40}); caller passes if MAX(community,management) level ≥ this
    [MaxLength(2000)] string? TemplateResponse;
    List<string>? TemplateResponses;
    Guid? PipelineId;                                // required when Tier != template
    [Range(0,86400)] int CooldownSeconds;
    [Range(0,86400)] int UserCooldownSeconds;
    bool CooldownPerUser;
    [MaxLength(500)] string? Description;
    List<string>? Aliases;
}
sealed record UpdateCommandDto { string? PrefixMode; string? CustomPrefix; string? MatchMode; string? MatchPattern;
    string? Tier; int? MinPermissionLevel; string? TemplateResponse;
    List<string>? TemplateResponses; Guid? PipelineId; int? CooldownSeconds; int? UserCooldownSeconds;
    bool? CooldownPerUser; string? Description; List<string>? Aliases; bool? IsEnabled; }
```

### 4.2 Dispatch / resolution (engine contracts — `Contracts/Commands/`)
```csharp
sealed record CommandDispatchRequest(Guid BroadcasterId, string Trigger, IReadOnlyList<string> Args,
    Guid UserId, string DisplayName, int UserPermissionLevel, string? MessageId, string RawMessage);
sealed record CommandDispatchResult(bool Matched, bool Executed, CommandTier? Tier, string? ResponseText,
    string? ExecutionId, string DenyReason);   // DenyReason = "" when not denied
enum CommandTier { Template, Pipeline, Code }
sealed record CommandResolution(Guid CommandId, string Name, CommandTier Tier, Guid? PipelineId,
    string? TemplateResponse, IReadOnlyList<string>? TemplateResponses, int MinPermissionLevel,
    int CooldownSeconds, int UserCooldownSeconds, bool CooldownPerUser, bool IsEnabled);
```

### 4.3 Pipelines (`DTOs/Pipelines/PipelineDtos.cs` — replace existing)
```csharp
sealed record PipelineDto(Guid Id, Guid BroadcasterId, string Name, string? Description, string TriggerKind,
    bool IsEnabled, PipelineGraphDto Graph, long TriggerCount, DateTime? LastTriggeredAt,
    DateTime CreatedAt, DateTime UpdatedAt);
sealed record PipelineListItemDto(Guid Id, string Name, string? Description, string TriggerKind, bool IsEnabled,
    long TriggerCount, DateTime? LastTriggeredAt, DateTime UpdatedAt);
sealed record CreatePipelineDto { [Required,MaxLength(200)] string Name; [MaxLength(500)] string? Description;
    [RegularExpression("^(command|event|timer|manual|webhook)$")] string TriggerKind = "command";
    bool IsEnabled = true; required PipelineGraphDto Graph; }
sealed record UpdatePipelineDto { [MaxLength(200)] string? Name; [MaxLength(500)] string? Description;
    bool? IsEnabled; PipelineGraphDto? Graph; }

sealed record PipelineGraphDto(List<PipelineStepDto> Steps);
sealed record PipelineStepDto(Guid? Id, Guid? ParentStepId, string? Branch, int Order,
    [Required,MaxLength(60)] string ActionType, Dictionary<string,object?> ConfigJson, Guid? CodeScriptId,
    bool IsEnabled, List<PipelineStepConditionDto> Conditions);
sealed record PipelineStepConditionDto(Guid? Id, [Required,MaxLength(40)] string ConditionType,
    string? Operator, string? LeftOperand, string? RightOperand, bool Negate, int Order);
sealed record PipelineValidationResult(bool IsValid, IReadOnlyList<PipelineValidationError> Errors);
sealed record PipelineValidationError(int? Order, string Code, string Message);
```

### 4.4 Engine runtime contracts (`Application/Pipeline/`)
```csharp
sealed class ActionContext {                          // replaces both old ActionContext + PipelineExecutionContext
    required string ExecutionId; required Guid BroadcasterId; required Guid TriggeredByUserId;
    required string TriggeredByDisplayName; string? MessageId; string? RedemptionId; string? RewardId;
    required string RawMessage; required IReadOnlyList<string> Args;
    required IReadOnlyDictionary<string,object?> Parameters;     // this step's resolved ConfigJson
    required IDictionary<string,string> Variables;              // pipeline-scoped, namespaced keys — TRUSTED (platform-resolved)
    IReadOnlyDictionary<string,string> TaintedVariables          // UNTRUSTED, attacker-authored (webhook payload.* — webhooks.md §7.1);
        = new Dictionary<string,string>();                       //   resolver renders these for display sinks but the engine FAILS-CLOSED
                                                                 //   if a tainted token feeds a security-sensitive param (ban/timeout UserRef,
                                                                 //   shoutout target, http_request Fqdn/Path/Method, send_webhook endpoint).
    string? EventType;                                           // the triggering event type (webhook.<provider>.<kind> / domain event) — source for send_webhook
    Guid? JournalEventId;                                        // EventJournal.EventId of the triggering event (null for non-journaled triggers)
    int HostCallCount; }                                        // engine-incremented; budget-enforced
sealed record ActionResult(bool Success, string? Output, string? ErrorMessage,
    IReadOnlyDictionary<string,string>? VariablesSet, bool StopPipeline) {
    static ActionResult Ok(string? output = null, IReadOnlyDictionary<string,string>? vars = null);
    static ActionResult Fail(string error);
    static ActionResult Stop(string? output = null); }
sealed record CompiledPipeline(Guid PipelineId, Guid BroadcasterId, int Version, int MaxStepCount,
    IReadOnlyList<CompiledStep> RootSteps);
sealed record CompiledStep(Guid StepId, int Order, ICommandAction Action,
    IReadOnlyDictionary<string,object?> ConfigJson, Guid? CodeScriptId,
    IReadOnlyList<CompiledCondition> Conditions, IReadOnlyList<CompiledStep> ThenBranch,
    IReadOnlyList<CompiledStep> ElseBranch);
sealed record CompiledCondition(IConditionEvaluator Evaluator, string? Operator,
    string? LeftOperand, string? RightOperand, bool Negate);
```

### 4.5 Timers / Event responses / Built-ins
```csharp
// Timers (DTOs/Timers/TimerDtos.cs — widen Id→Guid, add PipelineId)
sealed record TimerDto(Guid Id, string Name, List<string> Messages, Guid? PipelineId, int IntervalMinutes,
    int MinChatActivity, bool IsEnabled, DateTime? LastFiredAt, int NextMessageIndex, DateTime CreatedAt, DateTime UpdatedAt);
sealed record TimerListItem(Guid Id, string Name, int IntervalMinutes, bool IsEnabled, DateTime? LastFiredAt, int MessageCount, DateTime CreatedAt);
sealed record CreateTimerDto { [Required,MaxLength(100)] required string Name; [Required,MinLength(1)] required List<string> Messages;
    Guid? PipelineId; [Range(1,1440)] int IntervalMinutes = 30; [Range(0,10000)] int MinChatActivity; bool IsEnabled = true; }
sealed record UpdateTimerDto { [MaxLength(100)] string? Name; List<string>? Messages; Guid? PipelineId;
    [Range(1,1440)] int? IntervalMinutes; [Range(0,10000)] int? MinChatActivity; bool? IsEnabled; }

// Event responses (DTOs/EventResponses/EventResponseDtos.cs — widen Id→Guid, PipelineJson→PipelineId)
sealed record EventResponseDto(Guid Id, string EventType, bool IsEnabled, string ResponseType, string? Message,
    Guid? PipelineId, Dictionary<string,string> Metadata, DateTime CreatedAt, DateTime UpdatedAt);
sealed record EventResponseListItem(Guid Id, string EventType, bool IsEnabled, string ResponseType, DateTime UpdatedAt);
sealed record UpdateEventResponseDto { bool? IsEnabled; [RegularExpression("^(chat_message|overlay|pipeline|none)$")] string? ResponseType;
    [MaxLength(2000)] string? Message; Guid? PipelineId; Dictionary<string,string>? Metadata; }

// Persisted JSON POCO for ChannelBuiltinCommand.OverridesJson (G.2a) — Newtonsoft [VC:JSON] target.
// Lives in NomNomzBot.Domain/Entities/ beside the entity. Concrete shape so the Newtonsoft converter
// has a type. `Enabled` is the per-channel toggle; the remaining fields nullable = "no override; inherit
// the built-in's default". Service maps to/from BuiltinCommandOverridesDto (transport carries the
// override fields; persisted POCO also holds the per-channel `Enabled` toggle).
sealed record BuiltinCommandOverrides(bool Enabled, string? CustomResponseTemplate, int? CooldownSeconds,
    CommunityStanding? MinRole);

// Read-only snapshots seeded onto BuiltinCommandContext by the dispatcher from twitch-helix.md projections
// (channel info + stream state). StreamSnapshot.StartedAt feeds {{stream.uptime}}; IsLive gates online-only
// built-ins (e.g. uptime/title). Both live here beside BuiltinCommandContext.
sealed record ChannelSnapshot(Guid BroadcasterId, string Title, string GameName);
sealed record StreamSnapshot(bool IsLive, DateTimeOffset? StartedAt, int ViewerCount);

// Built-in execution context (Application/Pipeline/ — passed to IBuiltinCommand.ExecuteAsync, §3.10).
// Kept parallel to the pipeline ActionContext (§4.4): same tenant/user/args/snapshot inputs, plus the
// chat + template collaborators a built-in needs to produce its reply. `Chat` is the canonical IChatProvider
// (owned by twitch-helix.md, consumed here); `Templates` is the canonical ITemplateEngine render entry (§6.3).
sealed record BuiltinCommandContext(Guid BroadcasterId, Guid TriggeringUserId, CommunityStanding TriggeringUserRole,
    IReadOnlyList<string> Args, string RawArgs, ChannelSnapshot Channel, StreamSnapshot? Stream,
    IChatProvider Chat, ITemplateEngine Templates);

// Built-ins (DTOs/Commands/BuiltinCommandDtos.cs)
sealed record BuiltinCommandDto(string BuiltinKey, string Name, string Description, bool IsEnabled,
    int EffectiveCooldownSeconds, int EffectiveMinPermissionLevel, BuiltinCommandOverridesDto? Overrides);
sealed record BuiltinCommandOverridesDto(int? CooldownSeconds, int? MinPermissionLevel, string? ResponseTemplate);
sealed record BuiltinResolution(bool Matched, string BuiltinKey, IBuiltinCommand? Command, bool IsEnabled,
    int CooldownSeconds, int MinPermissionLevel);
```

---

## 5. Controller endpoints

All controllers extend `BaseController`, `[ApiVersion("1.0")]`, `[Authorize]`, return `StatusResponseDto<T>` /
`PaginatedResponse<T>` via `ResultResponse(...)`/`GetPaginatedResponse(...)`. Tenant `{channelId}` is `Guid` and **must be
verified owned by the authenticated principal** (IDOR must-fix #1 — `ICurrentTenantService`/`IChannelAccessService`);
mismatch ⇒ 403.

**Role gate.** Gate 1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's). Gate 2 =
`IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in
the action-key column before the service call (403 FORBIDDEN when below). The keys are seeded global `ActionDefinitions`
(schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. The
management floors are `Moderator`=10, `SuperMod`=20, `Editor`=30, `Broadcaster`=40.

| Controller | Verb + Route (`api/v{version:apiVersion}/...`) | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `CommandsController` (extend) | `GET channels/{channelId}/commands` | `PageRequestDto` | `PaginatedResponse<CommandListItem>` | management / Moderator · `commands:read` |
| | `GET channels/{channelId}/commands/{commandName}` | — | `StatusResponseDto<CommandDto>` | management / Moderator · `commands:read` |
| | `POST channels/{channelId}/commands` | `CreateCommandDto` | `StatusResponseDto<CommandDto>` (201) | management / Editor · `commands:write` |
| | `PUT channels/{channelId}/commands/{commandName}` | `UpdateCommandDto` | `StatusResponseDto<CommandDto>` | management / Editor · `commands:write` |
| | `DELETE channels/{channelId}/commands/{commandName}` | — | 204 | management / Editor · `commands:write` |
| `BuiltinCommandsController` (new) | `GET channels/{channelId}/commands/builtin` | — | `StatusResponseDto<IReadOnlyList<BuiltinCommandDto>>` | management / Moderator · `commands:builtin:read` |
| | `PUT channels/{channelId}/commands/builtin/{builtinKey}/enabled` | `{ bool IsEnabled }` | `StatusResponseDto<BuiltinCommandDto>` | management / Editor · `commands:builtin:write` |
| | `PUT channels/{channelId}/commands/builtin/{builtinKey}/overrides` | `BuiltinCommandOverridesDto` | `StatusResponseDto<BuiltinCommandDto>` | management / Editor · `commands:builtin:write` |
| `PipelinesController` (extend) | `GET channels/{channelId}/pipelines` | `PageRequestDto` | `PaginatedResponse<PipelineListItemDto>` | management / Moderator · `pipelines:read` |
| | `GET channels/{channelId}/pipelines/{id:guid}` | — | `StatusResponseDto<PipelineDto>` | management / Moderator · `pipelines:read` |
| | `POST channels/{channelId}/pipelines` | `CreatePipelineDto` | `StatusResponseDto<PipelineDto>` (201) | management / Editor · `pipelines:write` |
| | `PUT channels/{channelId}/pipelines/{id:guid}` | `UpdatePipelineDto` | `StatusResponseDto<PipelineDto>` | management / Editor · `pipelines:write` |
| | `DELETE channels/{channelId}/pipelines/{id:guid}` | — | 204 | management / Editor · `pipelines:write` |
| | `POST channels/{channelId}/pipelines/validate` | `PipelineGraphDto` | `StatusResponseDto<PipelineValidationResult>` | management / Editor · `pipelines:validate` |
| `TimersController` (extend) | `GET channels/{channelId}/timers` | `PageRequestDto` | `PaginatedResponse<TimerListItem>` | management / Moderator · `timers:read` |
| | `GET channels/{channelId}/timers/{id:guid}` | — | `StatusResponseDto<TimerDto>` | management / Moderator · `timers:read` |
| | `POST channels/{channelId}/timers` | `CreateTimerDto` | `StatusResponseDto<TimerDto>` (201) | management / Editor · `timers:write` |
| | `PUT channels/{channelId}/timers/{id:guid}` | `UpdateTimerDto` | `StatusResponseDto<TimerDto>` | management / Editor · `timers:write` |
| | `DELETE channels/{channelId}/timers/{id:guid}` | — | 204 | management / Editor · `timers:write` |
| `EventResponsesController` (extend) | `GET channels/{channelId}/event-responses` | `PageRequestDto` | `PaginatedResponse<EventResponseListItem>` | management / Moderator · `eventresponses:read` |
| | `GET channels/{channelId}/event-responses/{eventType}` | — | `StatusResponseDto<EventResponseDto>` | management / Moderator · `eventresponses:read` |
| | `PUT channels/{channelId}/event-responses/{eventType}` | `UpdateEventResponseDto` | `StatusResponseDto<EventResponseDto>` | management / Editor · `eventresponses:write` |
| | `DELETE channels/{channelId}/event-responses/{eventType}` | — | 204 | management / Editor · `eventresponses:write` |

> No public controller for `IPipelineEngine`/`ICommandDispatcher`/`IPipelineCompiler`/`ICooldownManager` — those are
> runtime-internal (driven by chat/EventSub ingestion + the timer `BackgroundService`), not HTTP-exposed.

---

## 6. Pipeline actions + conditions + template variables

### 6.1 Built-in `ICommandAction` blocks (`Type` = snake_case registry key)

Existing actions to re-target onto the canonical `ICommandAction` (keep their `Type` strings): `send_message`,
`send_reply`, `set_variable`, `delay`/`wait`, `random_response`, `stop`, `timeout`, `ban`, `shoutout`,
`delete_message`, `song_request`, `song_skip`, `song_current`, `song_queue`, `song_volume`. **Net-new for ~18-block
80% coverage + long-tail valves:** `set_counter` / `adjust_counter` (persistent `NamedCounters` G.4, tenant-scoped),
`run_code` (T3 — references `CodeScript.CurrentVersionId`, executes via `IScriptExecutor`), `http_request` (SSRF-hardened,
`HttpEgressAllowlist`-gated).

| `Type` | Config DTO (the step's `ConfigJson` shape) | Behavior (state change / side effect) |
|---|---|---|
| `send_message` | `{ string Message }` | renders `Message` via `ITemplateEngine`, sends via `IChatProvider.SendMessageAsync` |
| `send_reply` | `{ string Message }` | reply to `MessageId` via `IChatProvider.SendReplyAsync` |
| `set_variable` | `{ string Name, string Value }` | sets `Variables[Name]` (rendered); returns `VariablesSet` |
| `set_counter` | `{ string Name, long Value }` | sets the persistent counter `(ctx.BroadcasterId, Name)` to `Value` — upserts `NamedCounters` (G.4); tenant-scoped via `ctx.BroadcasterId` |
| `adjust_counter` | `{ string Name, long Delta }` | increments/decrements counter `(ctx.BroadcasterId, Name)` by `Delta` (atomic upsert; absent ⇒ starts at 0); returns the **new value** as `Output` and sets it into `Variables[Name]`; tenant-scoped via `ctx.BroadcasterId` |
| `set_viewer_data` | `{ string Key, string Value, string? Target }` | upserts the per-viewer `ViewerDatum` (G.14) for the target viewer (default = triggering viewer; `Target` resolves a `@name`/id) — string set; tenant+viewer-scoped via `ctx.BroadcasterId`. (owned by `per-viewer-data.md`) |
| `adjust_viewer_data` | `{ string Key, long Delta, string? Target }` | atomic numeric increment of the per-viewer `ViewerDatum` (G.14) for the target viewer (default = triggering viewer; absent ⇒ starts at `Delta`); returns the **new value** as `Output` and sets it into `Variables[Key]`; tenant+viewer-scoped via `ctx.BroadcasterId`. (owned by `per-viewer-data.md`) |
| `wait` | `{ int? Seconds, int? Milliseconds }` | delays (capped per-step; counts against cumulative `Wait` cap) |
| `random_response` | `{ List<string> Messages }` | picks one at random, sends to chat |
| `stop` | `{}` | sets `StopPipeline` — terminates the run cleanly |
| `timeout` | `{ string UserRef, int DurationSeconds, string? Reason }` | `IChatProvider.TimeoutUserAsync` |
| `ban` | `{ string UserRef, string? Reason }` | `IChatProvider.BanUserAsync` |
| `delete_message` | `{ string MessageId }` | `IChatProvider.DeleteMessageAsync` |
| `shoutout` | `{ string TargetChannel }` | shoutout via chat provider |
| `song_request`/`song_skip`/`song_current`/`song_queue`/`song_volume` | as today | broker-pattern music ops bound to `ctx.BroadcasterId` (token injected host-side; **no token/url in config**) |
| `run_code` | `{ Guid CodeScriptId }` *(only this guid — no inline source in config)* | resolves `CurrentVersionId`, calls `IScriptExecutor.ExecuteAsync` (Wasmtime SaaS / Jint lite); increments `HostCallCount`; capability-brokered |
| `http_request` | `{ string Fqdn, string Method, string? Path, string? BodyTemplate, string? ResultVariable }` | egress **only** to an enabled `HttpEgressAllowlist` row for the tenant; FQDN-pinned, no redirects, response-size capped (`MaxResponseBytes`). **Method enforcement:** `Method` must be in the row's `AllowedMethods` CSV (else reject). **Request-body cap:** a body is permitted only when `AllowRequestBody=true` (and never for GET/HEAD); the rendered `BodyTemplate` is **rejected, not truncated**, when it exceeds `MaxRequestBytes`. **Path enforcement:** when `PathPrefix` is set, the request `Path` must start with it (null `PathPrefix` = any path on the FQDN). Result into `ResultVariable` |
| `send_webhook` | `{ Guid OutboundWebhookEndpointId }` *(only this guid — **no** url/secret/headers/body in config; broker pattern)* | resolves the `OutboundWebhookEndpoints` (H.8) row for `ctx.BroadcasterId` and calls `IOutboundWebhookDispatcher.EnqueueForEndpointAsync(ctx.BroadcasterId, endpointId, ctx.EventType ?? "webhook.manual.send", ctx.Variables, ctx.JournalEventId)` (`webhooks.md` §3.6 — `EventType`/`JournalEventId` come from the `ActionContext` fields §4.4; `JournalEventId` is null for action-initiated sends in a non-event-triggered pipeline, matching the nullable `OutboundWebhookDelivery.JournalEventId` column). The target url, `whsec_` signing secret, body/header templates, and SSRF boundary all live on the endpoint + its `HttpEgressAllowlist` (H.7) row — Standard-Webhooks signed, FQDN-pinned, retried/dead-lettered async. Fast-ack: returns `ActionResult.Success` on enqueue; delivery is the background worker's job. The sole config key is an opaque endpoint `Guid` (carries no url/secret) so the `ICommandConfigValidator` (§3.11) invariant holds by construction. |
| `send_whisper` | `{ string TargetUserRef, string Message }` | renders `Message` via `ITemplateEngine` and sends the whisper through the shipped `ITwitchWhispersApi.SendWhisperAsync(Guid fromUserId, string toTwitchUserId, string message)`: the sender is whichever identity Guid the action passes as `fromUserId` — the action passes the channel's bot account's User Guid for `ctx.BroadcasterId` — resolved internally to `from_user_id` and sent on that identity's **own** user token (`user:manage:whispers` pre-checked per call by the sub-client; no bot-token brokering happens inside the whispers API, and no `send_whisper` action is shipped today — the from-identity contract stated here is the shipped API's, no token in config). Gated by `chat:whisper:send`; **rate-limited** per-tenant via the distributed limiter (whisper spam is a ban risk — the limits-baseline applies). `user:manage:whispers` requested progressively. |
| `play_sound` | `{ string Clip, int? Volume, bool WaitForFinish, string? Handle }` *(no url/token in config; broker pattern)* | resolves the library clip (`Clip` = id or name) for `ctx.BroadcasterId` via `ISoundClipService.ResolveForPlaybackAsync` and pushes exactly one `IOverlayClient.PlaySound` to the always-loaded overlay (effective volume = clip default unless `Volume` overrides). When `WaitForFinish`, the action awaits the clip's (capped) `DurationMs` before completing so a following action runs after playback. Unknown/disabled clip ⇒ typed action failure (no throw, **no overlay push**). (owned by `sound-system.md`) |
| `stop_sound` | `{ string? Handle, bool All }` | pushes a stop to the overlay for the named `Handle`, or stops all overlay playback when `All` (bound to `ctx.BroadcasterId`). (owned by `sound-system.md`) |
| `run_pipeline` | `{ string Pipeline, string Mode (inline\|detached), IReadOnlyList<string>? Args, bool Wait }` | invokes another of the channel's pipelines — `inline` shares the current run's variable bag (merged on return), `detached` is an independent run (optional `Wait`); recursion-depth-capped (fail-closed at `MaxRecursionDepth`). (owned by `pipeline-control-flow.md`) |
| `break` | — | exits the enclosing `loop` block (no-op outside a loop). (owned by `pipeline-control-flow.md`) |
| `continue` | — | skips to the enclosing loop's next iteration (no-op outside a loop). (owned by `pipeline-control-flow.md`) |

All actions: **fail-closed** (unknown `Type` rejected at save and at compile), **no tenant/credential/url config keys**
(`ICommandConfigValidator` invariant), `Guid`-typed `BroadcasterId` from `ActionContext`.

### 6.2 Condition evaluators (`Type` matches `PipelineStepCondition.ConditionType`)

| `Type` | Operands | Behavior |
|---|---|---|
| `user_role` | `LeftOperand` = required role/level | true when `ctx` user level ≥ required (ascending ladder); fail-closed unknown role |
| `random` | `LeftOperand` = percent/chance | true with given probability |
| `var_compare` | `LeftOperand`, `Operator` (`contains`\|`equals`\|`iequals`\|`startswith`\|`endswith`\|`matches`\|`gt`\|`lt`\|`gte`\|`lte`), `RightOperand` | compares a (rendered) variable to a value via the **shared comparator** (below) |
| `cooldown` | `LeftOperand` = scope key | true when not on the named cooldown (checks `ICooldownManager`) |

`Negate` inverts the result. **Unknown `ConditionType` ⇒ fail-closed (run abort), never treat-as-true** (reverses the
current live defect).

**Shared comparator (`IValuePredicate` — `Application/Pipeline/IValuePredicate.cs`).** The operator set above is defined
**once** and reused by **two surfaces**: this pipeline-gating `var_compare` condition (author-driven branching) **and** the
render-time `{{if.<path>.<op>:…}}` template predicate (§6.3.2). One operator table, one evaluator — no second comparator.
Semantics: `gt`/`lt`/`gte`/`lte` are **numeric** (both sides parsed as numbers; if either side is non-numeric ⇒ false);
`contains`/`equals`/`startswith`/`endswith` are **ordinal** string ops; `iequals` is case-insensitive; `matches` is a regex
test via the **shared `IRegexMatcher`** (§6.4 — NonBacktracking, time-bounded, same ReDoS policy as `MatchMode=Regex`).

### 6.3 Template variables (`VariableResolver` — `Application/Pipeline/VariableResolver.cs`, replace)

Supports namespaced + parameterized tokens `{{namespace.key}}` and
`{{namespace.key:arg1:arg2}}` (one `:` splits key from the arg list; further `:` split args; the **pronoun `verb:` pair**
and the **`if.*` conditional helper** (§6.3.2) use `|` internally to split their two branches — the `if.*` value-predicate
form additionally carries an operator suffix + an operand colon-arg before the branch run — see those blocks). Resolution is **pure** (no side effects, no I/O) over the `ActionContext.Variables`
bag seeded by the dispatcher/engine **before** the render.

**Balanced-brace recursive resolver (replaces the single-pass `[GeneratedRegex]` replace).** A token's branch text (an
`if` then/else) may itself contain `{{ … }}`, which a flat single-pass regex cannot match. The resolver is therefore a
**balanced-brace recursive walker**: it scans left-to-right, finds each top-level `{{ … }}` by counting brace depth (so
nested `{{ … }}` inside a branch are paired correctly), resolves the token, and — for a token whose value is itself
template text (an `if` branch) — recurses into the **selected** branch text. The `VariableResolver` keeps its
`static partial` shape, but the brace-pairing/recursion is hand-written (a `[GeneratedRegex]` is still used for the
leaf-token *key:args* shape inside one matched `{{ … }}`, not for locating nested braces). Properties of the walk:

- **Lazy branch evaluation.** For an `if`, only the **SELECTED** branch is resolved; the unchosen branch text is never
  walked — cheaper and side-effect-free (no token in a dead branch ever resolves).
- **Bounds (DoS — bounded and allowed, fail-closed).** Concrete limits, enforced per render:
  - **Max recursion depth 8.** Exceeding depth 8 emits the **raw, unresolved token text verbatim** and stops descending
    that subtree (fail-closed; the rest of the template still renders) and logs once.
  - **Total expanded-output cap ≈ 4 KB** (chat-bound). Once cumulative rendered output reaches the cap, expansion stops
    and the truncated result is returned (logged).
  - **Cycle detection for `set_variable` self/mutual references.** A per-render **visited set** tracks variable keys
    currently being expanded; if resolving `{{a}}` re-enters `{{a}}` (directly or via `{{b}}`→`{{a}}`), the cyclic token
    resolves to **empty string** and the cycle is logged — never an infinite loop.
- **Pronoun smart-alternation preserved.** The §6.3.1/Pronoun-helpers left-to-right ordering and the
  `nameIntroduced` / `lastSubjectSingular` state are **maintained across the recursive walk in document order** — nested
  tokens resolve at their textual position, so a pronoun/verb inside an `if` branch sees the same alternation state it
  would at that point in flat text, keeping subject/verb agreement correct.
- **Taint context threaded through nesting.** The recursion carries the `TaintedVariables` context down every level: a
  nested token that resolves a tainted `payload.*` value still hits the §7 taint boundary — **tested-but-not-emitted is
  fine** (a predicate may read it for a comparison), but a tainted value **emitted into a security-sensitive sink fails
  closed** exactly as at the top level. Nesting does not launder taint.
- **Unknown token ⇒ empty string** (render-time, non-fatal) is preserved at every depth.

Illustrative nested example (owner's): `{{if.user.name.contains:Duka:Hey Duka {{if.user.ismod:🤠|}}|}}` — the outer
predicate selects the `then` branch only when the name contains `Duka`; that branch text itself contains a nested `if`
(the 🤠 shown only for mods), resolved lazily one level down.

**`ITemplateEngine` contract** (`Application/Pipeline/ITemplateEngine.cs`; impl `TemplateEngine` is `VariableResolver`-backed, registered `AddSingleton<ITemplateEngine, TemplateEngine>()`). It is the single render entry point — `ICommandDispatcher` (T1 responses), the pipeline actions (`send_message`/`send_reply`/…), and the webhook subsystem (outbound `BodyTemplate`/`CustomHeaders`, inbound display sinks) all call it; no caller pokes `VariableResolver` directly:

```csharp
public interface ITemplateEngine
{
    // Balanced-brace recursive render of `template` against a flat variable bag (namespaced keys, §6.3). Pure,
    // synchronous, I/O-free (the bag is pre-seeded by the caller). Unknown token -> empty string. Nested {{ }} inside an
    // `if` branch ARE resolved (only the SELECTED branch; lazy), bounded by depth 8 / ~4 KB output / cycle detection
    // (§6.3). A substituted VARIABLE VALUE is never re-scanned as a template (no second-order injection) — only an
    // author-written `if` branch (template text the author typed) recurses. Used for chat responses, action messages,
    // and outbound webhook bodies.
    string Render(string template, IReadOnlyDictionary<string,string> variables);

    // Convenience overload that renders against an ActionContext: merges the trusted `Variables` bag and (read-only,
    // display-sink only) the `TaintedVariables` bag (webhooks.md §7.1) into the resolution scope; the tainted context is
    // threaded through the recursive walk, so a nested token resolving a tainted payload.* value still hits the §7
    // boundary at any depth. Security-sensitive action params do NOT use this overload — they bind via the engine's
    // fail-closed tainted-token check, not by rendering.
    string Render(string template, ActionContext context);
}
``` 

Any token whose backing namespace needs a Helix call, an economy
read, or a music read is **pre-resolved into the bag by the dispatcher** (column **Needs-context** below states what to seed)
so the resolver itself stays I/O-free and order-independent (the pronoun pass is the one documented exception).

**Conventions for every table:** *Data source* = the schema `Table.Column` or service that backs the value (per
`2026-06-16-database-schema.md`, `twitch-helix.md`, `economy.md`, `music-sr.md`, `roles-permissions.md`). *Needs-context* =
what the dispatcher must seed/resolve into `Variables` for the token to render (`—` = already on the chat/command context,
no extra work). Capitalization-of-key, the `:arg` form, and "unknown token ⇒ empty string" apply uniformly. Tokens are
grouped one table per namespace.

> **Inbound-webhook namespace (`webhook.*` / `payload.*`).** When a pipeline/event-response is triggered by a verified
> inbound webhook (`TriggerKind=webhook`; owner `webhooks.md`), the **webhook dispatcher pre-seeds** the parsed body into
> the `ActionContext.Variables` bag **before** the render — `{{webhook.provider}}`/`{{webhook.event}}`/`{{webhook.id}}`
> plus every flattened body field as `{{payload.<field>}}` (e.g. `{{payload.from_name}}`, `{{payload.amount}}`). The
> `VariableResolver` stays **pure / I-O-free** — this is exactly the documented "dispatcher seeds, resolver renders"
> pattern used for the Helix/economy/music tokens; **no resolver/engine change**, only a new pre-seeded namespace.
> (Outbound webhook body/header templates are the *author's* templates rendered by the same `ITemplateEngine` over the
> triggering event's standard bag — no outbound-specific namespace.)

#### 6.3.1 Date/time formatter layer (applies to every duration- and instant-typed token)

The resolver applies **one uniform formatter** to any token whose backing value type is a **duration**
(`TimeSpan`) or an **instant** (`DateTime`/`DateTimeOffset`). It is applied **centrally by the resolver based on the
token's value type** — *not* re-implemented per token — so the same `:arg` forms work identically on every date helper,
present and future. New duration/instant tokens get the formatter for free the moment they declare their value type; the
catalog tables tag the affected tokens with **[duration — accepts the §6.3.1 formatter]** / **[instant — accepts the
§6.3.1 formatter]**.

**Duration tokens** (e.g. `{{user.accountage}}`, `{{user.followage}}`, `{{user.subtenure}}`,
`{{user.watchtime}}`, `{{user.lastseen}}`, `{{stream.uptime}}`):

- **no arg** → humanized, the **2 most-significant** non-zero units (`"3 years, 2 months"`).
- **`:N`** (integer) → the **N** most-significant non-zero units (`:3` → `"3 years, 2 months, 5 days"`).
- **`:unit:unit…`** → **exactly** the listed units, in descending order, regardless of which are zero. Unit codes:
  `y`=years, `mo`=months, `w`=weeks, `d`=days, `h`=hours, `m`=minutes, `s`=seconds.
  - **Explicit collision call-out: `m` = minutes and `mo` = months.** So `:y:m` = years + **minutes**; years + **months**
    is `:y:mo`. The resolver never infers; the code is literal.
- **`:short`** → compact form (`"3y 2mo"`, `"2h 14m 03s"`).

**Instant tokens** (e.g. `{{user.createdat}}`, `{{user.followsince}}`, `{{user.subsince}}`, `{{channel.createdat}}`,
`{{stream.startedat}}`, `{{date.now}}`, `{{time.now}}`):

- **no arg** → an **unambiguous, locale-independent** human format — month-name + 24h time rendered in the **channel
  timezone** (the broadcaster's `Users.Timezone` via `Channels.OwnerUserId`, **UTC fallback**) with an explicit tz label,
  e.g. `16 Jun 2026 14:30 CEST`. Deliberately **not** locale-numeric (`06/07/2026` is ambiguous MM/DD-vs-DD/MM — the exact
  failure ISO 8601 exists to prevent).
- **presets:** `:iso` (**ISO 8601 / RFC 3339, UTC** — `2026-06-16T14:30:00Z`, the machine-exact standard) · `:date` ·
  `:time` · `:datetime` · `:shortdate` · `:longdate` · `:relative` (`"3 years ago"` — timezone-independent).
- **Data layer is always ISO 8601 / UTC `DateTimeOffset`** — every persisted column and v1-API datetime is RFC 3339 UTC;
  this formatter affects **chat rendering only**, never the stored or transported value.
- **Raw `strftime`/.NET format patterns ride the `:raw=` sentinel arg.** The `:` inside a pattern such as `HH:mm`
  collides with the `:` arg delimiter, so a raw format pattern is introduced by the literal `:raw=` prefix and the
  **entire remainder of the token (up to the closing `}}`) is the verbatim .NET custom format string** — no further `:`
  splitting is applied past `raw=`. Example: `{{date.now:raw=dd-MM-yyyy HH:mm}}` renders `16-06-2026 14:30`. A literal
  `}}` inside a pattern is escaped `\}\}`. This is the one escaping rule; the named instant presets cover every common
  case so `:raw=` is the explicit escape hatch for bespoke patterns.

#### 6.3.2 Conditional helper (`if.*`) — replaces raw boolean render tokens

Raw booleans rendering the literal strings `"true"`/`"false"` into chat are useless, so **no boolean is a printable
catalog token.** Each boolean STATE survives in two non-render roles only:

1. a pipeline **condition operand** — booleans belong in the §6.2 condition layer (`user_role`, `var_compare`, …), not
   in rendered text; and
2. an **`if.*` boolpath** — the input to the conditional helper below.

Where a fixed word reads naturally, a **humanized word token** is provided instead of a boolean — e.g.
`{{stream.status}}` → `"live"`/`"offline"` (and the existing pronoun-block `{{user.status}}`).

**`if` namespace — one conditional helper, two forms.** It reuses the **exact `:A|B` two-branch shape** already used by the
pronoun `{{user.verb:wins|win}}` token (the single `:` separates key from the arg run; `|` splits the two branches). It
takes either a **boolean path** or a **value predicate**:

**Form A — boolean path** (unchanged):

- `{{if.<boolpath>:<then>|<else>}}` → renders `<then>` when `<boolpath>` is true, else `<else>`.

**Form B — value predicate** (new — any value token path + an operator):

- `{{if.<path>.<op>:<operand>:<then>|<else>}}` → renders `<then>` when `<path> <op> <operand>` holds, else `<else>`.
- `<path>` = **any value token path** (`user.name`, `channel.title`, `args.1`, `payload.*`, …) — its resolved value is the
  left side of the test.
- `<op>` ∈ `contains` \| `equals` \| `iequals` \| `startswith` \| `endswith` \| `matches` \| `gt` \| `lt` \| `gte` \| `lte`
  — the **same operator set + same comparator** as the §6.2 `var_compare` condition (`IValuePredicate`; `matches` = regex
  via the shared `IRegexMatcher` §6.4; `gt`/`lt`/`gte`/`lte` numeric; string ops ordinal; `iequals` case-insensitive).
- `<operand>` = the **single colon-arg after the op**; `<then>|<else>` = the **next colon-arg, pipe-split** — so branch
  text may freely contain colons (only the operand's colon and the colon before the branch run are structural).
- Example: `{{if.user.name.contains:Duka:Hey Duka! 🎉|}}` → greets when the triggering user's name contains `Duka`, else
  nothing.

**Resolution — which form.** The resolver inspects the key (everything before the first structural `:`): if it **ends in a
known `.<op>` suffix** it is Form B (value predicate); otherwise it is Form A (boolean path). The two forms never collide
because the boolpath catalog contains no name ending in a known operator.

**Both forms:**

- **Empty `<else>` is allowed:** `{{if.user.ismod:🛡️|}}` renders the shield for mods and nothing otherwise.
- `<then>`/`<else>` are **rendered text** — inner tokens inside a branch are resolved (e.g.
  `{{if.user.issub:thanks {{user.name}}|}}`).
- **Branches may nest `{{ … }}`, including a nested `if`** — the selected branch is resolved by the balanced-brace
  recursive resolver (§6.3, lazy: only the chosen branch walks), bounded by **depth 8 / ~4 KB output / cycle detection**.
  Example: `{{if.user.name.contains:Duka:Hey Duka {{if.user.ismod:🤠|}}|}}`.

> **Security (taint boundary — consistent with §7).** The rendered output is **always the author's `<then>`/`<else>`
> literal, never the tested value.** A predicate may *read* a tainted `{{payload.*}}` value to evaluate the test, but that
> value is **never emitted** — only the author-fixed branch text is. So `{{if.payload.from_name.equals:VIP:welcome VIP|}}`
> is safe: the untrusted `payload.from_name` is read for the comparison and discarded; the emitted string is the author's
> own literal. This is exactly the §7 ITemplateEngine "tested-but-not-emitted is fine" rule — a tainted value driving a
> *display* branch is not a sink. (An author who *does* want to echo the value writes `{{payload.from_name}}` directly,
> which the existing display-sink rules already cover; nothing about `if.*` changes that boundary.)

**Available boolpaths** (only states backed by real seeded context — cross-checked against the catalog; none invented):

| Boolpath | True when | Backed by |
|---|---|---|
| `stream.live` | stream is live | `Streams.EndedAt IS NULL` (seed live flag) |
| `user.ismod` | level ≥ `Moderator` | derived from `{{user.level}}` (seed resolved level) |
| `user.issub` | active subscription | `TwitchSubscribers.EndedAt IS NULL` (seed sub row) |
| `user.isvip` | `Vip` standing | `ChannelCommunityStandings.Standing` (seed resolved standing) |
| `user.isfollower` | following the channel | `TwitchFollowers` row exists (Helix/cached) |
| `user.isbroadcaster` | level = `Broadcaster` (40) | derived from `{{user.level}}` (seed resolved level) |

These same boolpaths are equally valid under a user-bearing namespace the dispatcher resolves (e.g. `if.target.issub`)
wherever that namespace's state is seeded.

**Form C — composite conditions (`all` / `any` / `!`).** Combine conditions without a full expression grammar:

- `{{if.all(<cond>, <cond>, …):<then>|<else>}}` — true iff **all** conditions hold (AND).
- `{{if.any(<cond>, <cond>, …):<then>|<else>}}` — true iff **any** condition holds (OR).
- **Negation:** a `!` prefix on any condition (`!user.ismod`, `!user.name.contains:bot`) — the only negation form.
- Each `<cond>` is a **Form A boolpath** or a **Form B predicate** (`<path>.<op>:<operand>`), evaluated by the same
  `IValuePredicate`. Parens `()` bound the list; **top-level `,`** separates conditions (operands are single values and
  carry no top-level comma). `<then>`/`<else>` pipe-split as in A/B, and either branch may nest `{{ … }}` (lazy, via the
  §6.3 recursive resolver).
- **Bound:** at most **8 conditions** per `all`/`any` → save fails `too_many_conditions`. A condition list does **not**
  nest another `all`/`any` inside it (the parser stays flat) — compose deeper logic by nesting an `if` in a branch, or use
  `run_code`.
- Examples: `{{if.all(user.ismod, user.name.contains:Duka):Hey Duka mod!|}}` ·
  `{{if.any(user.issub, user.isvip):thanks for the support!|}}` · `{{if.all(user.ismod, !user.name.contains:bot):…|…}}`.

**Bounded by design — the ceiling (decided).** The condition language is exactly **{Form A boolpath, Form B single
predicate, Form C `all`/`any`/`!`}** over the fixed §6.2 operator set (shared `IValuePredicate`), plus branch **nesting**
via the §6.3 recursive resolver. There is **no parenthesized AND/OR-precedence algebra**, no arithmetic, and no
user-defined functions in the template parser — that is deliberately the **`run_code`** action's job (the escape hatch for
arbitrary logic). The template surface and the pipeline `var_compare` condition speak the same small comparator; anything
beyond `all`/`any`/`!` + nesting drops to `run_code`.

**Validation.** Condition syntax is checked at save by `ICommandConfigValidator` → fail-closed `invalid_condition` for an
unknown op/path/combinator or unbalanced `{{}}`/`()`; `too_many_conditions` past the 8-cap; a `matches` operand runs
through `IRegexMatcher.ValidateAndCompile` (§6.4). At render time an unknown token is still empty-string (non-fatal), and a
malformed-but-persisted condition fails closed to the `<else>` branch.

#### `user.*` — the triggering user (grammar/pronoun keys live in the **Pronoun helpers** block below, not repeated here)

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{user.name}}` / `{{user.username}}` | login name | `Users.Username` | — |
| `{{user.displayname}}` | display name | `Users.DisplayName` | — |
| `{{user.id}}` | internal user id (guid) | `Users.Id` | — |
| `{{user.twitchid}}` | Twitch numeric user id | Helix `GET /users` `id` | seed from chat tags (already present) |
| `{{user.mention}}` | `@displayname` | `Users.DisplayName` (prefixed) | — |
| `{{user.link}}` | `https://www.twitch.tv/{login}` | `Users.Username` | — |
| `{{user.color}}` | chat name color (`#RRGGBB`) | `Users.Color` | — |
| `{{user.role}}` | effective role name, friendly-cased: `Viewer · Subscriber · VIP · Artist · Moderator · Super Mod · Editor · Broadcaster`. The `Everyone(0)` ladder floor (absence of an elevated role) renders **`Viewer`**, never the literal `Everyone` sentinel — `Everyone` stays a gating-only value (the §6.2 `user_role` condition floor). | `IRoleResolver.ResolveEffectiveLevelAsync` → `roles-permissions.md` ladder, mapped to a display label | seed resolved level |
| `{{user.level}}` | numeric level (0/2/4/6/10/20/30/40) | same resolver `LevelValue` | seed resolved level |
| `{{user.subtier}}` | `1`/`2`/`3` (mapped from `1000/2000/3000`) | `TwitchSubscribers.Tier` | seed sub row |
| `{{user.submonths}}` | cumulative sub months | `TwitchSubscribers.CumulativeMonths` | seed sub row |
| `{{user.substreak}}` | current sub streak months | `TwitchSubscribers.StreakMonths` | seed sub row |
| `{{user.subsince}}` | sub start date **[instant — accepts the §6.3.1 formatter]** | `TwitchSubscribers.StartedAt` | seed sub row |
| `{{user.followage}}` | follow duration **[duration — accepts the §6.3.1 formatter]** | `now − TwitchFollowers.FollowedAt` | **Helix** follow lookup (or cached row) |
| `{{user.followsince}}` | follow date **[instant — accepts the §6.3.1 formatter]** | `TwitchFollowers.FollowedAt` | **Helix** follow lookup (or cached row) |
| `{{user.accountage}}` | account age **[duration — accepts the §6.3.1 formatter]** | `now − Users.CreatedAt` | — |
| `{{user.createdat}}` | account creation date **[instant — accepts the §6.3.1 formatter]** | `Users.CreatedAt` | — |
| `{{user.lastseen}}` | time since last seen **[duration — accepts the §6.3.1 formatter]** | `Users.LastSeenAt` | — |
| `{{user.watchtime}}` | total watch time **[duration — accepts the §6.3.1 formatter]** | `Σ WatchSessions.DurationSeconds` | seed aggregate (watch-session read) |
| `{{user.watchstreak}}` | consecutive-stream streak | `WatchStreaks.CurrentStreak` | seed watch-streak read |
| `{{user.timezone}}` | IANA timezone | `Users.Timezone` | — |
| `{{user.balance}}` | currency balance | `CurrencyAccountDto.Balance` (`ICurrencyAccountService`) | seed economy read |
| `{{user.rank}}` | leaderboard position (1-indexed) | `LeaderboardEntryDto.Rank` (`IEconomyLeaderboardService`) | seed economy read |

#### `target.*` — a resolved target (first user-arg / shoutout / timeout subject)

Mirrors **every** `user.*` key (same data sources, same Needs-context) but for the resolved target user instead of the
triggering user — e.g. `{{target.name}}`, `{{target.followage}}`, `{{target.lastgame}}`, `{{target.balance}}`. Pronoun
grammar keys also apply (see Pronoun helpers). The dispatcher resolves the target from `{{args.1}}` (strip a leading `@`),
looks up / lazily creates the `Users` row, and seeds the same per-user reads under the `target` namespace. Unresolved target
⇒ all `target.*` ⇒ empty string.

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{target.lastgame}}` | last game the target streamed (shoutout) | Helix `GET /channels` `GameName` for target | **Helix** channel lookup for target |
| `{{target.link}}` | `https://www.twitch.tv/{target login}` | resolved target `Username` | seed target resolution |

#### `channel.*` — the broadcaster channel (tenant root)

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{channel.name}}` | channel login name | `Channels.Name` | — |
| `{{channel.id}}` | channel id (guid) | `Channels.Id` | — |
| `{{channel.title}}` | current stream title | `Channels.Title` | — |
| `{{channel.game}}` / `{{channel.category}}` | game/category name | `Channels.GameName` | — |
| `{{channel.gameid}}` | game/category id | `Channels.GameId` | — |
| `{{channel.tags}}` | comma-joined tags | `Channels.Tags` `[VC:JSON]` | — |
| `{{channel.contentlabels}}` | comma-joined content labels | `Channels.ContentLabels` `[VC:JSON]` | — |
| `{{channel.language}}` | broadcaster language | `Channels.Language` | — |
| `{{channel.url}}` | `https://www.twitch.tv/{name}` | `Channels.Name` | — |
| `{{channel.createdat}}` | channel creation date **[instant — accepts the §6.3.1 formatter]** | `Channels.CreatedAt` | — |
| `{{channel.followers}}` | follower total count | Helix `GET /channels/followers` `total` | **Helix** count call |
| `{{channel.subs}}` | subscriber total count | Helix `GET /subscriptions` `total` | **Helix** count call |

#### `stream.*` — live stream state

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{stream.status}}` | `live`/`offline` (humanized word token) | `Streams.EndedAt IS NULL` (or Helix `GET /streams` non-empty) | seed live flag |
| `{{stream.startedat}}` | stream start time **[instant — accepts the §6.3.1 formatter]** | `Streams.StartedAt` | — |
| `{{stream.uptime}}` | `now − StartedAt` **[duration — accepts the §6.3.1 formatter]** | `Streams.StartedAt` | — |
| `{{stream.viewers}}` | current viewer count | Helix `GET /streams` `viewer_count` | **Helix** stream read |
| `{{stream.peakviewers}}` | peak viewers this stream | `Streams.ViewerCountPeak` | — |
| `{{stream.title}}` | current stream title | `Streams.Title` | — |
| `{{stream.game}}` / `{{stream.category}}` | current category | `Streams.GameName` | — |

#### `args.*` — command argument access

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{args.1}}` … `{{args.N}}` | nth whitespace-split arg (1-indexed) | command invocation text | — |
| `{{args.all}}` | full argument string after the command | invocation text | — |
| `{{args.count}}` | number of args | invocation text | — |
| `{{args.after:N}}` | all args from position N onward (joined) | invocation text | — |
| `{{args.1.user}}` | parse arg 1 as a user → resolve into `target.*` | arg + `Users` lookup | dispatcher resolves arg→target |

#### `random.*` — randomness (deterministic-pure given the seeded RNG/chatter set)

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{random.number:min:max}}` | inclusive integer in range | seeded RNG | — |
| `{{random.pick:a,b,c}}` | one comma item at random | literal args | — |
| `{{random.percent}}` | `0`–`100` integer | seeded RNG | — |
| `{{random.coin}}` | `heads`/`tails` | seeded RNG | — |
| `{{random.chatter}}` | a random present viewer's display name | Helix Get Chatters (`GET /chat/chatters`, cached) — see `twitch-helix.md` §3.2 | dispatcher pre-loads the chatter list before render (resolution stays pure) |

#### `date.*` / `time.*` — clock (rendered in `Users.Timezone` when present, else channel/UTC)

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{date.now}}` | current date, channel tz (unambiguous default) **[instant — accepts the §6.3.1 formatter]** | dispatcher clock | seed render time |
| `{{date.format:raw=fmt}}` | custom-formatted date — the verbatim .NET pattern follows the `:raw=` sentinel (§6.3.1; `}}` escaped `\}\}`), e.g. `{{date.format:raw=dd-MM-yyyy}}`; the instant presets on `{{date.now}}` cover the common cases | dispatcher clock | seed render time |
| `{{date.dayofweek}}` | weekday name | dispatcher clock | seed render time |
| `{{time.now}}` | current time, channel tz (unambiguous default) **[instant — accepts the §6.3.1 formatter]** | dispatcher clock | seed render time |
| `{{time.format:raw=fmt}}` | custom-formatted time — the verbatim .NET pattern follows the `:raw=` sentinel (§6.3.1; `}}` escaped `\}\}`), e.g. `{{time.format:raw=HH:mm}}`; the instant presets on `{{time.now}}` cover the common cases | dispatcher clock | seed render time |

#### `economy.*` — currency & leaderboard (gate: economy enabled for tenant)

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{economy.name}}` | currency name (singular) | `CurrencyConfigDto.CurrencyName` (`ICurrencyConfigService`) | seed config read |
| `{{economy.nameplural}}` | currency name (plural) | `CurrencyConfigDto.CurrencyNamePlural` | seed config read |
| `{{economy.balance:user}}` | a named user's balance | `CurrencyAccountDto.Balance` (`ICurrencyAccountService`) | resolve user + seed economy read |
| `{{economy.rank:user}}` | a named user's leaderboard rank | `LeaderboardEntryDto.Rank` (`IEconomyLeaderboardService`) | resolve user + seed economy read |
| `{{economy.top:N}}` | top-N leaderboard (name + value list) | `IEconomyLeaderboardService.GetRankingAsync` | seed leaderboard read |
| `{{economy.earned:user}}` | lifetime earned | `CurrencyAccountDto.LifetimeEarned` | resolve user + seed economy read |
| `{{economy.spent:user}}` | lifetime spent | `CurrencyAccountDto.LifetimeSpent` | resolve user + seed economy read |

#### `song.*` / `music.*` — now-playing & queue (gate: a music provider connected — Spotify/YouTube)

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{song.title}}` | now-playing title | `NowPlayingDto.TrackName` (`IMusicService`) | seed now-playing read; provider gate |
| `{{song.artist}}` | now-playing artist | `NowPlayingDto.Artist` | seed now-playing read; provider gate |
| `{{song.album}}` | album name | `NowPlayingDto.Album` | seed now-playing read; provider gate |
| `{{song.art}}` | cover-art URL | `NowPlayingDto.ImageUrl` | seed now-playing read; provider gate |
| `{{song.duration}}` | track length | `NowPlayingDto.DurationSeconds` | seed now-playing read; provider gate |
| `{{song.position}}` | playback progress | `NowPlayingDto.ProgressSeconds` | seed now-playing read; provider gate |
| `{{song.requester}}` | who requested the current song | `NowPlayingDto.RequestedBy` | seed now-playing read; provider gate |
| `{{music.queuelength}}` | queued track count | `SongRequestQueueDto.CurrentLength` (`ISongRequestQueueStateService`) | seed queue-state read |
| `{{music.next}}` | next queued track (title — artist) | `MusicQueueDto.Queue[0]` (`IMusicService.GetQueueAsync`) | seed queue read |

#### `command.*` — the running command

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{command.name}}` | command name | `Commands.Name` (current) / `CommandUsage.CommandNameSnapshot` | — |
| `{{command.prefix}}` | the effective prefix of the firing command (`Default`→channel `DefaultCommandPrefix`, `Custom`→`CustomPrefix`, `None`→empty) | `Commands.PrefixMode`/`CustomPrefix` + `Channels.DefaultCommandPrefix` | — (resolved by the dispatcher at match time) |
| `{{command.usagecount}}` | total successful invocations | `COUNT(CommandUsage WHERE WasSuccessful)` | seed usage aggregate |
| `{{command.cooldown}}` | remaining cooldown (humanized) | `CommandCooldownStates` (`ICooldownManager`) | seed cooldown read |

#### `count.*` — persistent named counters

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{count.<name>}}` | current value of the persistent counter `<name>` (e.g. `{{count.deaths}}`); renders `0` if the counter does not exist | `NamedCounters.Value` (G.4) | counter lookup by `(BroadcasterId, <name>)` (dispatcher pre-loads referenced counters into the bag) |

#### `loop.*` / `switch.*` — control-flow block scope (owned by `pipeline-control-flow.md`)

Block-scoped — bound by the enclosing `loop`/`switch` block during the tree walk; resolved from the run bag, pure (no I/O). Empty string outside the block.

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{loop.item}}` | current item of the enclosing `foreach` loop | run bag (engine-bound per iteration) | — (engine seeds inside the loop block) |
| `{{loop.index}}` | current iteration index, **0-based** | run bag (engine-bound per iteration) | — (engine seeds inside the loop block) |
| `{{loop.count}}` | total iteration count of the enclosing loop | run bag (engine-bound) | — (engine seeds inside the loop block) |
| `{{switch.value}}` | the evaluated value of the enclosing `switch` block | run bag (engine-bound) | — (engine seeds inside the switch block) |

#### `viewer.*` — per-viewer custom data + headline stats (owned by `per-viewer-data.md`)

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{viewer.data.<key>}}` / `{{target.data.<key>}}` | the stored per-viewer value for `<key>` (e.g. `{{viewer.data.deaths}}`); empty if unset | `ViewerData.Value` (G.14) | dispatcher pre-seeds referenced keys for the triggering viewer (+ target) via `IViewerDataService.LoadKeysAsync` (the `{{count.*}}` rule — resolver stays I/O-free) |
| `{{viewer.messages}}` | total messages sent in this channel | `ViewerProfileDto.TotalMessages` (analytics M.1) | seed viewer-profile read |
| `{{viewer.watchtime}}` | accumulated watch time (humanized) | `ViewerProfileDto.TotalWatchSeconds` (analytics M.1) | seed viewer-profile read |
| `{{viewer.firstseen}}` | when first seen in this channel | `ViewerProfileDto.FirstSeenAt` (analytics M.1) | seed viewer-profile read |
| `{{viewer.redemptions}}` | total channel-point redemptions | `ViewerProfileDto.TotalRedemptions` (analytics M.1) | seed viewer-profile read |
| `{{viewer.songrequests}}` | total song requests | `ViewerProfileDto.TotalSongRequests` (analytics M.1) | seed viewer-profile read |

Each `viewer.*` token has a `{{target.*}}` mirror under the existing `target.*` mirror convention (above) — for the
resolved target instead of the triggering viewer.

#### `bot.*` — the bot identity

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{bot.name}}` | bot account login | `BotAccounts.BotUsername` | — |
| `{{bot.prefix}}` | channel default command prefix (default `!`) | `Channels.DefaultCommandPrefix` | — (on the channel context) |

#### custom — author-defined and HTTP results

| Token | Description | Data source | Needs-context |
|---|---|---|---|
| `{{<key>}}` (any `set_variable` key) | a value written earlier in the run | `set_variable` action wrote it to `Variables` | prior `set_variable` step |
| `{{<ResultVariable>}}` | `http_request` response captured into a var | `http_request` `ResultVariable` (allowlisted egress) | prior `http_request` step |

**Pronoun helpers — grammatically-correct sentences (ports the current bot's `TemplateHelper`).** The triggering user's
`Users.PronounId` → `Pronouns` row (schema R.1) drives a **single stateful left-to-right pass**: the resolver tracks
`nameIntroduced` + `lastSubjectSingular`, **maintained across the recursive walk in document order** — nested tokens
inside an `if` branch resolve at their textual position, so the alternation state advances exactly as it would in flat
text and subject/verb agreement stays correct through nesting (pure w.r.t. the outside world, but **order-dependent
within one template** — these tokens are the documented exception to "resolution is pure per-token"). Two modes,
selected by whether the user has a grammatical pronoun:

- **Explicit-pronoun mode** — `Subject` set and `Key` ∉ {`any`,`other`}: every reference renders the **same** pronoun,
  consistently, from the R.1 columns. `{{user.subject}}`→`Subject`, `{{user.object}}`→`Object`,
  `{{user.possessive}}`→`PossessiveDeterminer`, `{{user.possessivepronoun}}`→`PossessivePronoun`,
  `{{user.reflexive}}`→`Reflexive`. Agreement from `IsSingular` (or `Subject` ∈ {he,she}).
- **Smart-alternation mode** — no pronoun set, or non-grammatical `any`/`other`: natural prose instead of robotic
  repetition. The **first** subject/possessive reference renders the user's **name** (singular agreement —
  "StoneyEagle **is** awesome"); **every subsequent** reference falls back to neutral they/them/their (plural agreement —
  "**They** play great games"). `{{user.possessive}}` before the name is introduced renders `Name's`.

**Shared tokens (both modes):**
- `{{user.verb:SING|PLUR}}` → picks `SING` vs `PLUR` by current agreement state; author supplies both forms so any
  irregular verb works (`{{user.verb:wins|win}}` → "wins"/"win"). Pipe splits the pair (the one colon separates key from arg).
- `{{user.presenttense}}` → "is"/"are" · `{{user.pasttense}}` → "was"/"were" · `{{user.tense}}` → live-aware copula
  (is/are live, was/were offline; requires `isLive` in context, else token left intact) · `{{user.status}}` → "live"/"offline".
- `{{user.genderedterm}}` → "dude"/"dudette"/"friend" derived from the pronoun (neutral default "friend").
- `{{user.pronouns}}` → the **display badge** combining `PronounId` (+ `AltPronounId`): `subject(primary)/subject(alt)` when an alt is set (`sheher`+`theythem` → "She/They"), `subject` when the primary is singular, `subject/object` otherwise (e.g. "He/Him"), neutral default "they/them" when unset (`PronounId=null`). The badge is **display-only** — it reads both pronoun slots and does **not** advance the alternation state; the grammar helpers above remain primary-only (the alt never affects sentence rendering, per `pronouns.md` D4). `{{target.pronouns}}` mirrors it for the command target.
- `{{user.link}}` → `https://www.twitch.tv/{username}`.
- **Capitalization is the case of the token key:** `{{user.subject}}` → "they", `{{user.Subject}}` → "They" (sentence-start).
- **TTS:** a `usernamePronunciation` override (phonetic spelling) replaces the name in
  `{{user.name}}`/`{{user.username}}`/`{{user.displayname}}` and seeds the smart-alternation name, so spoken output says it right.

**Fallback / GDPR:** unset or scrubbed (`PronounId=null`) ⇒ smart-alternation neutral default (name-then-they). Rendering
never requires having stored a pronoun, so it stays `[PII-S9]`-clean. The same tokens are available under any other
user-bearing namespace the dispatcher resolves (e.g. `target.*` for a shoutout/timeout target).

*Migration:* current single-brace flat tags (`{subject}`, `{verb:a|b}`) → namespaced double-brace (`{{user.subject}}`,
`{{user.verb:a|b}}`); the `he→his`/`he→dude` hardcoded switches are replaced by the R.1 columns + a small neutral-default map.

#### Tokens decided out / dependency-gated (catalog scope boundary)

These are tokens the StreamElements/Nightbot/Fossabot/Wizebot sets popularize. Each has a binding decision below — they
are **not** in the catalog above, and that is final for this subsystem:

- **`{{channel.views}}` is excluded.** Twitch **deprecated** `view_count` on the users endpoint, so no upstream metric
  backs it and our Helix DTOs do not carry it. The token does not ship — there is nothing to render. Introducing a
  substitute metric is a new data-source concern owned by `twitch-helix.md`, not this catalog; until that spec defines a
  replacement field, this catalog has no `{{channel.views}}` token.
- **`{{economy.ranktier}}` / `{{economy.tiername}}` and daily/streak economy tokens depend on the economy subsystem.**
  `economy.md` today models balance, lifetime earned/spent, and numeric leaderboard rank — not a named rank-tier, daily
  bonus, or earn-streak. These tokens are owned by `economy.md`: when it adds tier/streak modeling, the corresponding
  tokens join the `economy.*` table via the same dispatcher-seeds/resolver-renders pattern (no resolver change). This is
  a **dependency on `economy.md`**, not an open question for this spec — the `economy.*` tokens already catalogued
  (`{{economy.balance:user}}`, `{{economy.rank:user}}`, `{{economy.top:N}}`, …) are the complete set this subsystem ships
  against the current economy model.

> **Already backed (in the catalog above):** `{{count.*}}` named counters → `NamedCounters` (schema G.4) + `set_counter`/`adjust_counter` actions (§6.1). `{{random.chatter}}` → Helix Get Chatters (`twitch-helix.md` §3.2, `moderator:read:chatters` progressive scope). `{{bot.prefix}}` storage → `Channels.DefaultCommandPrefix` (schema A.2). Raw date/time format patterns → the `:raw=` sentinel arg (§6.3.1).

Unknown token ⇒ empty string (render-time, non-fatal — only **execution** of unknown action/condition is fail-closed).

### 6.4 `IRegexMatcher` (NET-NEW — `Application/Pipeline/IRegexMatcher.cs`)

The single, shared regex surface for this subsystem: it backs both the `MatchMode=Regex` command trigger (§3.2.1) **and**
the `matches` operator on the §6.3.2 `{{if.*}}` value predicate and the §6.2 `var_compare` condition. **No Wasmtime/Jint
sandbox is involved** — a user-supplied pattern is made ReDoS-safe by **.NET's own regex engine**, not by an external
resource sandbox.

```csharp
public interface IRegexMatcher
{
    // Match `input` against `pattern`. Compiles-and-caches the Regex keyed by pattern. Fail-closed:
    // a RegexMatchTimeoutException returns Result.Success(false) (no-match) and is logged — never hangs.
    Result<bool> IsMatch(string pattern, string input);

    // Save-time validation: rejects an over-length pattern and any construct NonBacktracking cannot compile.
    // Returns Result.Failure("invalid_match_pattern") / Result.Failure("unsupported_regex_construct").
    Result ValidateAndCompile(string pattern);
}
```

**Policy (the impl `RegexMatcher` enforces all of it):**

- **Engine = `RegexOptions.NonBacktracking | RegexOptions.Compiled`.** NonBacktracking is a **linear-time** automaton —
  catastrophic backtracking (ReDoS) is **impossible by construction**, not merely time-limited.
- **Unsupported constructs rejected at save.** NonBacktracking cannot compile backreferences, lookahead, lookbehind, or
  atomic groups — the `Regex` ctor throws for them. `ValidateAndCompile` catches that and returns
  `Result.Failure("unsupported_regex_construct")` (the pattern never reaches the hot path).
- **Pattern length cap ≤ 200 chars**, validated at save (`invalid_match_pattern`) — matches the `MatchPattern string(200)`
  column.
- **Defense-in-depth `matchTimeout`** (`RegexOptions`-independent `Regex.MatchTimeout`, default ~50ms, configurable). On
  `RegexMatchTimeoutException` the matcher returns **no-match + logs** (fail-closed — never hang, never throw to the
  caller). With NonBacktracking this should never fire; it is a belt-and-braces guard only.
- **Compile-and-cache** the `Regex` keyed by pattern (bounded LRU); match only against the **already length-bounded**
  inbound message (the chat message length is capped upstream), so input size is bounded too.

`ValidateAndCompile` is called by `ICommandConfigValidator.ValidateCommand` (§3.6) at save; `IsMatch` is called on the hot
path by the dispatcher (`MatchMode=Regex`), by the `{{if.*}}` `matches` operator (§6.3.2), and by the `var_compare`
`matches` operator (§6.2). One matcher, three surfaces — no duplicate regex policy anywhere.

---

## 7. DI registration

In `NomNomzBot.Application/DependencyInjection.cs` (`AddApplication`) and the Infrastructure profile registrar. Lifetimes
match existing convention (registries singleton, engine/services scoped, actions/conditions transient).

```csharp
// Registries (singleton, startup-populated)
services.AddSingleton<ICommandActionRegistry, CommandActionRegistry>();
services.AddSingleton<IConditionEvaluatorRegistry, ConditionEvaluatorRegistry>();
services.AddSingleton<IBuiltinCommandCatalog, BuiltinCommandCatalog>();

// Engine + compiler + validator
services.AddScoped<IPipelineEngine, PipelineEngine>();
services.AddSingleton<IPipelineCompiler, PipelineCompiler>();       // cache keyed (BroadcasterId,PipelineId,version)
services.AddScoped<ICommandConfigValidator, CommandConfigValidator>();

// Application services
services.AddScoped<ICommandService, CommandService>();
services.AddScoped<ICommandDispatcher, CommandDispatcher>();
services.AddScoped<IPipelineService, PipelineService>();
services.AddScoped<ITimerService, TimerService>();
services.AddScoped<IEventResponseService, EventResponseService>();
services.AddScoped<IBuiltinCommandService, BuiltinCommandService>();
services.AddSingleton<ITemplateEngine, TemplateEngine>();           // VariableResolver-backed
services.AddSingleton<IRegexMatcher, RegexMatcher>();               // NonBacktracking, compile-and-cache (§6.4)

// Cooldown manager — profile adapter (see below)
// Actions (transient) — register every ICommandAction:
services.AddTransient<ICommandAction, SendMessageAction>();
//   …send_reply, set_variable, set_counter, adjust_counter, wait, random_response, stop, timeout, ban,
//     delete_message, shoutout, song_request/skip/current/queue/volume, run_code, http_request
// Conditions (transient):
services.AddTransient<IConditionEvaluator, UserRoleCondition>();
//   …random, var_compare, cooldown
// Built-ins (transient):
services.AddTransient<IBuiltinCommand, FollowageBuiltin>();         // …uptime, shoutout, stats (per-viewer-data.md), etc.

// Timer scheduler
services.AddHostedService<TimerSchedulerService>();                 // PeriodicTimer; guarded by IRunOnceGuard
```

**Deployment-profile adapter variants** (selected by `App__DeploymentMode` per stack §profile axis):

| Abstraction | lite (self-host) | full/SaaS |
|---|---|---|
| `ICooldownManager` | `InMemoryCooldownManager` + `CommandCooldownStates` write-through (single node) | `RedisCooldownManager` + `CommandCooldownStates` (multi-node correct) |
| `IPipelineCompiler` cache | `HybridCache` L1-only | `HybridCache` L1 + Redis L2 (invalidate fan-out) |
| `run_code` executor (`IScriptExecutor`, **owned by sandbox subsystem**) | Jint | Wasmtime x86_64-Cranelift |
| Timer run-once (`IRunOnceGuard`) | no-op | `pg_try_advisory_lock` / `DistributedLock.Postgres` |

---

## 8. Dependencies (from the stack doc)

- **Newtonsoft.Json** — all `[VC:JSON]` columns (`Aliases`, `TemplateResponses`, `ConfigJson`, `Messages`, `MetadataJson`, `StepLogsJson`, `GraphJsonCache`, `OverridesJson`) and pipeline graph (de)serialization (schema §1.4 + conventions). *(Note: the live code uses `System.Text.Json` for pipeline JSON; this subsystem standardizes on Newtonsoft per the binding convention.)*
- **Microsoft.EntityFrameworkCore 10** (+ Npgsql / Sqlite providers, DI-selected) — persistence via `IApplicationDbContext`/`IUnitOfWork`; EF10 named query filters for soft-delete + tenant.
- **Microsoft.Extensions.Caching.Hybrid** — `IPipelineCompiler` + resolution caches (L1 lite, L1+Redis SaaS).
- **StackExchange.Redis 2.13.17** (transitive, SaaS only) — distributed cooldown + compiler-cache invalidation.
- **System.Threading (`PeriodicTimer`, `Channels`, `RateLimiter`)** — in-box; timer scheduler tick, fire-and-forget dispatch, per-tenant side-effecting-import rate limits.
- **Cronos** (MIT) — only if scheduled timers ever need cron expressions (current model is interval-based; not required for I.1).
- **`IScriptExecutor`** (Wasmtime 44.0.0 SaaS / Jint 4.9.2 lite) — **consumed**, not owned; `run_code` calls it.
- In-box `ILogger` + `[LoggerMessage]` + OpenTelemetry — no Serilog. `Result<T>`, `IEventBus` — existing app primitives.

No new 3rd-party dependency is introduced by this subsystem beyond what the stack doc already accepts.

---

## 9. Decisions (resolved)

The three cross-cutting decisions this subsystem locks:

1. **`run_code` boundary.** `IScriptExecutor` and the sandbox (`Wasmtime`/`Jint`) are owned by the sandbox-execution subsystem; this spec defines only the `run_code` action surface and the `CodeScript`/`CodeScriptVersion` reads. Capability-broker enforcement (`ICommandConfigValidator` forbidding tenant/credential/url config) is shared and specified here. This is a **dependency on the sandbox-execution subsystem**, not a deferral — the action surface and broker invariant are fully specified above.
2. **JSON library is Newtonsoft.Json.** The binding convention mandates Newtonsoft.Json, so every `[VC:JSON]` converter uses it, overriding the stack doc's serialization preference for System.Text.Json. The live code's `System.Text.Json` pipeline JSON is migrated to Newtonsoft as part of this rebuild (§8).
3. **Built-in catalog identity is code-defined.** `BuiltinKey` strings (`followage`, `uptime`, `shoutout`, …) are defined by `IBuiltinCommand` implementations, never seeded DB rows. Catalog membership is the concrete set of `IBuiltinCommand` classes registered in DI (§7), authored alongside the implementation — it is not a schema decision and carries no migration.
