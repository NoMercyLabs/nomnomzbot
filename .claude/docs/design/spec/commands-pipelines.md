# Interface Specification — `commands-pipelines` subsystem

**Status:** as-built reference + design intent. Each section says what ships in `server/src` today; text marked **unbuilt** is design intent that no code implements, and its build-or-cut call belongs to the owner (§0 has the per-section status table). Data source of truth: locked DB schema
`.claude/docs/design/2026-06-16-database-schema.md` (G.2, G.2a, G.3, H.1–H.7, I.1, I.2, M.5), execution-model decision
`.claude/docs/design/2026-06-16-custom-command-execution.md`, stack `.claude/docs/design/2026-06-16-stack-and-dependencies.md`,
defaults `.claude/docs/design/2026-06-16-decisions-resolved.md`.

**Namespace:** `NomNomzBot.*`. **.NET 10 / C# 14 / EF Core 10.** File-scoped namespaces, `Nullable`
enabled, async all the way, `Result<T>` (`NomNomzBot.Application.Common.Models`) over exceptions/null. App JSON =
**Newtonsoft.Json** (per schema §1.4 + conventions). Surrogate PKs = `Guid` via `Guid.CreateVersion7()`. Tenant key
`BroadcasterId` is **`Guid`** (FK→`Channels.Id`). Soft-delete via `IsDeleted`+`DeletedAt` global filter.

> **Scope of this subsystem (owns):** authored `Commands` (T1 template / T2 pipeline / T3 code-trigger), built-in
> command enable/disable+override (`ChannelBuiltinCommands`), the normalized pipeline model
> (`Pipelines`/`PipelineSteps`/`PipelineStepConditions`) + execution engine + `ICommandAction` blocks + condition
> evaluators + template variables (`ITemplateResolver`), per-command cooldowns (`ICooldownManager`, in-memory),
> `Timers`, `EventResponses`, and the per-run telemetry (`PipelineExecutions`; `CommandUsage` is an entity with no writer today).
>
> **Out of scope (consumed via existing interfaces, NOT redefined here):** the T3 sandbox itself
> (`IScriptExecutor` / Wasmtime+Jint, owned by the sandbox-execution subsystem — this subsystem only references
> `CodeScripts.CurrentVersionId` from a `run_code` step and calls `IScriptExecutor`), chat send / moderation
> (`IChatProvider`), music (`IMusicService`), tenant resolution (`ICurrentTenantService`), event bus
> (`IEventBus`/`IDomainEventDispatcher`), HTTP egress vault (`HttpEgressAllowlist` rows are owned here as config but
> the `http_request` action's SSRF-hardened client is the sandbox subsystem's), token vault, crypto.

---

## 0. History note and implementation status

**History.** This spec began as the rebuild plan for two parallel pipeline stacks and `int`-keyed entities. That rebuild landed: one action/condition contract (`ICommandAction` / `ICommandCondition`, `Application/Abstractions/Pipeline`), `Guid` keys, the normalized `Pipeline` / `PipelineStep` / `PipelineStepCondition` model, and a fail-closed engine (an unknown action or condition aborts the run; an action exception stops it). The old per-row "existing → action" table is retired. Later work added the tree model (`pipeline-control-flow.md`, `pipeline-tree-and-editor.md`), deferred execution (§10) and the built-in reply catalogue (§11).

### Implementation status (checked 2026-09-30)

| § | Subject | Status |
|---|---|---|
| 1 | Entities | Built. Column deltas and the extra entities are listed in §1. `CommandCooldownState` (G.3) and `CommandUsage` (M.5) exist but no code writes them. |
| 2 | Domain events | Partly built: `CommandExecutedEvent`, `CommandFailedEvent` (plus `VoiceTriggerFiredEvent`). Every other event is **unbuilt**. |
| 3.1 | `ICommandService` | Built; different signatures. |
| 3.2 | Command dispatch | Built as `ChatMessageHandler`; `ICommandDispatcher` does not exist. |
| 3.3 | `IPipelineEngine` | Built; different request/outcome shape plus the resume methods. |
| 3.4 | `IPipelineService` | Built; no `ValidateAsync` (validation is `POST pipelines/validate` → `ICommandConfigValidator`); adds `GetBlastRadiusAsync`. |
| 3.5 | `IPipelineCompiler` | **Unbuilt.** |
| 3.6 | `ICommandConfigValidator` | Built in a smaller shape; the tainted-payload guard and trigger validation are **unbuilt**. |
| 3.7 | Timers | Built as `ITimerManagementService` + the `TimerService` background worker. |
| 3.8 | `IEventResponseService` | Built; no `TriggerAsync` (runtime is `IEventResponseExecutor`). |
| 3.9 | `IBuiltinCommandService` | Built; different shape. |
| 3.10 | `IBuiltinCommandCatalog` / `IBuiltinCommand` | Built. |
| 3.11 | `ICooldownManager` | Built; synchronous and in-memory, string-keyed. |
| 3.12 | Registries | **Unbuilt**; actions and conditions are DI-scanned. |
| 3.13 | Action / condition contract | Built as `ICommandAction` / `ICommandCondition`. |
| 3.13a | Tier authoring caps | Wired for `custom_commands` through `LimitedResourceRegistry`; the other keys were not re-checked. |
| 4 | DTOs | Built; the shapes differ (§4). |
| 5 | Endpoints | Built; §5 lists the as-built routes. |
| 6.1 | Actions | Built except `random_response`, `http_request`, `send_whisper`. Catalogue: `GET channels/{channelId}/pipelines/actions`. |
| 6.2 | Conditions | `user_role`, `random`, `comparison` built; `var_compare`, `cooldown` **unbuilt**. |
| 6.3 | Template variables | Built as `ITemplateResolver`; the placeholder grammar is single-brace `{name}`. The `{{ }}` grammar, `if.*` helper and duration/instant formatter are **unbuilt** (the grammar decision is the owner's). |
| 6.4 | Regex matching | `IRegexMatcher` **unbuilt**; `MatchMode=Regex` uses a plain `Regex` with a 100 ms timeout. |
| 7 | DI | As-built listing in §7. |
| 10 | Deferred execution | Built. |
| 11 | Built-in reply catalogue | Built. |

---

## 1. Entities (locked schema — owned by this subsystem)

All defined in `.claude/docs/design/2026-06-16-database-schema.md`. **Do not redefine columns**; this lists ownership, the
EF entity class (`NomNomzBot.Domain/Commands/Entities/`), key fields, and the converter/enum flags. The as-built column deltas follow the table. Every row carries
`BroadcasterId : Guid` (FK→`Channels.Id`, `ITenantScoped`) unless noted. `[VC:JSON]` = `ValueConverter`+`ValueComparer`
over Newtonsoft.Json; `[VC:enum]` = enum↔text converter. `ConfigSchemaVersion : int` (default 1) on every
app-interpreted-JSON config table is the per-row upcast anchor.

| # | Entity (`Domain/Commands/Entities/*.cs`) | Base | PK | Key fields / types |
|---|---|---|---|---|
| **G.2** | `Command` | `SoftDeletableEntity` | `Id : Guid` | `BroadcasterId:Guid`; `Name:string(100)`; `NameNormalized:string(100)` (unique `(BroadcasterId,NameNormalized)`); `PrefixMode:string(20)`[VC:enum] `Default`\|`Custom`\|`None`; `CustomPrefix:string(8)?` (when `Custom`); `MatchMode:string(20)`[VC:enum] `StartsWith`\|`Exact`\|`Contains`\|`Regex` (default `StartsWith`); `MatchPattern:string(200)?` (required when `MatchMode=Regex`, else null); `Tier:string(20)`[VC:enum] `template`\|`pipeline`\|`code`; `Description:string(500)?`; `Aliases:List<string>`[VC:JSON]; `TemplateResponse:string(2000)?`; `TemplateResponses:List<string>?`[VC:JSON]; `ConfigSchemaVersion:int`; `PipelineId:Guid?` (FK→Pipelines); `MinPermissionLevel:int` (persisted ladder value; the DTOs expose the rung name, §4.1); `CooldownSeconds:int`; `UserCooldownSeconds:int` (0=off); `CooldownPerUser:bool`; `IsEnabled:bool`; `IsPlatform:bool`; `UseCount:long`; `LastUsedAt:DateTime?` |
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

**As-built column deltas and extra entities.** The key widening (`Guid` ids, `Guid` `BroadcasterId`) is done. Where the code differs from the table above:

- **`Command` (G.2)** adds `PresetKey string?` (the fun-command preset the row came from; `POST commands/{commandName}/reset-to-preset` restores it).
- **`ChannelBuiltinCommand` (G.2a)** adds the platform-source columns `PlatformSourceDefinitionId`, `PlatformSourceVersion`, `PlatformSourceHash`, `PlatformSourceSyncedAt`. `OverridesJson` is a plain `string?` blob, not a typed POCO — its shape is in §11.
- **`CommandCooldownState` (G.3)** has an entity, a configuration and a `DbSet`, but nothing reads or writes it: cooldowns live in the in-memory `CooldownManager` (§3.11).
- **`Pipeline` (H.1)** adds `ParameterNamesJson`, the `Triggers` collection (`PipelineTrigger`) and the platform-source columns; `TriggerKind` defaults to `manual` and `MaxStepCount` to 50.
- **`PipelineStep` (H.2)** adds `BlockKind`, `BlockConfigJson` and `ContinueOnError`; `ConfigJson` is a JSON `string`.
- **`PipelineStepCondition` (H.3)** adds `ParentConditionId` and `GroupOp` (the condition tree). The as-built `ConditionType` values are `user_role`, `random`, `comparison` (§6.2).
- **`PipelineExecution` (H.4)** has a nullable `PipelineId`; `StepLogsJson` is a `string`.
- **`Timer` (I.1)** adds `FireOnce` and the platform-source columns. **`EventResponse` (I.2)** adds `SpeakWithTts`, `FollowsPlatformDefault` and the platform-source columns.
- **`CommandUsage` (M.5)** has an entity and a `DbSet`, is read by the analytics services, and has **no writer**; the command's `UseCount` / `LastUsedAt` are folded by `CommandUseCountHandler` from `CommandExecutedEvent` instead.
- **Entities not in the table:** `ChatTrigger` (pattern triggers: `Pattern`, `MatchType` `contains|exact|starts_with|regex`, `CaseSensitive`, `Response` or `PipelineId`, `CooldownSeconds`, `MinPermissionLevel`); `VoiceTrigger` and `VoiceTranscriptSegment` (spoken-word triggers); `PipelineTrigger` and `PipelineRunState` (`pipeline-tree-and-editor.md`); `ScheduledPipelineTask` (§10); `PlatformBuiltinReplyDefault` (`BuiltinKey`, `Slot`, `Template`) and `PlatformEventResponseDefault` (`EventType`, `IsEnabled`, `Message`, `SpeakWithTts`) — the platform-admin defaults behind §11 and §3.8.

---

## 2. Domain events

**As-built:** only `CommandExecutedEvent` and `CommandFailedEvent` exist (in `Domain/Commands/Events/`, plus `VoiceTriggerFiredEvent`). `BeforeCommandExecutedEvent`, `AfterCommandExecutedEvent` and every net-new event below are **unbuilt**; the engine records runs in `PipelineExecution` rows instead. The rest of this section is the design intent.

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
`NomNomzBot.Infrastructure/Services/...` unless noted). **As-built:** the command/pipeline/timer/event-response services take `string broadcasterId` (parsed to a `Guid` inside; a bad id fails `VALIDATION_FAILED`), and each subsection gives the shipped signatures. `Guid broadcasterId` is the design intent. Repositories via
`IUnitOfWork` + `IApplicationDbContext`; never raw `DbContext` in controllers.

### 3.1 `ICommandService` (as-built — `Application/Commands/Services/ICommandService.cs`)

Management CRUD for authored commands, keyed by command **name**. Runtime resolution is not on this interface: the chat handler resolves triggers from the in-memory `ChannelRegistry` cache (§3.2).

```csharp
Task<Result<CommandDto>> CreateAsync(string broadcasterId, CreateCommandDto request, CancellationToken ct = default);
Task<Result<CommandDto>> UpdateAsync(string broadcasterId, string commandName, UpdateCommandDto request, CancellationToken ct = default);
Task<Result>             DeleteAsync(string broadcasterId, string commandName, string? actorId = null, CancellationToken ct = default);
Task<Result<CommandDto>> GetAsync(string broadcasterId, string commandName, CancellationToken ct = default);
Task<Result<PagedList<CommandListItem>>> ListAsync(string broadcasterId, PaginationParams pagination, CancellationToken ct = default);
Task<Result<string>>     ExecuteAsync(string broadcasterId, string commandName, string userId, string? input = null, CancellationToken ct = default);
```
- `CreateAsync` — normalizes and validates the name and prefix, validates template responses and template helpers, enforces name uniqueness on `NameNormalized`, checks the `custom_commands` quota and the response-variation quota through the limit registry, and validates the pipeline binding, then persists the `Command` and invalidates the channel's registry cache.
- `UpdateAsync` — partial update; re-validates a rename, the templates and the quotas; invalidates the registry cache.
- `DeleteAsync` — writes an audit `Record` (`command_deleted`, actor = `actorId`, else the broadcaster) **before** the soft delete, then invalidates the registry cache and publishes a config-changed event. It does **not** touch cooldown state (cooldowns are in-memory, §3.11). No entity holds a foreign key to `Command.Id`, so nothing else needs clearing.
- `ExecuteAsync` — runs an enabled command by name as `userId` outside chat (a `pipeline` or `code` command dispatches through its bound pipeline). Fails `NOT_FOUND` for an unknown or disabled command.

**Design intent, unbuilt:** `ListAsync`/`GetAsync` etc. over `Guid broadcasterId`, and `ResolveAsync(Guid broadcasterId, string trigger)` returning a `CommandResolution` (§4.2).

### 3.2 Command dispatch — as-built `ChatMessageHandler` (`Infrastructure/Chat/EventHandlers/ChatMessageHandler.cs`)

`ICommandDispatcher` and `CommandDispatchRequest` / `CommandDispatchResult` do **not** exist. The single runtime entry is `ChatMessageHandler`, an `IEventHandler<ChatMessageReceivedEvent>` on the hot path for every chat message. Its order of checks:

1. Read the channel prefix (`ChannelContext.CommandPrefix`, default `!`) from the in-memory `ChannelRegistry`; resolve an authored command from the registry cache (`ResolveAuthoredCommand`, §3.2.1), else a built-in from `IBuiltinCommandCatalog`. No database hit on this path.
2. Check the caller's effective rung against the command's `MinPermissionLevel`; a refusal sends a system notice (`system/permissiondenied`).
3. Check global and per-user cooldowns through `ICooldownManager` (§3.11); a hit sends a cooldown notice.
4. Execute: a `template` command renders through `ITemplateResolver` and sends the reply; a `pipeline` or `code` command builds a `PipelineRequest` and calls `IPipelineEngine.ExecuteAsync`; a built-in calls `IBuiltinCommand.ExecuteAsync` and sends the reply through `IBuiltinResponseComposer` (§11).
5. Set the cooldown and publish `CommandExecutedEvent` or `CommandFailedEvent`. `CommandUseCountHandler` folds a success into `Command.UseCount` / `LastUsedAt`.

The same handler also fires chat triggers (`ChatTrigger`), sound triggers, passive clip links and poll votes for a message. **Design intent, unbuilt:** the `ICommandDispatcher.DispatchAsync` contract with a typed result, the `CommandCooldownBlockedEvent`, and `CommandUsage` appends.

#### 3.2.1 Prefix + match resolution (how an inbound message becomes a command hit)

**As-built:** implemented in `ChatMessageHandler.ResolveAuthoredCommand` over the `ChannelRegistry` cache. `PrefixMode` (`Default`/`Custom`/`None`) and `MatchMode` (`StartsWith`/`Exact`/`Contains`/`Regex`) both work. The prefix comes from `Channel.CommandPrefix`. A `Regex` pattern is compiled once per cache load as a plain `Regex` (`IgnoreCase`, 100 ms match timeout) — not `NonBacktracking` (§6.4); an invalid pattern is skipped with a warning. The reserved data-rights precedence in step 3 is not re-verified here.

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

### 3.3 `IPipelineEngine` (as-built — `Application/Abstractions/Pipeline/IPipelineEngine.cs`)

Executes a normalized pipeline. Implemented by `PipelineEngine` (`Infrastructure/Platform/Pipeline/PipelineEngine.cs`), registered scoped.

```csharp
Task<PipelineExecutionResult> ExecuteAsync(PipelineRequest request, CancellationToken ct = default);
Task                          CancelAllForChannelAsync(Guid broadcasterId);
int                           GetActiveCountForChannel(Guid broadcasterId);
Task<Result<string?>>         RunInlineSubPipelineAsync(PipelineExecutionContext callerCtx, Guid targetPipelineId,
                                  IReadOnlyList<string>? args, IReadOnlyDictionary<string, string>? namedArgs = null, CancellationToken ct = default);
Task<PipelineExecutionResult> ResumeAsync(Guid runStateId, CancellationToken ct = default);
Task<int>                     ResumeSuspendedRunsForEventAsync(Guid broadcasterId, string eventName,
                                  IReadOnlyDictionary<string, string> eventData, CancellationToken ct = default);
Task<int>                     ResumeTimedOutWaitsAsync(CancellationToken ct = default);
```
- `ExecuteAsync` — loads the pipeline, seeds variables and runs the step tree **fail-closed**: an unknown `ActionType` or `ConditionType` aborts the run; an action exception or a failed result stops it (`Failed`); a step that opted in with `ContinueOnError` survives a failed result, and a `try` block catches a failure inside its body. Enforces the global and per-channel concurrency gates, the total-action, iteration, recursion-depth and runtime caps, and persists one `PipelineExecution` row with bounded step logs. The tree walk (block kinds, `if`/`switch`/`loop`/`random_branch`/`try`/`detached_step`, suspend and resume) is owned by `pipeline-control-flow.md` and `pipeline-tree-and-editor.md`.
- The three resume methods back `wait_for_event` (`pipeline-tree-and-editor.md` §2.3): `ResumeAsync` continues one persisted run, `ResumeSuspendedRunsForEventAsync` wakes every run parked on an equal event name (`EventResponseExecutor` calls it), `ResumeTimedOutWaitsAsync` is driven by `WaitForEventTimeoutSweepWorker`.
- `CancelAllForChannelAsync` — cancels every active run for the tenant (stream-offline path), best-effort, and cancels suspended waits.

`PipelineRequest` (same file) — `BroadcasterId:Guid`; `PipelineId:Guid?`; `PipelineJson:string` (inline JSON, default `"{}"`; used when no `PipelineId`); `TriggeredByUserId:string`; `TriggeredByDisplayName:string`; `MessageId:string?`; `RedemptionId:string?`; `RewardId:string?`; `ChannelEventId:string?`; `RawMessage:string`; `InitialVariables:Dictionary<string,string>`. There is no `TriggerKind`, `Args`, `TaintedVariables`, `EventType` or `JournalEventId` field.

`PipelineExecutionResult` — `ExecutionId:string`; `Outcome:PipelineOutcome`; `Duration:TimeSpan`; `StepsExecuted:int`; `StepsSkipped:int`; `Total:int`; `ErrorMessage:string?`; `StepLogs:IReadOnlyList<StepExecutionLog>`; plus the suspend fields `SuspendedAtStepId`, `SuspendCursorJson`, `SuspendedRunStateId`, `SuspendWaitEventName`, `SuspendWaitTimeoutSeconds`. There is no `HostCallCount`.

`PipelineOutcome` — `Completed`, `Stopped`, `Failed`, `PartiallyFailed`, `TimedOut`, `Cancelled`, `AbortedBudget`, `Suspended`. (There is no `Denied`.)

**Design intent, unbuilt.** The non-user trigger contract below (a `WebhookSystemActor.UserId = Guid.Empty` sentinel) has no constant or engine branch in the tree.

> **Non-user (system-actor) triggers — binding contract.** Not every trigger has a Twitch user: a `TriggerKind=webhook`
> run (owner `webhooks.md` §3.2.2) and a `TriggerKind=manual`/system run (both shipped enum values, H.1) have **no**
> `Users` row, and the same contract binds every system-actor trigger kind. The reserved
> sentinel **`WebhookSystemActor.UserId = Guid.Empty`** is passed as `TriggeredByUserId` for these; `TriggeredByDisplayName`
> carries the wire-source label (e.g. the provider name). The engine treats `TriggeredByUserId == Guid.Empty` as a
> **system trigger**: it is **never** dereferenced as a `Users` FK and **skips per-user permission/cooldown gates**
> (there is no user to gate), while **global + per-channel concurrency admission still applies**. Define the constant
> once (`NomNomzBot.Domain.Constants.WebhookSystemActor`); both this engine and the webhook dispatcher use it.
### 3.4 `IPipelineService` (as-built — `Application/Commands/Services/IPipelineService.cs`)

Management CRUD for the normalized pipeline model. `string broadcasterId`; `Guid id`.

```csharp
Task<Result<PagedList<PipelineListItemDto>>> ListAsync(string broadcasterId, PaginationParams pagination, CancellationToken ct = default);
Task<Result<PipelineDto>>                    GetAsync(string broadcasterId, Guid id, CancellationToken ct = default);
Task<Result<PipelineDto>>                    CreateAsync(string broadcasterId, CreatePipelineDto request, CancellationToken ct = default);
Task<Result<PipelineDto>>                    UpdateAsync(string broadcasterId, Guid id, UpdatePipelineDto request, CancellationToken ct = default);
Task<Result>                                 DeleteAsync(string broadcasterId, Guid id, CancellationToken ct = default);
Task<Result<PipelineBlastRadiusDto>>         GetBlastRadiusAsync(string broadcasterId, Guid id, CancellationToken ct = default);
```
- `Create/UpdateAsync` — accept the editor graph (`graph` JSON), validate through `ICommandConfigValidator`, normalize into `Pipeline` + `PipelineStep` + `PipelineStepCondition` rows, refresh `GraphJsonCache`, publish a config-changed event and invalidate the bound caches.
- `GetBlastRadiusAsync` — the **counted** dependents of a pipeline right now: the commands, chat triggers, timers and event responses whose `PipelineId` points at it (`PipelineBlastRadiusDto`: a count and the names for each, plus `TotalReferences`). The dashboard calls it and shows the result before a delete (`GET pipelines/{id}/blast-radius`).
- `DeleteAsync` — a **soft** delete that does **not** fail when the pipeline is in use. Because a soft delete never fires the database's `ON DELETE SET NULL`, it first clears `PipelineId` on every referencing `Command`, `ChatTrigger`, `Timer` and `EventResponse` explicitly, so the blast radius the preview promised is the blast radius that happens; then it removes the pipeline, publishes a config-changed event and invalidates the caches. (The design's `pipeline_in_use` rejection was not built.)
- There is no `ValidateAsync` on the service: the editor's dry validation is `POST pipelines/validate` → `ICommandConfigValidator.ValidatePipelineAsync` (§3.6), and a dry run of a saved pipeline is `POST pipelines/{id}/test-run` (`IPipelineTestRunService`).

### 3.5 `IPipelineCompiler` — unbuilt

No `IPipelineCompiler`, `CompiledPipeline`, `CompiledStep` or `CompiledCondition` exist. `PipelineEngine` loads the persisted `PipelineStep` rows (or, for a flat command pipeline, the cached graph JSON that `ChannelRegistry` builds through `PipelineGraphBuilder`) and resolves each action and condition from the DI-scanned `IEnumerable<ICommandAction>` / `IEnumerable<ICommandCondition>` at run time. Hot reload comes from `IChannelRegistry` cache invalidation on save, not a compiler cache.

### 3.6 `ICommandConfigValidator` (as-built — `Application/Abstractions/Pipeline/ICommandConfigValidator.cs`)

Save-time, fail-closed validator; the capability-broker invariant lives here. Implemented by `CommandConfigValidator` (`Infrastructure/Platform/Pipeline/`), registered scoped.

```csharp
Task<Result<PipelineValidationResult>> ValidatePipelineAsync(PipelineGraphInput graph, CancellationToken ct = default);
Result<PipelineValidationResult>       ValidateAction(ActionDefinition action);
ActionDefinition                       NormalizeResourceIdFields(ActionDefinition action);
```
`PipelineValidationResult` carries `IsValid`, `ErrorCode`, `ErrorMessage`. `PipelineGraphInput(IReadOnlyList<PipelineStepInput> Steps)`, `PipelineStepInput(ActionType, Config, ConditionType?, ConditionParams?, IsEnabled)`.

- Rejects with `MISSING_ACTION_TYPE`, `UNKNOWN_ACTION_TYPE` (checked against the DI-scanned actions), `STEP_COUNT_EXCEEDED` (more than 100 steps), `BANNED_CONFIG_KEY` (a config key named `url`, `secret`, `webhook_url`, `api_key`, `token`, `password`, `credential`, `authorization` or `bearer`), `URL_IN_CONFIG` (a value that looks like a URL or credential), `UNKNOWN_TEMPLATE_HELPER` (a field the action marks templated names a helper the pipeline registry does not know) and `INVALID_RESOURCE_ID` (a resource-picker field that is neither a ULID nor a Guid).
- `NormalizeResourceIdFields` rewrites a picker's ULID wire form to the owned `Guid` before persist.

**Unbuilt (design intent):** `ValidateCommand` (trigger validation: `PrefixMode`/`CustomPrefix` consistency, `MatchPattern` required and length-capped for `Regex`, `invalid_custom_prefix`, `invalid_match_pattern`, `unsupported_regex_construct`) — the trigger fields are stored as sent and an invalid regex is only skipped, with a warning, when the registry loads; and the **tainted-payload guard** for webhook-triggered pipelines (`tainted_payload_in_sensitive_param`) — no `TaintedVariables` bag exists. There is no architecture test for the no-tenant/credential/url-config invariant; the validator enforces it at save.

**Design-intent text (unbuilt parts only, kept for the build-or-cut call):**

- **Tainted-payload guard (webhook triggers — `webhooks.md` §7.1).** When validating a pipeline whose `TriggerKind=webhook`, `ValidatePipeline` fails closed (`tainted_payload_in_sensitive_param`) if any security-sensitive action parameter binds a `{{payload.*}}` token: `ban`/`timeout` `UserRef`, `shoutout` `TargetChannel`, `http_request` `Fqdn`/`Path`/`Method`, `send_webhook` `OutboundWebhookEndpointId`. `payload.*` is attacker-authored; it may feed display sinks but never the target of a moderation/egress action.
- **Command trigger validation.** `PrefixMode=Custom` requires a non-empty `CustomPrefix` (≤8 chars); `PrefixMode∈{Default,None}` requires `CustomPrefix` null/empty. `MatchMode` must be one of `StartsWith`/`Exact`/`Contains`/`Regex`; for `Regex`, `MatchPattern` is required (non-empty, ≤200 chars) and must compile; for any other mode it must be null/empty.

### 3.7 `ITimerManagementService` + `TimerService` (as-built)

Timers are split in two. **CRUD** is `ITimerManagementService` (`Application/Commands/Services/ITimerManagementService.cs`, scoped, convention-registered); **scheduling** is `TimerService` (`Infrastructure/Commands/Jobs/TimerService.cs`), a `BackgroundService` — there is no `ITimerService` and no `FireDueAsync`.

```csharp
Task<Result<PagedList<TimerListItem>>> ListAsync(string broadcasterId, PaginationParams pagination, CancellationToken ct = default);
Task<Result<TimerDto>>                 GetAsync(string broadcasterId, Guid id, CancellationToken ct = default);
Task<Result<TimerDto>>                 CreateAsync(string broadcasterId, CreateTimerDto request, CancellationToken ct = default);
Task<Result<TimerDto>>                 UpdateAsync(string broadcasterId, Guid id, UpdateTimerDto request, CancellationToken ct = default);
Task<Result>                           DeleteAsync(string broadcasterId, Guid id, CancellationToken ct = default);
Task<Result<TimerDto>>                 ToggleAsync(string broadcasterId, Guid id, CancellationToken ct = default);
```
- `TimerService` ticks every 30 seconds under an `IRunOnceGuard` lease (so two instances do not double-fire). For each enabled due timer — `IntervalMinutes` elapsed **and** enough new chat messages since its last fire (`MinChatActivity`, tracked in memory per timer) — it sends `Messages[NextMessageIndex]` (rendered through `ITemplateResolver`) or, when `PipelineId` is set, dispatches that pipeline with the message as `{timer.message}`. It advances `NextMessageIndex` (rotating), sets `LastFiredAt`, and persists both. `FireOnce` timers disable after their first fire.
- `TimerFiredEvent` does not exist (§2).

### 3.8 `IEventResponseService` (as-built — `Application/Commands/Services/IEventResponseService.cs`)

CRUD for per-event reactions (I.2). `string broadcasterId`.

```csharp
Task<Result<PagedList<EventResponseListItem>>> ListAsync(string broadcasterId, PaginationParams pagination, CancellationToken ct = default);
Task<Result<EventResponseDto>>                 GetByEventTypeAsync(string broadcasterId, string eventType, CancellationToken ct = default);
Task<Result<EventResponseDto>>                 UpsertAsync(string broadcasterId, string eventType, UpdateEventResponseDto request, CancellationToken ct = default);
Task<Result>                                   ResetToDefaultAsync(string broadcasterId, string eventType, CancellationToken ct = default);
```
- `UpsertAsync` — when `ResponseType="pipeline"` requires a valid `PipelineId` (else `Result.Failure`); validates `MetadataJson` shape. Creates-or-updates one row per `(BroadcasterId, EventType)`.
- `ResetToDefaultAsync` — resets one row's fields back to its seeded default (disabled, `chat_message`, no message/pipeline/metadata) **in place**. Never removes the row — see the settled model below. Backend route: `POST .../event-responses/{eventType}/reset` (204).
- **Runtime trigger is `IEventResponseExecutor`** (`Application/Commands/Services/IEventResponseExecutor.cs`), not a `TriggerAsync` on this service: `ExecuteAsync(broadcasterId, eventTypeKey, userId, userDisplayName, variables)` looks up the enabled response for the inbound event and runs it (chat message, overlay and/or bound pipeline, plus TTS when `SpeakWithTts`), and also wakes pipelines parked on `wait_for_event` (§3.3). `ReplayAsync(...)` re-runs the **full** response — chat, TTS and overlay, including gift-bomb chains — and returns an `EventResponseOutcome`. `EventResponseTriggeredEvent` does not exist.

**Settled model (S-EVENTRESPONSE-NO-CREATE, 2026-08-28) — a seeded catalogue, not a user-authored list.**
An `EventResponse` row is a **fixed, seeded catalogue entry**, one per known platform event type
(`EventResponsePresetCatalog`), never user-created and never user-deletable:
- `EventResponseDefaultsSeeder` seeds one disabled row per catalog event type for every channel (full-startup
  `ISeeder` pass + a scoped call on onboarding), so a channel's row count always equals the catalog size.
- The dashboard's only operations on a row are **toggle enabled/disabled**, **edit** (response type, message,
  pipeline binding, overlay/widget target), and **reset to default** — there is no create affordance and no
  destructive delete. "Reset" is an honest label: it resets the row's fields in place and the row remains
  present and enumerable; it is never a removal.
- `EventResponse` carries **no `[CountedResource]`** and `event_responses` has **no entry** in
  `LimitedResourceRegistry` — a per-channel cap on a resource the operator can never create more of than the
  catalog's fixed size would be decorative, unenforceable state (the truthful-data house rule).

**Tone-aware defaults (settled 2026-09-30).** A row that follows the platform default speaks in the channel's
personality tone (`Channel.Personality`, five tones, Informative the default). A row with its own text never
changes when the tone changes.
- **Precedence** for a row with `FollowsPlatformDefault = true`, the same order as built-in replies (§11): the
  platform admin's text (`PlatformEventResponseDefault.Message` when not null), then one random line from
  `EventResponseToneCatalog` for `(eventType, tone)` with an Informative fallback, then nothing.
- **`EventResponseToneCatalog`** (`Application/Commands/Services/`, beside `EventResponsePresetCatalog`) is
  code-defined content in the `ToneTemplateCatalog` shape: for each event type that ships ON (the nine
  `PlatformEventResponseDefaultsSeeder.EnabledMessages` keys), all five tones with 1–4 lines each. Every line
  of an event uses only the placeholders of that event's Informative lines — a test enforces it, so a tone
  never breaks `{user}`/`{count}`/`{also_said}`. Lines are English chat content, like the built-in catalogue.
- **`PlatformEventResponseDefault.Message` null means "no admin text — use the tone catalogue".** The seeder no
  longer writes `Message`, and on every start it nulls a row whose `Message` still equals the legacy seeded
  line, so untouched platform rows become tone-aware. An admin edit sets `Message` and then wins for every
  tone; the admin's restore sets it back to null.
- **Truthful dashboard.** `EventResponseDto` gains `IReadOnlyList<string> ToneLines`: for a following row, the
  lines the bot picks from (the admin text alone when set, else the catalogue lines for the channel's tone);
  empty for a row with its own text. A following row's `Message` is the first of those lines. The editor says
  the bot picks one of these lines in the channel's tone and lists them. The first edit of a following row
  (`AdoptPlatformDefaultAsync`) copies that first line into `Message`.
- **Consequence shown before saving.** Changing the personality tone names how many event responses follow the
  default (they change voice) and how many keep their own text (unchanged).
- Rejected: generating lines with an LLM at send time — bursts (raids, gift bombs) add latency and cost, the
  streamer cannot preview it, and a model's joke about a viewer's name is a moderation risk.

### 3.9 `IBuiltinCommandService` (as-built — `Application/Commands/Services/IBuiltinCommandService.cs`)

Owns per-channel enable/disable and overrides of the code-defined built-ins (G.2a). Built-ins are catalogue-defined, never per-channel-seeded rows, so a fresh channel always lists them (this closed the "commands show 0 / seeding skipped" issue).

```csharp
Task<Result<IReadOnlyList<BuiltinCommandDto>>> ListAsync(string broadcasterId, CancellationToken ct = default);
Task<Result<BuiltinCommandDto>>                GetAsync(string broadcasterId, string builtinKey, CancellationToken ct = default);
Task<Result>                                   SetEnabledAsync(string broadcasterId, string builtinKey, bool enabled, CancellationToken ct = default);
Task<Result>                                   SetSpeakWithTtsAsync(string broadcasterId, string builtinKey, bool enabled, CancellationToken ct = default);
Task<Result<BuiltinCommandDto>>                UpdateSettingsAsync(string broadcasterId, string builtinKey, BuiltinSettingsUpdate update, CancellationToken ct = default);
Task<Result<BuiltinCommandDto>>                ResetAsync(string broadcasterId, string builtinKey, CancellationToken ct = default);

sealed record BuiltinSettingsUpdate(int? CooldownSeconds, string? MinPermissionLevel);   // MinPermissionLevel is a rung name
sealed record BuiltinCommandDto(string BuiltinKey, string Name, bool IsEnabled, int DefaultCooldownSeconds,
    string DefaultMinPermissionLevel, string ReplyGroup, bool SpeakWithTts, bool IsReserved,
    int? CooldownSecondsOverride, string? MinPermissionLevelOverride, int ReplyOverrideCount);
```
- `SetEnabledAsync` upserts the `ChannelBuiltinCommand` toggle row (an absent row means enabled with the catalogue defaults). `SetSpeakWithTtsAsync` and `UpdateSettingsAsync` write into the row's `OverridesJson` blob, merged so setting one field never drops another (§11). `ResetAsync` clears the cooldown and permission overrides back to the catalogue defaults.
- There is no `ResolveAsync`, no `BuiltinResolution` and no `BuiltinCommandToggledEvent`; the chat handler reads the merged state from the channel registry cache. Reply texts are edited through `IBuiltinReplyService` (§11).

### 3.10 `IBuiltinCommandCatalog` + `IBuiltinCommand` (as-built — `Application/Commands/Builtin/`)

Static registry of code-defined built-ins (`followage`, `uptime`, `shoutout`, `stats`, …). One class per built-in
(`Infrastructure/Commands/Builtins/`); no DB seed rows.

**`stats` (alias `profile`) built-in** (`StatsBuiltin`, `BuiltinKey="stats"`, owned by `per-viewer-data.md`) — renders the
caller's (or `@target`'s) headline stats (messages, watch-time, points + rank, streak, first-seen) by composing
`IViewerAnalyticsService.GetProfileAsync` + `ICurrencyAccountService` + `IEconomyLeaderboardService` (no new projection —
parity with the legacy `Stats` command). Its output text is customizable per slot through the reply catalogue (§11), like
every other built-in.

```csharp
// Application/Commands/Builtin/IBuiltinCommand.cs
public interface IBuiltinCommand
{
    string BuiltinKey { get; }                       // e.g. "followage"
    int DefaultCooldownSeconds { get; }
    int DefaultMinPermissionLevel { get; }           // the catalogue default; DTOs expose the rung name
    bool IsReserved => false;                        // reserved data-rights built-ins cannot be overridden (§11)
    Task<Result<string>> ExecuteAsync(BuiltinCommandContext context, CancellationToken ct = default);
}

public interface IBuiltinCommandCatalog
{
    IReadOnlyCollection<IBuiltinCommand> GetAll();
    IBuiltinCommand? Get(string builtinKey);
}

// BuiltinCommandContext (class): BroadcasterId (Guid), TriggeringUserId (string), TriggeringUserDisplayName, TriggeringUserLogin,
// MessageId, RoleLevel (int), Args (string), ReplyParentMessageBody?, ReplyParentUserName?, Personality (tone), SpeakWithTts, CancellationToken.
```
Each built-in is a class registered scoped in `DependencyInjection` (one `AddScoped<IBuiltinCommand, …>` line each). The design's `ChannelSnapshot` / `StreamSnapshot` / `ITemplateEngine` context members were not built; a built-in reads what it needs from injected services and composes its reply through `IBuiltinResponseComposer` (§11).

### 3.11 `ICooldownManager` (as-built — `Application/Abstractions/RateLimiting/ICooldownManager.cs`)

Synchronous and **in-memory**: the singleton `CooldownManager(TimeProvider)` keeps cooldown timestamps in process memory, keyed by channel id string and command name. Cooldowns therefore do not survive a restart and are not shared across instances.

```csharp
bool      IsOnCooldown(string channelId, string commandName, bool isExemptFromCooldown, string? userId = null);
TimeSpan? GetRemainingCooldown(string channelId, string commandName, string? userId = null);
void      SetCooldown(string channelId, string commandName, TimeSpan duration, string? userId = null);
void      ClearCooldown(string channelId, string commandName, string? userId = null);
void      ClearAllCooldowns(string channelId);
```
The `CommandCooldownState` table (G.3) is **unused** — nothing reads or writes it. **Design intent, unbuilt:** an async `Guid commandId` interface written through to that table, and a Redis-backed variant for multi-node.

### 3.12 Registries — unbuilt

`ICommandActionRegistry` and `IConditionEvaluatorRegistry` do not exist. Actions and conditions are multi-bound in DI by an assembly scan (`AddImplementationsOf<ICommandAction>` and `AddImplementationsOf<ICommandCondition>`, transient) and consumed as `IEnumerable<ICommandAction>` / `IEnumerable<ICommandCondition>` by `PipelineEngine`, `CommandConfigValidator` and `PipelinesController`. Dropping a class in is enough to register it.

### 3.13 `ICommandAction` / `ICommandCondition` (as-built — `Application/Abstractions/Pipeline/`)

The single consolidated contract. `ICommandAction` is self-describing for the editor.

```csharp
public interface ICommandAction
{
    string ActionType { get; }                       // snake_case registry key, matches PipelineStep.ActionType
    LocalizedText Category { get; }                  // editor grouping
    LocalizedText Description { get; }               // editor copy
    IReadOnlyList<PipelineActionFieldDescriptor> Fields => [];   // typed fields the editor renders
    bool ResolvesOwnTemplates => false;
    Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action);
}

public interface ICommandCondition
{
    string ConditionType { get; }                    // matches PipelineStepCondition.ConditionType
    Task<bool> EvaluateAsync(PipelineExecutionContext ctx, ConditionDefinition condition);
}
```
`IConditionEvaluator`, `CompiledCondition` and `ActionContext` (the design's names) do not exist; §4.4 has the as-built runtime types.

### 3.13a Tier authoring-count enforcement (shared rule for §3.1 / §3.7; design intent — the tier wiring is not re-verified against the as-built services)

`ICommandService` and `ITimerManagementService` cap the **count** of author-created content per the
`TierLimit` mechanism (`monetization-billing.md` §8 — the authoritative owner; this is the consumer-side wiring). No new
infrastructure: each create/update path reads the tenant entitlement through **`IBillingTierService.GetEntitlementAsync`**
(the single tier-aware source — `IFeatureGateService` is **not** forked) and compares the relevant `LimitValue` to the
current count **before persisting**.

**`IEventResponseService` (§3.8) is deliberately NOT a participant here (S-EVENTRESPONSE-NO-CREATE):** its rows are a
fixed, seeded catalogue keyed by event type, never user-created, so an authoring-count cap on it would be decorative —
`event_responses` carries no `[CountedResource]` and no `LimitedResourceRegistry` entry.

- **Keys consumed here:** `custom_commands` (live `Command` count, §3.1 `CreateAsync`), `timers` (live `Timer` count,
  §3.7 `CreateAsync`), and `response_variations_per_trigger` (length of `Command.TemplateResponses` in §3.1, and of the
  `random_response` action's `Messages` on the reward pipeline). `LimitValue = -1` ⇒ unlimited.
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

### 4.1 Commands (as-built — `Application/Commands/Dtos/CommandDtos.cs`)

`MinPermissionLevel` is a rung **name** (`Everyone`, `Subscriber`, `Vip`, `Artist`, `Moderator`, `LeadModerator`, `Editor`, `Broadcaster`), never a number; the numeric `AllowedValues` ladder list of the original design is gone. `Tier`, `PrefixMode` and `MatchMode` are plain strings on the wire.
```csharp
sealed record CommandDto(Guid Id, string Name, string Tier, string MinPermissionLevel, bool IsEnabled,
    string PrefixMode, string? CustomPrefix, string MatchMode, string? MatchPattern,
    string? TemplateResponse, List<string>? TemplateResponses, Guid? PipelineId,
    int CooldownSeconds, int UserCooldownSeconds, bool CooldownPerUser, string? Description,
    List<string> Aliases, long UseCount, DateTime CreatedAt, DateTime UpdatedAt) { string? PresetKey { get; init; } }

sealed record CommandListItem(Guid Id, string Name, string Tier, string MinPermissionLevel, bool IsEnabled,
    string PrefixMode, string? CustomPrefix, string MatchMode, string? MatchPattern,
    int CooldownSeconds, int UserCooldownSeconds, bool CooldownPerUser, string? Description,
    List<string> Aliases, long UseCount, DateTime CreatedAt,
    string? TemplateResponse, List<string>? TemplateResponses, Guid? PipelineId) { string? PresetKey { get; init; } }

sealed record CreateCommandDto {
    [Required, MaxLength(100)] required string Name;
    string Tier = "template";                                   // template|pipeline|code
    [MaxLength(20)] string MinPermissionLevel = "Everyone";     // a rung name
    [MaxLength(20)] string PrefixMode = "Default";              // Default|Custom|None
    [MaxLength(8)] string? CustomPrefix;                        // meaningful when PrefixMode=Custom
    [MaxLength(20)] string MatchMode = "StartsWith";            // StartsWith|Exact|Contains|Regex
    [MaxLength(200)] string? MatchPattern;                      // meaningful when MatchMode=Regex; stored unvalidated (§3.6)
    [MaxLength(2000)] string? TemplateResponse;
    List<string>? TemplateResponses;
    Guid? PipelineId;
    [Range(0,86400)] int CooldownSeconds;
    [Range(0,86400)] int UserCooldownSeconds;
    bool CooldownPerUser;
    [MaxLength(500)] string? Description;
    List<string>? Aliases;
    bool IsEnabled = true; }
sealed record UpdateCommandDto { /* every CreateCommandDto field, nullable; adds Name and IsEnabled */ }
```

### 4.2 Dispatch / resolution (design intent, unbuilt — no `ICommandDispatcher`, so none of these records exist; `UserPermissionLevel` here would be a rung, not a number)
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

### 4.3 Pipelines (as-built — `Application/Commands/Dtos/PipelineDtos.cs`)
```csharp
sealed record PipelineDto(Guid Id, string ChannelId, string Name, string? Description, bool IsEnabled, string TriggerKind,
    [property: JsonPropertyName("graph")] JsonElement? GraphJsonCache, long TriggerCount, DateTime? LastTriggeredAt,
    DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<string>? ParameterNames);
sealed record PipelineListItemDto(Guid Id, string Name, string? Description, bool IsEnabled, long TriggerCount,
    DateTime? LastTriggeredAt, DateTime UpdatedAt, IReadOnlyList<string>? ParameterNames) { bool HasPlatformDefault { get; init; } }
sealed record CreatePipelineDto { [Required,MaxLength(200)] string Name; [MaxLength(500)] string? Description;
    bool IsEnabled = true; [MaxLength(30)] string TriggerKind = "manual"; [JsonPropertyName("graph")] object? GraphJsonCache; }
sealed record UpdatePipelineDto { [MaxLength(200)] string? Name; [MaxLength(500)] string? Description;
    bool? IsEnabled; [MaxLength(30)] string? TriggerKind; [JsonPropertyName("graph")] object? GraphJsonCache; }
sealed record PipelineBlastRadiusDto(int CommandCount, IReadOnlyList<string> CommandNames,
    int ChatTriggerCount, IReadOnlyList<string> ChatTriggerPatterns, int TimerCount, IReadOnlyList<string> TimerNames,
    int EventResponseCount, IReadOnlyList<string> EventResponseEventTypes) { int TotalReferences { get; } }

// GET pipelines/actions — the editor's palette source
sealed record PipelineCatalogueDto(IReadOnlyList<PipelineActionDescriptorDto> Actions, IReadOnlyList<PipelineConditionDescriptorDto> Conditions);
sealed record PipelineActionDescriptorDto(string Type, LocalizedText Category, LocalizedText Description, IReadOnlyList<PipelineActionFieldDto> Fields);
sealed record PipelineActionFieldDto(string Name, string Kind, bool Required, bool Repeatable, IReadOnlyList<string>? Options, LocalizedText? Description);
sealed record PipelineConditionDescriptorDto(string Type);
```
The graph is one JSON document (`graph`), not a typed `PipelineGraphDto`: its steps are `PipelineStepDefinition` (`condition`, `action`, `stop_on_match`, `continue_on_error`, plus the tree fields `id`, `parent_step_id`, `branch`, `block_kind`, `block_config`, `order`). The design's `PipelineGraphDto` / `PipelineStepDto` / `PipelineStepConditionDto` and `PipelineValidationResult(IsValid, Errors[])` were not built; the validation result is `PipelineValidationResult(IsValid, ErrorCode, ErrorMessage)` (§3.6).

### 4.4 Engine runtime contracts (as-built — `Application/Abstractions/Pipeline/`)
```csharp
sealed class PipelineExecutionContext {                // replaces the design's ActionContext
    string ExecutionId; Guid BroadcasterId; string TriggeredByUserId; string TriggeredByDisplayName;
    string MessageId; string? RedemptionId; string? RewardId; string? ChannelEventId; string RawMessage;
    CancellationToken CancellationToken;
    Dictionary<string,string> Variables;               // the run's variable bag, case-insensitive
    int CurrentStepIndex; bool ShouldStop; bool ShouldBreakLoop; bool ShouldContinueLoop;
    int LoopDepth; int CallDepth; string? ReturnValue; List<StepExecutionLog> StepLogs; }
sealed class ActionDefinition { string Type; Dictionary<string,JsonElement>? Parameters; /* GetString / GetInt / GetBool */ }
sealed class ConditionDefinition { string Type; Dictionary<string,JsonElement>? Parameters; /* GetString */ }
sealed class ActionResult { bool Succeeded; string? Output; string? ErrorMessage; bool Suspended; string? WaitEventName; int? WaitTimeoutSeconds;
    static ActionResult Success(string? output = null); static ActionResult Failure(string error);
    static ActionResult Suspend(string? output = null); static ActionResult SuspendWaitingForEvent(string eventName, int timeoutSeconds); }
```
A deliberate stop is `ShouldStop` (set by the `stop` action), not a result flag; there is no `StopPipeline`, `VariablesSet`, `HostCallCount`, `TaintedVariables`, `EventType` or `JournalEventId`. `CompiledPipeline` / `CompiledStep` / `CompiledCondition` were not built (§3.5). Action `ConfigJson` keys are snake_case (`event_name`, `playlist_id`, `min_role`).

### 4.5 Timers / Event responses (as-built)

```csharp
// Application/Commands/Dtos/TimerDtos.cs
sealed record TimerDto(Guid Id, string Name, List<string> Messages, int IntervalMinutes, int MinChatActivity,
    bool IsEnabled, bool FireOnce, Guid? PipelineId, DateTime? LastFiredAt, int NextMessageIndex, DateTime CreatedAt, DateTime UpdatedAt);
sealed record TimerListItem(Guid Id, string Name, int IntervalMinutes, bool IsEnabled, bool FireOnce,
    DateTime? LastFiredAt, int MessageCount, DateTime CreatedAt) { bool HasPlatformDefault { get; init; } }
sealed record CreateTimerDto { [Required,MaxLength(100)] required string Name; required List<string> Messages;
    Guid? PipelineId; [Range(1,1440)] int IntervalMinutes = 30; [Range(0,10000)] int MinChatActivity; bool IsEnabled = true; bool FireOnce; }
sealed record UpdateTimerDto { /* every field nullable */ }

// Application/Commands/Dtos/EventResponseDtos.cs
sealed record EventResponseDto(Guid Id, string EventType, bool IsEnabled, string ResponseType, string? Message, Guid? PipelineId,
    Dictionary<string,string> Metadata, DateTime CreatedAt, DateTime UpdatedAt, bool FollowsPlatformDefault, bool SpeakWithTts);
sealed record EventResponseListItem(Guid Id, string EventType, bool IsEnabled, string ResponseType, DateTime UpdatedAt, bool FollowsPlatformDefault);
sealed record EventResponsePresetDto(string EventType, LocalizedText DefaultTemplate, IReadOnlyList<string> Variables);   // GET event-responses/catalog
sealed record UpdateEventResponseDto { bool? IsEnabled; [RegularExpression("^(chat_message|overlay|pipeline|none)$")] string? ResponseType;
    [MaxLength(2000)] string? Message; Guid? PipelineId; Dictionary<string,string>? Metadata; bool? SpeakWithTts; }
```
The built-in command DTOs are `BuiltinCommandDto` / `BuiltinSettingsUpdate` (§3.9) and `BuiltinReplyGroupDto` / `BuiltinReplyDto` / `BuiltinReplyVariableDto` (§11). The design's typed `BuiltinCommandOverrides` POCO, `BuiltinCommandOverridesDto`, `BuiltinResolution`, `ChannelSnapshot`, `StreamSnapshot` and the dispatcher-seeded `BuiltinCommandContext` were never built and are removed from this spec; the persisted blob shape is in §11.

---

## 5. Controller endpoints

All controllers extend `BaseController`, `[ApiVersion("1.0")]`, `[Authorize]`, return `StatusResponseDto<T>` /
`PaginatedResponse<T>` via `ResultResponse(...)`/`GetPaginatedResponse(...)`. Tenant `{channelId}` is `Guid` and **must be
verified owned by the authenticated principal** (IDOR must-fix #1 — `ICurrentTenantService`/`IChannelAccessService`);
mismatch ⇒ 403.

**Role gate.** Gate-1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). Gate-2 =
the `[RequireAction("<key>")]` attribute on the action, which runs `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)`
and enforces the per-route floor named in the action-key column before the service call (403 FORBIDDEN when below). The keys are seeded global
`ActionDefinitions` (`ActionDefinitionSeeder`, schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded
`FloorLevel`. Floors are rung names, never numbers. As seeded:

- **`*:read` keys** (`commands:read`, `pipelines:read`, `pipelines:validate`, `eventresponses:read`, `timers:read`) default to **Moderator** and the broadcaster may lower them to **Vip**.
- **`*:write` keys** (`commands:write`, `pipelines:write`, `eventresponses:write`, `timers:write`) default to **Moderator** and cannot be lowered (the write bundles delete). The broadcaster may still raise them.
- `commands:builtin:read` / `commands:builtin:write` are seeded but no route uses them; `BuiltinsController` gates on `commands:read` / `commands:write`.

All controllers below are per channel, under `api/v{version:apiVersion}/channels/{channelId}/...`. The `{commandName}` segment is the command's name; `{id}`, `{eventType}` and `{builtinKey}` are as named.

| Controller | Verb + Route | Request DTO | Response DTO | Gate-2 action key |
|---|---|---|---|---|
| `CommandsController` | `GET commands` | `PageRequestDto` | `PaginatedResponse<CommandListItem>` | `commands:read` |
| | `GET commands/{commandName}` | — | `StatusResponseDto<CommandDto>` | `commands:read` |
| | `POST commands` | `CreateCommandDto` | `StatusResponseDto<CommandDto>` (201) | `commands:write` |
| | `PUT commands/{commandName}` | `UpdateCommandDto` | `StatusResponseDto<CommandDto>` | `commands:write` |
| | `DELETE commands/{commandName}` | — | 204 | `commands:write` |
| | `GET command-presets` | — | `StatusResponseDto<IReadOnlyList<CommandPresetDto>>` | `commands:read` |
| | `POST commands/{commandName}/reset-to-preset` | — | `StatusResponseDto<CommandDto>` | `commands:write` |
| `BuiltinsController` (route base `builtins`) | `GET builtins` | — | `StatusResponseDto<IReadOnlyList<BuiltinCommandDto>>` | `commands:read` |
| | `GET builtins/{builtinKey}` | — | `StatusResponseDto<BuiltinCommandDto>` | `commands:read` |
| | `PATCH builtins/{builtinKey}` | `{ bool Enabled }` | `StatusResponseDto<object>` | `commands:write` |
| | `PUT builtins/{builtinKey}/settings` | `UpdateBuiltinSettingsRequest` (`CooldownSeconds`, `MinPermissionLevel` rung name) | `StatusResponseDto<BuiltinCommandDto>` | `commands:write` |
| | `DELETE builtins/{builtinKey}/settings` | — | `StatusResponseDto<BuiltinCommandDto>` (reset to defaults) | `commands:write` |
| | `PUT builtins/{builtinKey}/tts` | `{ bool Enabled }` | `StatusResponseDto<object>` | `commands:write` |
| | `GET builtins/replies` · `PUT builtins/{builtinKey}/replies/{slot}` · `DELETE builtins/{builtinKey}/replies/{slot}` | see §11 | see §11 | `commands:read` / `commands:write` |
| `PipelinesController` | `GET pipelines` | `PageRequestDto` | `PaginatedResponse<PipelineListItemDto>` | `pipelines:read` |
| | `GET pipelines/{id:guid}` | — | `StatusResponseDto<PipelineDto>` | `pipelines:read` |
| | `GET pipelines/actions` | — | `StatusResponseDto<PipelineCatalogueDto>` (the editor palette, §6.1) | `pipelines:read` |
| | `POST pipelines` | `CreatePipelineDto` | `StatusResponseDto<PipelineDto>` (201) | `pipelines:write` |
| | `PUT pipelines/{id:guid}` | `UpdatePipelineDto` | `StatusResponseDto<PipelineDto>` | `pipelines:write` |
| | `GET pipelines/{id:guid}/blast-radius` | — | `StatusResponseDto<PipelineBlastRadiusDto>` | `pipelines:write` |
| | `DELETE pipelines/{id:guid}` | — | 204 | `pipelines:write` |
| | `POST pipelines/{id:guid}/test-run` | `PipelineTestRunRequest` (sample variables) | `StatusResponseDto<TestRunResultDto>` | `pipelines:write` |
| | `POST pipelines/validate` | `PipelineGraphInput` | `StatusResponseDto<PipelineValidationResult>` | `pipelines:validate` |
| | `GET pipelines/{id:guid}/platform-default` · `POST pipelines/{id:guid}/platform-default/restore` | — | see "Per-channel edit and reset" | `pipelines:read` / `pipelines:write` |
| `PipelineExecutionsController` | `GET pipeline-executions` · `GET pipeline-executions/{id:long}` | paging | run history (H.4) | `pipelines:read` |
| `TimersController` | `GET timers` | `PageRequestDto` | `PaginatedResponse<TimerListItem>` | `timers:read` |
| | `GET timers/{id:guid}` | — | `StatusResponseDto<TimerDto>` | `timers:read` |
| | `POST timers` | `CreateTimerDto` | `StatusResponseDto<TimerDto>` (201) | `timers:write` |
| | `PUT timers/{id:guid}` | `UpdateTimerDto` | `StatusResponseDto<TimerDto>` | `timers:write` |
| | `POST timers/{id:guid}/toggle` | — | `StatusResponseDto<TimerDto>` | `timers:write` |
| | `DELETE timers/{id:guid}` | — | 204 | `timers:write` |
| | `GET timers/{id:guid}/platform-default` · `POST timers/{id:guid}/platform-default/restore` | — | see "Per-channel edit and reset" | `timers:read` / `timers:write` |
| `EventResponsesController` | `GET event-responses` | `PageRequestDto` | `PaginatedResponse<EventResponseListItem>` | `eventresponses:read` |
| | `GET event-responses/catalog` | — | `StatusResponseDto<IReadOnlyList<EventResponsePresetDto>>` | `eventresponses:read` |
| | `GET event-responses/{eventType}` | — | `StatusResponseDto<EventResponseDto>` | `eventresponses:read` |
| | `PUT event-responses/{eventType}` | `UpdateEventResponseDto` | `StatusResponseDto<EventResponseDto>` | `eventresponses:write` |
| | `POST event-responses/{eventType}/reset` | — | 204 (row reset in place, never deleted) | `eventresponses:write` |
| | `GET event-responses/overlay` · `GET event-responses/alert-queue` | — | alert-overlay state | `eventresponses:read` |

There is no `DELETE event-responses/{eventType}` (S-EVENTRESPONSE-NO-CREATE) and no `PUT .../commands/builtin/{key}/enabled` or `/overrides` route; the design's `BuiltinCommandsController` and `BuiltinCommandOverridesDto` were never built.

### 5.1 Per-channel edit and reset-to-default

The rule: **every system-provided thing is editable per channel and can be put back on the platform default.** Editing never touches the platform copy; a channel's own row carries the change and the reset undoes it for that channel only.

| Thing | Edit (per channel) | Reset to default |
|---|---|---|
| Built-in command | `PUT builtins/{builtinKey}/settings`, `PATCH builtins/{builtinKey}`, `PUT builtins/{builtinKey}/tts` | `DELETE builtins/{builtinKey}/settings` |
| Built-in reply text | `PUT builtins/{builtinKey}/replies/{slot}` | `DELETE builtins/{builtinKey}/replies/{slot}` |
| Fun-command preset (`!8ball`, `!hug`, …) | `PUT commands/{commandName}` | `POST commands/{commandName}/reset-to-preset` (keeps the name and the on/off state; `GET command-presets` previews what a reset writes) |
| Timer installed from a platform template | `PUT timers/{id}` | `POST timers/{id}/platform-default/restore` (`GET .../platform-default` previews it; `TimerListItem.HasPlatformDefault`) |
| Pipeline installed from a platform template | `PUT pipelines/{id}` | `POST pipelines/{id}/platform-default/restore` (`PipelineListItemDto.HasPlatformDefault`) |
| Event response | `PUT event-responses/{eventType}` | `POST event-responses/{eventType}/reset` (`FollowsPlatformDefault` says whether the channel still tracks the default) |
| Economy game | `PUT economy/games` | `POST economy/games/{gameType}/reset` (keeps on/off; `economy:games:write`) |
| TTS config | `PUT tts/config` | `POST tts/config/reset` (`GET tts/config/defaults` shows the defaults; `tts:config:write`) |
| System widget | per-channel widget edit (`WidgetsController`, `widget:write`) | see the widgets spec — no reset route was re-verified here |

Platform-wide defaults are edited by platform admins through the `*DefaultsAdminController` family (`BuiltinReplyDefaultsAdminController`, `EventResponseDefaultsAdminController`, `ActionDefaultsAdminController`, `TtsVoiceDefaultsAdminController`); a channel that has not overridden a field follows the new default.

> No public controller for `IPipelineEngine` / `ICooldownManager` — those are runtime-internal (driven by chat/EventSub
> ingestion + the timer `BackgroundService`), not HTTP-exposed. (`ICommandDispatcher` and `IPipelineCompiler` do not exist.)

---

## 6. Pipeline actions + conditions + template variables

### 6.1 Built-in `ICommandAction` blocks (`Type` = snake_case registry key)

**The source of truth is the registry, not this table.** `GET pipelines/actions` (§5) returns every registered
`ICommandAction` with its category, description and typed field descriptors — over 100 action types as of 2026-09-30, grouped by
domain. The editor palette renders from it, so a new action never needs a spec edit to show up. Types in the tree today
(snake_case `ActionType`): chat and flow (`send_message`, `send_reply`, `announce`, `set_variable`, `wait`, `wait_for_event`,
`wait_until_raid_fires`, `stop`, `break`, `continue`, `run_pipeline`, `return_value`, `schedule_pipeline`, `pick_from_list`),
counters and viewer data (`set_counter`, `adjust_counter`, `set_viewer_data`, `adjust_viewer_data`, `clear_viewer_data`), moderation
(`timeout`, `ban`, `delete_message`, `shoutout`, `start_raid`, `permit`, `unpermit`), music (`song_*`, `music_*`, `play_track_once`,
`playlist_add`), sound and speech (`play_sound`, `stop_sound`, `play_tts`, `tts_synthesize`), rewards and economy
(`redemption_fulfill`, `redemption_refund`, `grant_currency`, `deduct_currency`, `check_balance`, `play_game`, giveaways, `jar_contribute`,
`require_tier`), integrations (`obs_*`, `vts_*`, `send_discord_notification`, `send_webhook`, `widget_event`, `post_quote`, `submit_media`,
`set_pronoun`, `start_live_game` / `cancel_live_game`), and `run_code`.

**Specced here, unbuilt:** `random_response`, `http_request` and `send_whisper` have no `ICommandAction`, and `delay` is not a
type (the shipped type is `wait`). Their tables below stay as the design for when they are built; the `send_whisper` row names the
shipped `ITwitchWhispersApi` it would call. Everything else in the table is built under the type name shown, with the config
key names in snake_case (`event_name`, `min_role`, `delay_seconds`) rather than the PascalCase shown in the design column.

**Design table (config keys shown PascalCase as designed; read each `Type` against the registry):**

| `Type` | Config DTO (the step's `ConfigJson` shape) | Behavior (state change / side effect) |
|---|---|---|
| `send_message` | `{ string Message }` | renders `Message` via `ITemplateResolver`, sends via `IChatProvider.SendMessageAsync` |
| `send_reply` | `{ string Message }` | reply to `MessageId` via `IChatProvider.SendReplyAsync` |
| `set_variable` | `{ string Name, string Value }` | sets `Variables[Name]` (rendered) |
| `set_counter` | `{ string Name, long Value }` | sets the persistent counter `(ctx.BroadcasterId, Name)` to `Value` — upserts `NamedCounters` (G.4); tenant-scoped via `ctx.BroadcasterId` |
| `adjust_counter` | `{ string Name, long Delta }` | increments/decrements counter `(ctx.BroadcasterId, Name)` by `Delta` (atomic upsert; absent ⇒ starts at 0); returns the **new value** as `Output` and sets it into `Variables[Name]`; tenant-scoped via `ctx.BroadcasterId` |
| `set_viewer_data` | `{ string Key, string Value, string? Target }` | upserts the per-viewer `ViewerDatum` (G.14) for the target viewer (default = triggering viewer; `Target` resolves a `@name`/id) — string set; tenant+viewer-scoped via `ctx.BroadcasterId`. (owned by `per-viewer-data.md`) |
| `adjust_viewer_data` | `{ string Key, long Delta, string? Target }` | atomic numeric increment of the per-viewer `ViewerDatum` (G.14) for the target viewer (default = triggering viewer; absent ⇒ starts at `Delta`); returns the **new value** as `Output` and sets it into `Variables[Key]`; tenant+viewer-scoped via `ctx.BroadcasterId`. (owned by `per-viewer-data.md`) |
| `wait` | `{ int? Seconds, int? Milliseconds }` | delays (capped per-step; counts against cumulative `Wait` cap) |
| `random_response` *(unbuilt)* | `{ List<string> Messages }` | picks one at random, sends to chat |
| `stop` | `{}` | sets `ctx.ShouldStop` — terminates the run cleanly |
| `timeout` | `{ string UserRef, int DurationSeconds, string? Reason }` | `IChatProvider.TimeoutUserAsync` |
| `ban` | `{ string UserRef, string? Reason }` | `IChatProvider.BanUserAsync` |
| `delete_message` | `{ string MessageId }` | `IChatProvider.DeleteMessageAsync` |
| `shoutout` | `{ string TargetChannel }` | shoutout via chat provider |
| `song_request`/`song_skip`/`song_current`/`song_queue`/`song_volume` | as today | broker-pattern music ops bound to `ctx.BroadcasterId` (token injected host-side; **no token/url in config**) |
| `run_code` | `{ Guid CodeScriptId }` *(only this guid — no inline source in config)* | resolves `CurrentVersionId`, calls `IScriptExecutor.ExecuteAsync` (Jint self-host; the Wasmtime SaaS adapter is a separate profile binding); capability-brokered |
| `http_request` *(unbuilt)* | `{ string Fqdn, string Method, string? Path, string? BodyTemplate, string? ResultVariable }` | egress **only** to an enabled `HttpEgressAllowlist` row for the tenant; FQDN-pinned, no redirects, response-size capped (`MaxResponseBytes`). **Method enforcement:** `Method` must be in the row's `AllowedMethods` CSV (else reject). **Request-body cap:** a body is permitted only when `AllowRequestBody=true` (and never for GET/HEAD); the rendered `BodyTemplate` is **rejected, not truncated**, when it exceeds `MaxRequestBytes`. **Path enforcement:** when `PathPrefix` is set, the request `Path` must start with it (null `PathPrefix` = any path on the FQDN). Result into `ResultVariable` |
| `send_webhook` | `{ Guid OutboundWebhookEndpointId }` *(only this guid — **no** url/secret/headers/body in config; broker pattern)* | resolves the `OutboundWebhookEndpoints` (H.8) row for `ctx.BroadcasterId` and enqueues a delivery through `IOutboundWebhookDispatcher` (`webhooks.md` §3.6). The target url, `whsec_` signing secret, body/header templates, and SSRF boundary all live on the endpoint + its `HttpEgressAllowlist` (H.7) row — Standard-Webhooks signed, FQDN-pinned, retried/dead-lettered async. Fast-ack: returns `ActionResult.Success` on enqueue; delivery is the background worker's job. The sole config key is an opaque endpoint `Guid` (carries no url/secret) so the `ICommandConfigValidator` (§3.6) invariant holds by construction. |
| `send_whisper` *(unbuilt)* | `{ string TargetUserRef, string Message }` | renders `Message` via `ITemplateResolver` and sends the whisper through the shipped `ITwitchWhispersApi.SendWhisperAsync(Guid fromUserId, string toTwitchUserId, string message)`: the sender is whichever identity Guid the action passes as `fromUserId` — the action would pass the channel's bot account's User Guid for `ctx.BroadcasterId` — resolved internally to `from_user_id` and sent on that identity's **own** user token (`user:manage:whispers` pre-checked per call by the sub-client; no bot-token brokering happens inside the whispers API; no token in config). Gated by `chat:whisper:send`; **rate-limited** per-tenant via the distributed limiter (whisper spam is a ban risk — the limits-baseline applies). `user:manage:whispers` requested progressively. |
| `play_sound` | `{ string Clip, int? Volume, bool WaitForFinish, string? Handle }` *(no url/token in config; broker pattern)* | resolves the library clip (`Clip` = id or name) for `ctx.BroadcasterId` via `ISoundClipService.ResolveForPlaybackAsync` and pushes exactly one `IOverlayClient.PlaySound` to the always-loaded overlay (effective volume = clip default unless `Volume` overrides). When `WaitForFinish`, the action awaits the clip's (capped) `DurationMs` before completing so a following action runs after playback. Unknown/disabled clip ⇒ typed action failure (no throw, **no overlay push**). (owned by `sound-system.md`) |
| `stop_sound` | `{ string? Handle, bool All }` | pushes a stop to the overlay for the named `Handle`, or stops all overlay playback when `All` (bound to `ctx.BroadcasterId`). (owned by `sound-system.md`) |
| `run_pipeline` | `{ string Pipeline, string Mode (inline\|detached), IReadOnlyList<string>? Args, bool Wait }` | invokes another of the channel's pipelines — `inline` shares the current run's variable bag (merged on return), `detached` is an independent run (optional `Wait`); recursion-depth-capped (fail-closed at `MaxRecursionDepth`). (owned by `pipeline-control-flow.md`) |
| `break` | — | exits the enclosing `loop` block (no-op outside a loop). (owned by `pipeline-control-flow.md`) |
| `continue` | — | skips to the enclosing loop's next iteration (no-op outside a loop). (owned by `pipeline-control-flow.md`) |

All actions: **fail-closed** (unknown `Type` rejected at save and at run), **no tenant/credential/url config keys**
(`ICommandConfigValidator` invariant), `Guid`-typed `BroadcasterId` from `PipelineExecutionContext`.

### 6.2 Condition evaluators (`Type` matches `PipelineStepCondition.ConditionType`)

Built (`ICommandCondition`, `Infrastructure/Platform/Pipeline/`): `user_role`, `random` and `comparison`. `var_compare` and `cooldown` below are **unbuilt**; `comparison` is the shipped variable comparator.

| `Type` | Operands | Behavior |
|---|---|---|
| `user_role` | param `min_role` (or `role`) = a rung name | true when the triggering user's rung ≥ the required rung (ascending ladder: Everyone, Subscriber, Vip, Artist, Moderator, LeadModerator, Editor, Broadcaster); fail-closed on an unknown rung name |
| `random` | param `chance` (or `percent`) | true with the given probability |
| `comparison` | left / operator / right params | compares a (rendered) value to another value |
| `var_compare` *(unbuilt)* | `LeftOperand`, `Operator` (`contains`\|`equals`\|`iequals`\|`startswith`\|`endswith`\|`matches`\|`gt`\|`lt`\|`gte`\|`lte`), `RightOperand` | compares a (rendered) variable to a value via the **shared comparator** (below) |
| `cooldown` *(unbuilt)* | `LeftOperand` = scope key | true when not on the named cooldown (checks `ICooldownManager`) |

`Negate` inverts the result. **Unknown `ConditionType` ⇒ fail-closed (run abort), never treat-as-true** (reverses the
current live defect).

**Shared comparator (`IValuePredicate` — design intent, unbuilt; `comparison` is the as-built condition).** The operator set above is defined
**once** and reused by **two surfaces**: this pipeline-gating `var_compare` condition (author-driven branching) **and** the
render-time `{{if.<path>.<op>:…}}` template predicate (§6.3.2). One operator table, one evaluator — no second comparator.
Semantics: `gt`/`lt`/`gte`/`lte` are **numeric** (both sides parsed as numbers; if either side is non-numeric ⇒ false);
`contains`/`equals`/`startswith`/`endswith` are **ordinal** string ops; `iequals` is case-insensitive; `matches` is a regex
test via the **shared `IRegexMatcher`** (§6.4 — NonBacktracking, time-bounded, same ReDoS policy as `MatchMode=Regex`).

### 6.3 Template variables (design intent — as-built is `ITemplateResolver` + `TemplateHelperRegistry`)

> **As-built note (checked 2026-09-30).** The shipped grammar is **single-brace**: `{namespace.key}`, resolved by the singleton
> `TemplateResolver : ITemplateResolver` (`Infrastructure/Platform/Templating/TemplateResolver.cs`); the validator is
> `ITemplateHelperValidator`, and the catalogue of valid helpers is `TemplateHelperRegistry`
> (`Application/Abstractions/Templating/`). The pattern is a flat `\{([^{}]+)\}` walk plus dedicated patterns for `{list.pick.<name>}`,
> `{custom.<a>.<b>}` and `{transform.<fn>:<text>}`; there is no `VariableResolver` class and no balanced-brace recursion. In this
> section every `{{ … }}` is the **design** spelling of a `{ … }` token, and a helper that is not in `TemplateHelperRegistry` is unbuilt.
> Unbuilt in the tree: the `{{if.*}}` conditional helper and its value predicates, the recursion / 4 KB output / cycle bounds, taint
> tracking, and the `{{user.level}}` numeric variable (dropped — users see rung names, never numbers; use `{user.role}`). Unknown
> tokens render empty.


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
| `user.ismod` | rung ≥ `Moderator` | derived from the resolved role rung (seed) |
| `user.issub` | active subscription | `TwitchSubscribers.EndedAt IS NULL` (seed sub row) |
| `user.isvip` | `Vip` standing | `ChannelCommunityStandings.Standing` (seed resolved standing) |
| `user.isfollower` | following the channel | `TwitchFollowers` row exists (Helix/cached) |
| `user.isbroadcaster` | rung = `Broadcaster` | derived from the resolved role rung (seed) |

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
| `{{user.role}}` | effective role name, friendly-cased: `Viewer · Subscriber · VIP · Artist · Moderator · Lead Moderator · Editor · Broadcaster`. The `Everyone` ladder floor (absence of an elevated role) renders **`Viewer`**, never the literal `Everyone` sentinel — `Everyone` stays a gating-only value (the §6.2 `user_role` condition floor). | `IRoleResolver.ResolveEffectiveLevelAsync` → `roles-permissions.md` ladder, mapped to a display label | seed resolved level |
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

- **`{{channel.views}}` stays catalogued pending re-verification.** The claim that Twitch deprecated `view_count` on the
  users endpoint is **re-verified against the current Helix docs before any exclusion** (full-coverage rule: never act
  on a "deprecated/skip" claim about an external API without re-checking the live docs). Until verified, the token stays
  in the catalog and resolves from the Helix field `twitch-helix.md` maps for it; if the field is confirmed gone, the
  replacement metric is a data-source concern owned by `twitch-helix.md`, and this catalog keeps the token name bound to it.
- **`{{economy.ranktier}}` / `{{economy.tiername}}` and daily/streak economy tokens depend on the economy subsystem.**
  `economy.md` today models balance, lifetime earned/spent, and numeric leaderboard rank — not a named rank-tier, daily
  bonus, or earn-streak. These tokens are owned by `economy.md`: when it adds tier/streak modeling, the corresponding
  tokens join the `economy.*` table via the same dispatcher-seeds/resolver-renders pattern (no resolver change). This is
  a **dependency on `economy.md`**, not an open question for this spec — the `economy.*` tokens already catalogued
  (`{{economy.balance:user}}`, `{{economy.rank:user}}`, `{{economy.top:N}}`, …) are the complete set this subsystem ships
  against the current economy model.

> **Already backed (in the catalog above):** `{{count.*}}` named counters → `NamedCounters` (schema G.4) + `set_counter`/`adjust_counter` actions (§6.1). `{{random.chatter}}` → Helix Get Chatters (`twitch-helix.md` §3.2, `moderator:read:chatters` progressive scope). `{{bot.prefix}}` storage → `Channels.DefaultCommandPrefix` (schema A.2). Raw date/time format patterns → the `:raw=` sentinel arg (§6.3.1).

Unknown token ⇒ empty string (render-time, non-fatal — only **execution** of unknown action/condition is fail-closed).

### 6.4 `IRegexMatcher` (design intent — unbuilt)

> **As-built note (checked 2026-09-30).** No `IRegexMatcher` or `RegexMatcher` exists. `ChannelRegistry` compiles a command's or chat trigger's
> `MatchPattern` itself (`RegexOptions.IgnoreCase` unless case-sensitive, a **100 ms match timeout**), caches the `Regex` on the registry
> candidate (`CompiledRegex`), and `ChatMessageHandler` calls `CompiledRegex.IsMatch`, treating a `RegexMatchTimeoutException` as no match.
> It is a backtracking engine with a timeout, not `NonBacktracking`, and the pattern is not validated at save (§3.6). The policy below
> — one shared matcher, linear-time engine, save-time rejection of unsupported constructs, a 200-char cap — is the design for when it is
> built. There is no `matches` operator to share with, since `{{if.*}}` and `var_compare` are unbuilt.

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

As-built, in `NomNomzBot.Infrastructure/DependencyInjection.cs` (`AddInfrastructure`). Most services register by the `I<X>Service`
convention scan; the rest are explicit lines. There are no registries and no compiler.

```csharp
// Explicit singletons
services.AddSingleton<ICooldownManager, CooldownManager>();          // in-memory (§3.11)
services.AddSingleton<ITemplateResolver, TemplateResolver>();
services.AddSingleton<ITemplateHelperValidator, TemplateHelperValidator>();
services.AddSingleton<IChannelBuiltinReplyOverrides, Commands.Builtins.ChannelBuiltinReplyOverridesReader>();

// Explicit scoped
services.AddScoped<IPipelineEngine, PipelineEngine>();
services.AddScoped<IEventResponseExecutor, EventResponseExecutor>();
services.AddScoped<ICommandConfigValidator, CommandConfigValidator>();
services.AddScoped<IPipelineStepReferenceScanner, PipelineStepReferenceScanner>();
services.AddScoped<IBuiltinCommandCatalog, BuiltinCommandCatalog>();
services.AddScoped<IBuiltinCommand, Commands.Builtins.UptimeBuiltin>();   // one line per built-in: lurk, followage, stats, …

// By convention (I<X>Service → <X>Service, scoped)
//   ICommandService, IPipelineService, ITimerManagementService, IEventResponseService, IBuiltinCommandService,
//   IBuiltinReplyService, IScheduledPipelineService, IPipelineTestRunService, IChatTriggerService, ICommandPresetService, …

// Assembly scans — every implementation of the interface, no per-class line
services.AddImplementationsOf<ICommandAction>(infrastructure, ServiceLifetime.Transient);
services.AddImplementationsOf<ICommandCondition>(infrastructure, ServiceLifetime.Transient);
services.AddImplementationsOf<IEventResponsePresenter>(infrastructure, ServiceLifetime.Scoped);

// Background workers (auto-registered by the hosted-worker scan)
//   TimerService (30 s tick), ScheduledPipelineExpiryService (5 s tick), WaitForEventTimeoutSweepWorker
```

`ICommandActionRegistry`, `IConditionEvaluatorRegistry`, `IPipelineCompiler`, `ICommandDispatcher`, `ITemplateEngine`,
`IRegexMatcher`, `ITimerService` and `TimerSchedulerService` are **not registered because they do not exist** (the timer scheduler
is `TimerService`).

**Deployment-profile adapters** (selected by the deployment profile):

| Abstraction | as-built | design intent, unbuilt |
|---|---|---|
| `ICooldownManager` | one in-memory `CooldownManager` in every profile | Redis-backed variant + `CommandCooldownStates` write-through for multi-node |
| `IPipelineCompiler` cache | does not exist | `HybridCache` L1 / L1 + Redis L2 |
| `run_code` executor (`IScriptExecutor`, **owned by sandbox subsystem**) | Jint (self-host); the Wasmtime SaaS adapter is a separate profile binding | — |
| Run-once guard (`IRunOnceGuard`) | `NoOpRunOnceGuard` (single node) / `PostgresRunOnceGuard` (Postgres profile) — `TimerService` and the sweepers use it | — |

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
3. **Built-in catalog identity is code-defined.** `BuiltinKey` strings (`followage`, `uptime`, `shoutout`, …) are defined by `IBuiltinCommand` implementations, never seeded DB rows. Catalog membership is the concrete set of `IBuiltinCommand` classes registered in DI (§7, one `AddScoped` line each), authored alongside the implementation — it is not a schema decision and carries no migration.

---

## 10. As-built — deferred one-shot pipeline execution

A generic **deferred-execution** primitive: "run pipeline P **once**, T seconds from now, with these variables", durable across a process restart. It is the missing tooling behind timed follow-ups (a Voice-Swap auto-revert, a feather auto-hide, a timed reward). It is deliberately **not** any of the near-neighbours: the `wait` action holds the running pipeline for the delay (occupies a concurrency slot, and the 5-min execution timeout would kill a minutes-long hold); a `Timer` is a recurring interval; a `RedemptionTimer` is a reward-scoped countdown.

**Entity `ScheduledPipelineTask` (H — pipelines, tenant-scoped, no soft-delete).** Columns: `Id`, `BroadcasterId`, `PipelineId` (Guid, the target resolved at schedule time for determinism), `PipelineName string(200)?` (bundle-portable fallback for display; `PipelineId` stays authoritative), `DueAt DateTimeOffset`, `VariablesJson text` (the initial variables, string→string), `TriggeredByUserId string(100)` / `TriggeredByDisplayName string(255)` (carried through so the deferred run keeps its actor), `Status string(20)` (`pending`\|`fired`\|`cancelled`\|`expired`), `DedupeKey string(200)?`, `CreatedAt`, `FiredAt DateTimeOffset?`. Indexes: `(Status, DueAt)` for the sweep; a **partial unique** index on `(BroadcasterId, DedupeKey) WHERE Status = 'pending'` so re-scheduling with a key replaces the one live run and terminal rows may reuse the key. **Lifecycle is a status machine, not a delete** — terminal rows are kept so the sweep's status filter is the natural idempotency guard (a fired row can never fire twice) and a short audit trail survives.

**Service `IScheduledPipelineService`** (Application `Commands.Services`, Infrastructure `Commands`, convention-registered scoped): `ScheduleAsync(broadcasterId, pipelineId, delaySeconds, variables, triggeredBy…, dedupeKey?)` and the name-first `ScheduleByNameAsync(…, pipelineName, …)` (case-insensitive, enabled pipelines only; `NOT_FOUND` when unknown) — both clamp the delay to **[1s, 24h]** and, with a dedupe key, **update the live pending row in place** rather than stacking. `CancelAsync` / `CancelByDedupeKeyAsync` mark `cancelled`; `ListPendingAsync` lists soonest-due first. `FireDueAsync` (the sweeper entry point) marks each due pending task terminal **before** dispatch (crash-safe: a fired row can't re-fire), then dispatches through the pipeline engine with the saved `PipelineId` + variables + actor; a task overdue beyond the **10-minute stale-grace window** is `expired` instead of run (a long-late revert is wrong to fire).

**Sweeper `ScheduledPipelineExpiryService`** (Infrastructure `Commands.Jobs`, `BackgroundService`, auto-registered by the hosted-worker scan): a **5-second** clock-driven tick calling `FireDueAsync` on a fresh DI scope (cross-tenant, mirroring `RedemptionTimerExpiryService`). The first tick after boot **is** the startup sweep — tasks that came due during downtime fire now (or expire if stale).

**Surfaces.** Pipeline action **`schedule_pipeline`** (`ICommandAction`, category `flow`): params `pipeline` (name → tenant id; typed failure if unknown), `delay_seconds` (required, clamped), optional template-resolved `dedupe_key`; it captures the current context variables so the deferred run keeps its context. Script SDK capability **`schedule.pipeline`** (low tier, side-effecting, `custom_code` feature-gated): `nnz.api.schedule.pipeline(pipelineName, delaySeconds, variables?, dedupeKey?) → boolean`, routed through the host bridge to `ScheduleByNameAsync` for the caller's tenant — this is what a Voice-Swap script calls to schedule its own revert.

**REST management.** There are **no** `pipelines/scheduled` routes on `PipelinesController`. The only HTTP surface for scheduled tasks is the platform-admin job queue on `AdminController` (`api/v{version:apiVersion}/admin`, policy `IamPermissionKeys.IamManage`):

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `admin/jobs` | every `ScheduledPipelineTask` row across every tenant, newest first, paged (the real queue the sweeper works, never a fabricated one) |
| POST | `admin/jobs/{taskId:guid}/retry` | retries one failed (expired) job: schedules a brand-new due-now row for the same pipeline (the failed row is never mutated); refused when the job succeeded, is still queued, was cancelled, or its pipeline is gone; audited with the acting operator; rate-limited as security-sensitive |

Channel-scoped scheduling is reached through the `schedule_pipeline` action and the `schedule.pipeline` script capability above, and cancellation by dedupe key through `IScheduledPipelineService.CancelByDedupeKeyAsync`; the design's channel-level `GET`/`DELETE …/pipelines/scheduled` routes were not built. The DTO the service returns is `ScheduledPipelineTaskDto` (`Application/Commands/Dtos/ScheduledPipelineTaskDtos.cs`).

---

## 11. Built-in reply catalogue — every reply editable per channel

Owner directive (2026-09-29): the streamer edits **every** reply of **every** built-in from the dashboard, with the
same control as the system overlay editor — see the default, edit, preview, reset to default. No built-in reply
stays hardcoded. This section **supersedes** the single `CustomResponseTemplate` / `responseTemplate` field of the original
design (one override per built-in, applied to one reply only).

**Slot model.** A *reply slot* is one reply case of one built-in, keyed `(builtinKey, slot)` — e.g. `(sr, added)`,
`(sr, duplicate)`, `(sr, providerunavailable)`. Every sentence a built-in (or the chat handler on its behalf) can
send is a slot: success lines, usage lines, refusals, and service errors alike. Each slot is declared once in
`ToneTemplateCatalog` with:
- all five tones (`informative` first — its first line is the shipped default wording);
- its **declared variables** (the values the built-in seeds at runtime, e.g. `user`, `track.name`, `requested.by`).

Constants live in `BuiltinResponseSlots` (one nested class per built-in). Services never return reply text: they
return a typed error code plus data (`Result.ErrorCode` + `ErrorDetail`/typed value); the built-in maps the code to
its slot and composes the reply. Replies not owned by one command use a group key: `system` (the chat handler's own
lines, e.g. `system/permissiondenied`) and `botstatus`.

**Precedence (the only rule — `IBuiltinResponseComposer`).**
1. the channel's override for exactly this `(builtinKey, slot)`;
2. the platform admin's reply for this slot (`PlatformBuiltinReplyDefault`, already per slot — no migration);
3. a random variation of the channel's personality tone for this slot (Informative when the tone has none);
4. the built-in's neutral fallback (a safety net only — every slot ships an Informative line).

The composer looks the channel override up itself (`IChannelBuiltinReplyOverrides`, read from the channel
registry cache). Built-ins no longer thread an override through `BuiltinCommandContext`, so no slot can be
forgotten and no override leaks into a second slot (the old `!lurk` bug).

**Storage.** `ChannelBuiltinCommand.OverridesJson` is
`{ "responses": { "<slot>": "<template>" }, "speakWithTts": true, "cooldownSeconds": 30, "minPermissionLevel": 2 }` on the row for `builtinKey`
(one codec, `BuiltinOverridesJson`; the blob keeps the permission floor as the ladder's numeric value, and `BuiltinCommandDto` converts it to a rung name at the edge; a field is absent when it inherits the default) (group keys
`system`/`botstatus` get a row too; such rows are ignored by the enable/disable list, which walks the code catalogue).
The legacy `{ "responseTemplate": "..." }` is still read: it applies to the built-in's **legacy slots** (the slot
the old field fed — `uptime/live`, `song/playing`, `queue/list`, `sr/added`, `commands/list`, `lurk/lurking` +
`lurk/notlurking`, `accountage/age`, and the other built-ins that passed it) unless that slot has its own entry. The
next write to the row rewrites the legacy value into `responses` and drops `responseTemplate` — the owner's existing
override survives unchanged. No schema migration: the column already exists and the platform table is already per
slot.

**Locked slots.** A reserved data-rights built-in (`IsReserved`, gdpr-crypto.md §9) cannot be overridden by a
channel — its rows show in the catalogue as `isLocked: true`, read-only. The one exception is `forgetme/done`, the
streamer-stylable "clean slate" sentence §9 part 1 names; the mandatory re-entry clause is appended in code and is
never part of any template.

**Validation.** A write runs `ITemplateHelperValidator.Validate(template, TemplateHelperContext.Command,
declaredVariables)`: every `{placeholder}` must be a declared slot variable or a registered template helper valid in
the Command context. Unknown → `VALIDATION_FAILED` naming the placeholder (with a did-you-mean). Max 500 chars (the
platform column's limit). Blank = reset.

**API** (`BuiltinsController`, route base `api/v1/channels/{channelId}/builtins`; replaces `PUT {builtinKey}/response`,
and `BuiltinCommandDto.responseOverride` is removed):

| Method | Route suffix | Body | Response | Gate-2 action |
|---|---|---|---|---|
| GET | `/replies` | — | `StatusResponseDto<IReadOnlyList<BuiltinReplyGroupDto>>` | `commands:read` |
| PUT | `/{builtinKey}/replies/{slot}` | `{ template: string }` | `StatusResponseDto<BuiltinReplyDto>` | `commands:write` |
| DELETE | `/{builtinKey}/replies/{slot}` | — | `StatusResponseDto<BuiltinReplyDto>` (the reset row) | `commands:write` |

```csharp
sealed record BuiltinReplyGroupDto(string BuiltinKey, IReadOnlyList<string> CommandKeys, IReadOnlyList<BuiltinReplyDto> Replies);
sealed record BuiltinReplyDto(string BuiltinKey, string Slot, LocalizedText Label, LocalizedText Description,
    string EffectiveTemplate, string Source /* channel|platform|tone */, string DefaultTemplate,
    IReadOnlyList<string> ToneVariations, IReadOnlyList<BuiltinReplyVariableDto> Variables,
    bool IsOverridden, bool IsLocked);
sealed record BuiltinReplyVariableDto(string Name, LocalizedText Description, string SampleValue);
```
- `DefaultTemplate` = what the slot says with no channel override (platform reply, else the tone's first line).
- `ToneVariations` = the lines the channel's personality picks from (empty when a platform reply replaces them).
- `EffectiveTemplate` = the channel override when set, else `DefaultTemplate`. `Source` names the winning layer.
- Label/description keys: `builtin.reply.<builtinKey>.<slot>.label|.description`; variable keys
  `builtin.reply.var.<name>`. All in `server/i18n/schema-i18n-keys.manifest.json`, en + nl in the dashboard's
  `strings_builtin_replies*.xml`. `SampleValue` is a fixed English-neutral example (a name, a number, a track) the
  preview substitutes; it is data, not UI copy.

**Dashboard.** Commands page → each built-in row gets **Edit replies** (outline). It opens the built-in's reply
editor: one card per slot showing the label, description, the effective text, and an *Overridden* badge when the
channel has its own text. **Edit** (ghost) expands the slot inline: a textarea, the variable chips (click inserts
`{name}` at the end), a live preview that fills the variables with their sample values, then **Save** (the one
primary action of that slot) and **Cancel** (ghost). **Reset to default** is a destructive-ghost action shown only
when overridden; it asks for confirmation, then DELETEs and shows the default again. Locked slots render read-only
with a lock note. Reply groups that are not commands (`system`, `botstatus`) are reachable from a **Bot replies**
row at the end of the built-in list.
