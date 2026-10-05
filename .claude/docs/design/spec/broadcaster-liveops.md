# Interface Specification — `broadcaster-liveops` subsystem

**Status:** Directly implementable. Owner codes from this first-try. Closes gap **A — Broadcaster live-ops
writes** (`_GAP-AUDIT.md`): the **write side** of the broadcaster's live-ops surface — Polls, Predictions,
Raids, Ads/Commercials, Stream Schedule, Stream Markers, Clips — now has an owner.

**As-built (2026-09-30):** the write side ships as **ONE controller, `LiveOpsController`**, that calls the Helix
category sub-clients (`ITwitchPollsApi`, `ITwitchPredictionsApi`, `ITwitchRaidsApi`, `ITwitchAdsApi`,
`ITwitchClipsApi`, `ITwitchScheduleApi`, `ITwitchStreamsApi`) **directly**. Helix is the only store. **Not built:**
the service layer (`IPollService` … `IClipService`, §3), the F.12/F.13 mirror tables + `ILiveOpsReconciler` (§1,
§3), the write-side `*ManagedEvent` domain events (§2), the `Application/Contracts/LiveOps` DTOs (§4), and the
pipeline actions `start_poll`, `start_prediction`, `start_commercial`, `create_marker` (§6). The shipped surface is
the route table in §5; `start_raid` is the one built pipeline action (§6). Action keys are spelled `live-ops:*`.

**Scope (owns the WRITE side; the read side is owned elsewhere):** this subsystem lets the streamer **act** on
their channel's live-ops controls via Helix mutations. The corresponding **read-side ingest** —
`PollBeganEvent`/`PollEndedEvent`, `PredictionBeganEvent`/`PredictionLockedEvent`/`PredictionEndedEvent`,
`RaidEvent` — is **owned by `twitch-eventsub.md` §2** (those events ride in from `channel.poll.*` /
`channel.prediction.*` / `channel.raid` notifications). This spec **consumes** those ingested events to
reconcile the small amount of stateful local mirror it keeps (active poll / active prediction), and emits its
own **distinct** management/outcome events (`PollManagedEvent`, …) for the action a streamer took. It never
re-declares or re-publishes the read-side events.

> **The poll/prediction split, stated once (load-bearing).** Twitch is the source of truth for the live result
> tallies (vote/point counts) — those arrive on EventSub and are journaled by `twitch-eventsub.md`. This
> subsystem keeps only a **thin local mirror of the *active* poll/prediction** (the one the streamer just
> created) so the dashboard can render "a poll is live, here are its choices, it ends at T" the instant the
> create call returns, **before** the first EventSub progress frame. The mirror is reconciled (and finalized)
> from the ingested `Poll*`/`Prediction*` events; it is **not** an authoritative tally store. Raids, ads,
> schedule, markers, and clips are **fire-and-forget Helix mutations with no local active-state row** (schedule
> is Helix read-through — see §9.4).

**Binding conventions (apply to every type below):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10;
file-scoped namespaces; `Nullable` enabled; async all the way (never `.Result`/`.Wait`); `Result<T>` over
exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR,
no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` +
`[Route("api/v{version:apiVersion}/...")]` inherit `BaseController`, return through `ResultResponse`; surrogate
PKs `Guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is **`Guid`** (FK→`Channels.Id`); Twitch ids
are indexed `string` attribute columns, never keys; soft-delete (`IsDeleted`/`DeletedAt`) global filter where
noted; `[VC:JSON]` = hand-rolled `ValueConverter<T,string>`+`ValueComparer` over **Newtonsoft.Json**; `[VC:enum]`
columns store the short string token; the single injected clock is `TimeProvider` (never `DateTimeOffset.UtcNow`).
Helix calls go through the `twitch-helix.md` `ITwitchHelixClient` sub-clients — this subsystem **never** builds
raw Helix requests.

## Grounding

- **Source of truth:** locked schema `2026-06-16-database-schema.md` (Domain F — this spec adds **F.12
  `ActivePolls`** / **F.13 `ActivePredictions`** as schema deltas, §1); `2026-06-16-stack-and-dependencies.md`
  (libs); `2026-06-16-decisions-resolved.md` (resolved cross-cutting defaults).
- **Read-side ingest (consumed, not owned):** `twitch-eventsub.md` §2 (`PollBeganEvent`, `PollEndedEvent`,
  `PredictionBeganEvent`, `PredictionLockedEvent`, `PredictionEndedEvent`, `RaidEvent`) + §3.4
  `INotificationDispatcher`.
- **Helix client (consumed, not edited):** `twitch-helix.md` §3 sub-clients. **This spec needs Helix methods
  that the current `twitch-helix.md` sub-clients do NOT expose** (polls, predictions, raids, ads, schedule,
  markers, clips) — every one is listed in §8 as a **reconciliation item for `twitch-helix.md`**. Per the task
  rules this spec does **not** edit `twitch-helix.md`; it codes against the named sub-client methods on the
  assumption they will be added there.
- **Authz (consumed, not owned):** `roles-permissions.md` — Gate-1 (`[Authorize]` + tenant resolution) + Gate-2
  `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey, ct)`. The new
  `ActionDefinitions` rows this subsystem requires are **returned as deltas** (§5.1), not edited into
  `roles-permissions.md` here.
- **Progressive scopes (consumed):** `identity-auth.md` `IScopeGrantService` — each feature's Twitch scope is
  requested **only when the feature is first used**, never at login (§8).
- **Pipeline engine (consumed):** `commands-pipelines.md` §3.13 canonical `ICommandAction` + §4.4
  `ActionContext`/`ActionResult`.

---

## 1. Entities

> **NOT BUILT.** There is no `ActivePolls` / `ActivePredictions` entity, table, migration or `DbSet`. The live-poll and live-prediction mirror below is the deferred design; as built the dashboard reads the current poll/prediction from Helix (`GET …/live-ops/polls`, `…/predictions`). Schedule read-through (below) is as built.

This subsystem **owns two new, small stateful tables** (active poll / active prediction mirror) added to Domain
F of the locked schema, and declares the §2 domain events. Everything else (raids/ads/schedule/markers/clips) is
**stateless** — a Helix mutation with no local row. Schedule is **Helix read-through** (§9.4): no table.

> **Schema delta (this spec) — returned, not edited into the schema file.** Two new Domain-F tables. The
> concrete delta rows are restated in the final summary (`SCHEMA DELTAS`); the EF entity classes live in
> `NomNomzBot.Domain/Entities/`.

### F.12 `ActivePolls` `[soft-delete, tenant]` — the live-poll mirror

One row per **currently-active or just-finalized** poll the bot created. At most one `status=active` row per
tenant (Twitch allows one active poll at a time). Keyed locally by `Guid`; `TwitchPollId` is the indexed Twitch
id used to reconcile against the ingested `Poll*` events.

| Column | Type | Key/Null/Index | Notes |
|---|---|---|---|
| `Id` | guid | PK | Surrogate (UUIDv7). |
| `BroadcasterId` | guid | FK→Channels, Index | Tenant. |
| `TwitchPollId` | string(50) | Index | Twitch poll id (returned by `POST /polls`); set on create. |
| `Title` | string(60) | — | Poll question (Twitch max 60). |
| `Choices` | text | — | **[VC:JSON]** `List<ActivePollChoice>` (`{ Title, TwitchChoiceId?, Votes }`); `Votes`/`TwitchChoiceId` filled by reconcile. |
| `DurationSeconds` | int | — | 15–1800 (Twitch bounds). |
| `ChannelPointsVotingEnabled` | bool | — | Mirror of the create request. |
| `ChannelPointsPerVote` | int | Null | When channel-point voting is on. |
| `Status` | string(20) | Index | `active`\|`completed`\|`terminated`\|`archived` [VC:enum] — mirrors Twitch poll status. |
| `StartedByUserId` | guid | FK→Users, Null | The management user who started it (null = pipeline/system). |
| `StartedAt` | timestamp | Index | From the create response. |
| `EndsAt` | timestamp | — | `StartedAt + DurationSeconds`; drives the dashboard countdown before EventSub. |
| `EndedAt` | timestamp | Null | Set on reconcile from `PollEndedEvent` or an explicit End call. |
| `WinningChoiceTitle` | string(60) | Null | Set on finalize from `PollEndedEvent`. |
| `CreatedAt/UpdatedAt/DeletedAt` | timestamp | DeletedAt Null | |

**Unique** filtered `(BroadcasterId)` WHERE `Status='active' AND DeletedAt IS NULL` (at most one active poll per
tenant). **Index** `(BroadcasterId, TwitchPollId)` (reconcile lookup).

### F.13 `ActivePredictions` `[soft-delete, tenant]` — the live-prediction mirror

One row per currently-active or just-resolved prediction the bot created. At most one non-terminal
(`active`/`locked`) row per tenant.

| Column | Type | Key/Null/Index | Notes |
|---|---|---|---|
| `Id` | guid | PK | Surrogate (UUIDv7). |
| `BroadcasterId` | guid | FK→Channels, Index | Tenant. |
| `TwitchPredictionId` | string(50) | Index | Twitch prediction id (returned by `POST /predictions`). |
| `Title` | string(45) | — | Prediction question (Twitch max 45). |
| `Outcomes` | text | — | **[VC:JSON]** `List<ActivePredictionOutcome>` (`{ Title, TwitchOutcomeId?, Color?, ChannelPoints, Users }`); 2–10 outcomes; tallies filled by reconcile. |
| `PredictionWindowSeconds` | int | — | 30–1800 (Twitch bounds). |
| `Status` | string(20) | Index | `active`\|`locked`\|`resolved`\|`canceled` [VC:enum] — mirrors Twitch. |
| `WinningOutcomeId` | string(50) | Null | Twitch outcome id chosen on resolve. |
| `StartedByUserId` | guid | FK→Users, Null | Management user (null = pipeline/system). |
| `StartedAt` | timestamp | Index | From the create response. |
| `LocksAt` | timestamp | — | `StartedAt + PredictionWindowSeconds`; drives the dashboard countdown. |
| `LockedAt` | timestamp | Null | Set on Lock or reconcile from `PredictionLockedEvent`. |
| `EndedAt` | timestamp | Null | Set on Resolve/Cancel or reconcile from `PredictionEndedEvent`. |
| `CreatedAt/UpdatedAt/DeletedAt` | timestamp | DeletedAt Null | |

**Unique** filtered `(BroadcasterId)` WHERE `Status IN ('active','locked') AND DeletedAt IS NULL`. **Index**
`(BroadcasterId, TwitchPredictionId)`.

> POCO shapes for the `[VC:JSON]` columns (live beside the entities in `NomNomzBot.Domain/Entities/`):
> ```csharp
> public sealed record ActivePollChoice(string Title, string? TwitchChoiceId, int Votes);
> public sealed record ActivePredictionOutcome(
>     string Title, string? TwitchOutcomeId, string? Color, long ChannelPoints, int Users);
> ```

**References (owned elsewhere, never mutated here):** `Channels`/`Users` (`identity-auth.md`), `EventJournal`
(`event-store.md`), `ActionDefinitions`/`ChannelActionOverrides` (`roles-permissions.md`), `IdempotencyKey`
(`twitch-helix.md` §1 — the Helix mutations below are idempotency-guarded inside the sub-clients).

---

## 2. Domain events

> **NOT BUILT.** None of the `*ManagedEvent` / `RaidStartedEvent` / `RaidCanceledEvent` / `CommercialStartedEvent` / `ScheduleSegmentChangedEvent` / `StreamMarkerCreatedEvent` / `ClipCreatedEvent` types exists; `LiveOpsController` publishes no domain event. The only built outbound signal is `RaidSentEvent` (`NomNomzBot.Domain/Stream/Events/`, fields `ToUserId` + `ToDisplayName`), published by the `start_raid` pipeline action (§6) — the built counterpart of `RaidStartedEvent`. The block below is the deferred design.

In `NomNomzBot.Domain/Events/LiveOps/`, each `sealed record : DomainEventBase` (canonical base,
`platform-conventions.md` §2.0 — supplies `Guid EventId` UUIDv7, `Guid BroadcasterId`, `DateTimeOffset
OccurredAt`; events **MUST NOT** redeclare those). All are **tenant-scoped** — the publisher sets the inherited
`BroadcasterId` to the owning channel. These are the **write-side / outcome** events for actions the streamer
took; they are **distinct** from the read-side `Poll*`/`Prediction*`/`Raid*` events ingested by
`twitch-eventsub.md`, which describe Twitch-originated state. The bus journals them (`event-store.md`).

```csharp
namespace NomNomzBot.Domain.Events.LiveOps;

/// <summary>The bot managed a poll on Twitch (started or explicitly ended via Helix). DISTINCT from the
/// ingested PollBeganEvent/PollEndedEvent (which describe Twitch-side lifecycle + live tallies). This carries
/// the management action + actor.</summary>
public sealed record PollManagedEvent : DomainEventBase
{
    public required Guid PollId { get; init; }                 // internal F.12 id
    public required string TwitchPollId { get; init; }
    public required string Action { get; init; }              // "started" | "ended"
    public required string Title { get; init; }
    public Guid? ActorUserId { get; init; }                   // null = pipeline/system
    public required string Source { get; init; }              // "dashboard" | "api" | "pipeline"
}

/// <summary>The bot managed a prediction on Twitch (started, locked, resolved, or canceled via Helix). DISTINCT
/// from the ingested Prediction* events.</summary>
public sealed record PredictionManagedEvent : DomainEventBase
{
    public required Guid PredictionId { get; init; }           // internal F.13 id
    public required string TwitchPredictionId { get; init; }
    public required string Action { get; init; }              // "started" | "locked" | "resolved" | "canceled"
    public required string Title { get; init; }
    public string? WinningOutcomeId { get; init; }            // set only when Action == "resolved"
    public Guid? ActorUserId { get; init; }
    public required string Source { get; init; }
}

/// <summary>The bot started a raid to another channel (Helix POST /raids). DISTINCT from the ingested RaidEvent
/// (which fires for an INCOMING raid). Carries the outbound target.</summary>
public sealed record RaidStartedEvent : DomainEventBase
{
    public required string TargetTwitchUserId { get; init; }
    public required string TargetDisplayName { get; init; }   // [PII-scrub]
    public required bool IsMature { get; init; }              // Twitch raid response flag
    public Guid? ActorUserId { get; init; }
    public required string Source { get; init; }              // "dashboard" | "api" | "pipeline"
}

/// <summary>The bot canceled a pending outbound raid (Helix DELETE /raids).</summary>
public sealed record RaidCanceledEvent : DomainEventBase
{
    public Guid? ActorUserId { get; init; }
    public required string Source { get; init; }
}

/// <summary>The bot started a commercial / snoozed the next ad (Helix POST /channels/commercial |
/// POST /channels/ads/schedule/snooze).</summary>
public sealed record CommercialStartedEvent : DomainEventBase
{
    public required string Action { get; init; }             // "commercial_started" | "ad_snoozed"
    public int? LengthSeconds { get; init; }                  // set for commercial_started
    public int? RetryAfterSeconds { get; init; }             // Twitch cooldown echoed back, when present
    public Guid? ActorUserId { get; init; }
    public required string Source { get; init; }
}

/// <summary>A stream-schedule segment was created/updated/deleted, or vacation toggled (Helix /schedule/*).
/// DISTINCT from any read-side schedule mirror — schedule is Helix read-through (§9.4), so this is purely the
/// audit/activity-feed signal for the write the streamer made.</summary>
public sealed record ScheduleSegmentChangedEvent : DomainEventBase
{
    public required string Action { get; init; }             // "segment_created" | "segment_updated" | "segment_deleted" | "vacation_set" | "vacation_cleared"
    public string? TwitchSegmentId { get; init; }            // null for vacation_* actions
    public Guid? ActorUserId { get; init; }
    public required string Source { get; init; }
}

/// <summary>A stream marker was created (Helix POST /streams/markers).</summary>
public sealed record StreamMarkerCreatedEvent : DomainEventBase
{
    public required string TwitchMarkerId { get; init; }
    public required int PositionSeconds { get; init; }       // offset into the live stream
    public string? Description { get; init; }
    public Guid? ActorUserId { get; init; }
    public required string Source { get; init; }
}

/// <summary>A clip was created (Helix POST /clips). Returns the edit URL; the clip is async-processed by Twitch.
/// </summary>
public sealed record ClipCreatedEvent : DomainEventBase
{
    public required string TwitchClipId { get; init; }
    public required string EditUrl { get; init; }
    public Guid? ActorUserId { get; init; }
    public required string Source { get; init; }
}
```

---

## 3. Service layer — as built: none (the controller calls the Helix sub-clients)

There is no `NomNomzBot.Application/Contracts/LiveOps/` folder and no `IPollService`, `IPredictionService`, `IRaidService`, `IAdScheduleService`, `IStreamScheduleService`, `IStreamMarkerService`, `IClipService` or `ILiveOpsReconciler`. The single `LiveOpsController` (§5) injects the Helix category sub-clients and `IChannelService` and calls Helix directly:

| Injected | Used for |
|---|---|
| `ITwitchPollsApi` | list, create, end a poll |
| `ITwitchPredictionsApi` | list, create, end (lock / resolve / cancel) a prediction |
| `ITwitchRaidsApi` | start, cancel a raid |
| `ITwitchAdsApi` | ad schedule, start a commercial, snooze the next ad |
| `ITwitchClipsApi` | create a clip |
| `ITwitchScheduleApi` | schedule read, iCalendar, segment create/update/delete, vacation settings |
| `ITwitchStreamsApi` | create a stream marker |
| `IChannelService` | authorize the public iCalendar subscribe feed (per-channel `OverlayToken`) |

Consequences of "no service layer" (decided as built, not deferred):

- **Helix is the only store.** There is no local active-poll / active-prediction row, so the controller never persists; a poll or prediction started in the Twitch UI is visible the same as one started here.
- **Errors are Twitch's.** A failed Helix call returns through `TwitchResultResponse` carrying the Twitch error code and message; the live-ops error codes of the earlier draft (`NO_ACTIVE_POLL`, `POLL_ALREADY_ACTIVE`, `NOT_LIVE`, `TWITCH_COOLDOWN`, …) do not exist. Request validation beyond a parsable channel id is Twitch's.
- **No `actorUserId` / `source` audit trail** and no journaled write-side event: a live-ops write is recorded only by Helix and by the request log.
- **Scopes are progressive** and are checked at the Helix transport/sub-client layer (§8.2, decision 9.7).

If a service layer, the F.12/F.13 mirror, or the write-side events are ever built, they are new work items against §1, §2 and §4 above, not a description of current behavior.

---

## 4. DTOs / contracts

> **NOT BUILT as a contracts folder.** There is no `NomNomzBot.Application/Contracts/LiveOps/`. As built, request bodies are the nested `record`s on `LiveOpsController` (listed in §5) plus `CreateScheduleSegmentRequest` / `UpdateScheduleSegmentRequest` from `NomNomzBot.Application.Contracts.Twitch`; responses are the Helix client records (`TwitchPoll`, `TwitchPrediction`, `TwitchRaid`, `TwitchCommercial`, `TwitchAdSchedule`, `TwitchAdSnooze`, `TwitchClipStub`, `TwitchSchedule`, `TwitchStreamMarker`). The records below are the deferred service-layer design.

`public sealed record`, in `NomNomzBot.Application/Contracts/LiveOps/`, serialized **Newtonsoft.Json**, PascalCase.

### Requests

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public sealed record StartPollRequest(
    string Title, IReadOnlyList<string> Choices, int DurationSeconds,
    bool ChannelPointsVotingEnabled = false, int? ChannelPointsPerVote = null);
// Validation: 1<=Title<=60; 2..5 choices, each 1..25 chars; 15<=DurationSeconds<=1800;
// ChannelPointsPerVote 1..1000000 required when ChannelPointsVotingEnabled.

public sealed record EndPollRequest(bool ShowResult = true);  // true=TERMINATED (visible) | false=ARCHIVED (hidden)

public sealed record StartPredictionRequest(
    string Title, IReadOnlyList<PredictionOutcomeInput> Outcomes, int PredictionWindowSeconds);
// Validation: 1<=Title<=45; 2..10 outcomes, each Title 1..25; 30<=PredictionWindowSeconds<=1800.
public sealed record PredictionOutcomeInput(string Title);

public sealed record StartRaidRequest(string TargetLogin);   // resolved to to_broadcaster_id via Helix

public sealed record CreateScheduleSegmentRequest(
    DateTime StartTime, string Timezone, bool IsRecurring, int DurationMinutes,
    string? CategoryId = null, string? Title = null);

public sealed record UpdateScheduleSegmentRequest(
    DateTime? StartTime = null, string? Timezone = null, int? DurationMinutes = null,
    string? CategoryId = null, string? Title = null, bool? IsCanceled = null);

public sealed record SetVacationRequest(bool Enabled, DateTime? StartTime = null, DateTime? EndTime = null, string? Timezone = null);
// Enabled=false clears vacation; when true, StartTime/EndTime/Timezone required.

public sealed record ScheduleQuery(DateTime? StartTime = null, string? Id = null, int First = 20);
```

### Responses

```csharp
public sealed record PollDto(
    Guid Id, string TwitchPollId, string Title, IReadOnlyList<PollChoiceDto> Choices,
    int DurationSeconds, bool ChannelPointsVotingEnabled, int? ChannelPointsPerVote,
    string Status, DateTime StartedAt, DateTime EndsAt, DateTime? EndedAt, string? WinningChoiceTitle);
public sealed record PollChoiceDto(string Title, string? TwitchChoiceId, int Votes);

public sealed record PredictionDto(
    Guid Id, string TwitchPredictionId, string Title, IReadOnlyList<PredictionOutcomeDto> Outcomes,
    int PredictionWindowSeconds, string Status, string? WinningOutcomeId,
    DateTime StartedAt, DateTime LocksAt, DateTime? LockedAt, DateTime? EndedAt);
public sealed record PredictionOutcomeDto(string Title, string? TwitchOutcomeId, string? Color, long ChannelPoints, int Users);

public sealed record RaidDto(string TargetTwitchUserId, string TargetDisplayName, bool IsMature, DateTime CreatedAt);

public sealed record CommercialResultDto(int LengthSeconds, string Message, int? RetryAfterSeconds);

public sealed record AdScheduleDto(
    DateTime? NextAdAt, int LengthSeconds, DateTime? LastAdAt, int PrerollFreeTimeSeconds,
    int SnoozeCount, DateTime? SnoozeRefreshAt);

public sealed record StreamScheduleDto(
    string BroadcasterId, IReadOnlyList<ScheduleSegmentDto> Segments, VacationDto? Vacation);
public sealed record ScheduleSegmentDto(
    string TwitchSegmentId, DateTime StartTime, DateTime? EndTime, string? Title,
    string? CategoryId, string? CategoryName, bool IsRecurring, bool IsCanceled);
public sealed record VacationDto(DateTime StartTime, DateTime EndTime);

public sealed record StreamMarkerDto(string TwitchMarkerId, int PositionSeconds, string? Description, DateTime CreatedAt);

public sealed record ClipDto(string TwitchClipId, string EditUrl);
```

---

## 5. Controller endpoints

**One controller:** `LiveOpsController` in `NomNomzBot.Api/Controllers/V1/`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/channels/{channelId}/live-ops")]`, `[Authorize]`, `[Tags("LiveOps")]`, inherits `BaseController`. (An earlier draft split this into one controller per resource under `…/liveops/…`; that was never built.) Each action parses `{channelId}` with `Guid.TryParse` — an unparsable id is a `400` `Invalid channel id.` — and passes it to the Helix sub-client as the broadcaster id. Success returns `StatusResponseDto<T>` (creates return `201`); a Helix failure returns through `TwitchResultResponse` with the Twitch error.

**Role gate.** All routes are **management plane**. **Gate-1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). **Gate-2** = the `[RequireAction("<key>")]` attribute → `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor (403 `FORBIDDEN` below it). Keys are `live-ops:<resource>:<verb>` — the hyphenated `live-ops` spelling — each seeded global in `ActionDefinitions` (`ActionDefinitionSeeder`, §5.1). Effective level = `MAX(community standing, management role, active permit grant)`.

### 5.1 Route table (as built)

Routes are relative to `/api/v1/channels/{channelId}/live-ops`. Request bodies are the nested `record`s on the controller.

| Verb | Route | Request | Response | Floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/polls` | — | `StatusResponseDto<IReadOnlyList<TwitchPoll>>` (first page, 10) | Moderator · `live-ops:polls:read` |
| POST | `/polls` | `CreatePollDto(Title, Choices, DurationSeconds, ChannelPointsVotingEnabled = false, ChannelPointsPerVote = 0)` | `StatusResponseDto<TwitchPoll>` (201) | Moderator · `live-ops:polls:write` |
| PATCH | `/polls/{pollId}/end` | `EndPollDto(Status)` — `TERMINATED` (end, stay visible) \| `ARCHIVED` (end, hide) | `StatusResponseDto<TwitchPoll>` | Moderator · `live-ops:polls:write` |
| GET | `/predictions` | — | `StatusResponseDto<IReadOnlyList<TwitchPrediction>>` (first page, 10) | Moderator · `live-ops:predictions:read` |
| POST | `/predictions` | `CreatePredictionDto(Title, Outcomes, PredictionWindowSeconds)` (`Outcomes` = list of titles) | `StatusResponseDto<TwitchPrediction>` (201) | Editor · `live-ops:predictions:write` |
| PATCH | `/predictions/{predictionId}/end` | `EndPredictionDto(Status, WinningOutcomeId?)` — `LOCKED` \| `RESOLVED` (needs `WinningOutcomeId`) \| `CANCELED` | `StatusResponseDto<TwitchPrediction>` | Editor · `live-ops:predictions:write` |
| POST | `/raids` | `StartRaidDto(TargetTwitchBroadcasterId)` (numeric id; a login is resolved only by the `start_raid` pipeline action, §6) | `StatusResponseDto<TwitchRaid>` (201) | Editor · `live-ops:raids:write` |
| DELETE | `/raids` | — | 204 | Editor · `live-ops:raids:write` |
| GET | `/raids/scoring-rules` | — | `StatusResponseDto<RaidScoringRules>` (this channel's own rules; `Neutral` when none were saved) | Moderator · `live-ops:raids:read` |
| PUT | `/raids/scoring-rules` | `RaidScoringRules` (the full set; a rule that breaks a limit is refused with `VALIDATION_FAILED` and nothing is stored) | `StatusResponseDto<RaidScoringRules>` | Editor · `live-ops:raids:write` |
| GET | `/ads/schedule` | — | `StatusResponseDto<TwitchAdSchedule>` | Moderator · `live-ops:ads:read` |
| POST | `/ads/commercial` | `StartCommercialDto(LengthSeconds)` (30/60/90/120/150/180) | `StatusResponseDto<TwitchCommercial>` | Editor · `live-ops:ads:write` |
| POST | `/ads/snooze` | — | `StatusResponseDto<TwitchAdSnooze>` | Editor · `live-ops:ads:write` |
| POST | `/clips` | — (created without delay) | `StatusResponseDto<TwitchClipStub>` (201) | Moderator · `live-ops:clips:write` |
| GET | `/schedule` | `?after=&pageSize=` (default page 100) | `StatusResponseDto<TwitchSchedule>` | Moderator · `live-ops:schedule:read` |
| GET | `/schedule/icalendar` | — | `text/calendar` (RFC 5545 snapshot) | Moderator · `live-ops:schedule:read` |
| GET | `/schedule/icalendar/subscribe` | `?token=` | `text/calendar` (public webcal feed) | **Anonymous** — authorized by the channel's `OverlayToken` query param, which must belong to the channel in the route |
| POST | `/schedule/segment` | `CreateScheduleSegmentRequest` | `StatusResponseDto<TwitchSchedule>` | Editor · `live-ops:schedule:write` |
| PATCH | `/schedule/segment/{segmentId}` | `UpdateScheduleSegmentRequest` | `StatusResponseDto<TwitchSchedule>` | Editor · `live-ops:schedule:write` |
| DELETE | `/schedule/segment/{segmentId}` | — | `StatusResponseDto<bool>` | Editor · `live-ops:schedule:write` |
| PUT | `/schedule/settings` | `UpdateScheduleSettingsDto(IsVacationEnabled?, VacationStartTime?, VacationEndTime?, Timezone?)` — the vacation toggle + window | `StatusResponseDto<bool>` | Editor · `live-ops:schedule:write` |
| POST | `/markers` | `CreateMarkerDto(Description?)` | `StatusResponseDto<TwitchStreamMarker>` (201) | Moderator · `live-ops:marker:create` |

Differences from the earlier per-resource draft: there is no `GET /polls/active`, `POST /polls/end`, `/predictions/active`, `/predictions/lock|resolve|cancel` (lock, resolve and cancel are all `PATCH /predictions/{id}/end` with a `Status`), no separate `/vacation` route (it is `PUT /schedule/settings`), and no `ScheduleQuery` type (the schedule read takes `?after=&pageSize=`).

> **False-friend disambiguation (load-bearing).** The existing `roles-permissions.md` key **`stream:schedule:write`**
> (Editor(30)/Low) gates the **internal metadata queue** owned by `stream-admin.md` — the
> `ScheduledStreamChanges` table that defers *title/game/tags* edits to a future instant. It has **nothing** to do
> with Twitch's `/schedule` broadcast calendar. This subsystem therefore uses a **new, distinct** key
> **`live-ops:schedule:write`** for the Helix stream-schedule segment/vacation writes. The two must not be merged:
> one defers internal channel metadata, the other publishes the public broadcast calendar.

### 5.2 `ActionDefinitions` seed (as built — `ActionDefinitionSeeder`, mirrored in `roles-permissions.md` §7.1)

`Plane = management`, seeded idempotently on the `ActionKey` unique index; `DefaultLevel = FloorLevel`; all are `Low` tier and grantable (`grant: true`). `FloorLevel` is the lowest a broadcaster may set via `ChannelActionOverride`.

| Action key | Floor | Used by |
|---|---|---|
| `live-ops:polls:read` | Moderator | `GET /polls` |
| `live-ops:polls:write` | Moderator (owner policy: moderators may run polls by default) | `POST /polls`, `PATCH /polls/{pollId}/end` |
| `live-ops:predictions:read` | Moderator | `GET /predictions` |
| `live-ops:predictions:write` | Editor | `POST /predictions`, `PATCH /predictions/{predictionId}/end` |
| `live-ops:raids:read` | Moderator | `GET /raids/scoring-rules` |
| `live-ops:raids:write` | Editor | `POST /raids`, `DELETE /raids`, `PUT /raids/scoring-rules` |
| `live-ops:ads:read` | Moderator | `GET /ads/schedule` |
| `live-ops:ads:write` | Editor | `POST /ads/commercial`, `POST /ads/snooze` |
| `live-ops:schedule:read` | Moderator | `GET /schedule`, `GET /schedule/icalendar` |
| `live-ops:schedule:write` | Editor | segment create/update/delete, `PUT /schedule/settings` |
| `live-ops:marker:create` | Moderator | `POST /markers` |
| `live-ops:clips:write` | Moderator | `POST /clips` |

The dashboard live-events hub maps its `liveops` event class to the read key `live-ops:polls:read` (`DashboardEventClasses`).

---

## 6. Pipeline actions

**Built: one action, `start_raid`.** `StartRaidAction` lives in `NomNomzBot.Infrastructure/Stream/PipelineActions/StartRaidAction.cs` (beside `ShoutoutAction` and `WaitUntilRaidFiresAction`, the Stream domain's pipeline actions) — **not** in a `LiveOpsActions.cs` file. It implements the single canonical `ICommandAction` (`NomNomzBot.Application/Abstractions/Pipeline/ICommandAction.cs`), whose real shape is:

```csharp
public interface ICommandAction
{
    string ActionType { get; }                              // "start_raid" (the discriminator; not `Type`)
    LocalizedText Category { get; }                         // a LocalizedText KEY — start_raid: "pipeline.category.stream"
    LocalizedText Description { get; }                      // a LocalizedText KEY, unique per action
    IReadOnlyList<PipelineActionFieldDescriptor> Fields => [];   // typed step-form schema (number / TwitchUser picker / …)
    bool ResolvesOwnTemplates => false;                     // true = the action resolves its own templated fields
    Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action);
}
```

Parameters are read from the step's `ActionDefinition` (`action.GetString("target")`, `action.GetInt("delay_seconds")`); the tenant is `ctx.BroadcasterId`; results are `ActionResult.Failure(...)` / success. Actions are auto-registered by the `AddImplementationsOf<ICommandAction>` assembly scan (no wiring edit). Event triggers run with **broadcaster authority**, so the Gate-2 floor passes — the channel itself is the actor.

| Action class | `ActionType` | Parameters (`Fields`) | Behavior |
|---|---|---|---|
| `StartRaidAction` | `start_raid` | `target` (required, `TwitchUser` picker — a login **or numeric user id**; a leading `@` is tolerated; a `{variable}` reference such as `{args.1}` is resolved from `ctx.Variables`), `delay_seconds` (0-90, wait AFTER the raid has fired), `raid_window_seconds` (30-300, the tunable Twitch server-side auto-fire window; default 116) | Resolves a login to an id via `ITwitchUsersApi.GetUsersByLoginsAsync`; best-effort live check on the target (a **confirmed-offline** target fails the step, a failed check never blocks); calls `ITwitchRaidsApi.StartRaidAsync(ctx.BroadcasterId, targetId)` immediately (never delayed). Twitch `409 Conflict` (a raid already in flight) is **tolerated as success**. Missing `channel:manage:raids` scope → a re-grant failure message (the Helix client already raised `TwitchHelixReauthRequiredEvent`). On success publishes `RaidSentEvent` and records `raid.fires_at_utc_ticks` in `ctx.Variables` for the companion `wait_until_raid_fires` action, then honors `delay_seconds`. |

`wait_until_raid_fires` (`WaitUntilRaidFiresAction`) is the companion step that waits out the remainder of the raid window; it is part of the raid flow, not a live-ops write.

**Not built (deferred design):** `start_poll`, `start_prediction`, `start_commercial`, `create_marker`. They would call the `ITwitchPollsApi` / `ITwitchPredictionsApi` / `ITwitchAdsApi` / `ITwitchStreamsApi` sub-clients directly (there is no service layer, §3) and follow the same `ICommandAction` shape and progressive-scope handling as `start_raid`. Until then a poll, prediction, commercial or marker is dashboard/API-only.

Security: live-ops actions are **broadcast-effecting**. The engine's tainted-variable rule (`commands-pipelines.md` §4.4) applies — a `{{webhook.*}}`/attacker-authored token feeding a raid target is fail-closed by the engine before `ExecuteAsync` runs.

> No pipeline action is planned for prediction lock/resolve/cancel, schedule CRUD, ad snooze, or clip create — these are human-decision or out-of-band operations, not automation primitives (resolve needs a chosen winner; schedule is calendar editing). They remain dashboard/API only.

---

## 7. DI registration

**As built there is no live-ops-specific registration.** `LiveOpsController` is discovered as an ordinary MVC controller and constructor-injects services that already exist:

- the Helix category sub-clients `ITwitchPollsApi`, `ITwitchPredictionsApi`, `ITwitchRaidsApi`, `ITwitchAdsApi`, `ITwitchClipsApi`, `ITwitchScheduleApi`, `ITwitchStreamsApi` — registered by the Twitch integration (`twitch-helix.md`);
- `IChannelService` — registered by the `I<X>Service` convention scan.

`StartRaidAction` (§6) is picked up by the existing `services.AddImplementationsOf<ICommandAction>(...)` scan in `NomNomzBot.Infrastructure/DependencyInjection.cs`; nothing is added by hand.

**Not registered (not built):** `IPollService`, `IPredictionService`, `IRaidService`, `IAdScheduleService`, `IStreamScheduleService`, `IStreamMarkerService`, `IClipService`, `ILiveOpsReconciler`, `ActivePollRepository`, `ActivePredictionRepository`, the `start_poll` / `start_prediction` / `start_commercial` / `create_marker` actions, and the `DbSet<ActivePoll>` / `DbSet<ActivePrediction>` on `IApplicationDbContext`. No `Poll*`/`Prediction*` → reconciler wiring exists.

---

## 8. Dependencies & cross-spec reconciliation

### 8.1 Consumed interfaces

| Need | Interface (owner) | Use here |
|---|---|---|
| Helix mutations (polls/predictions/raids/ads/schedule/markers/clips) | `ITwitchHelixClient` sub-clients (`twitch-helix.md`) — `Polls`/`Predictions`/`Raids`/`Ads`/`Schedule`/`Streams`/`Clips` | every `LiveOpsController` action (**shipped** — §8.2 maps each call) |
| Target user resolution (raid login→id) | `ITwitchUsersApi.GetUsersByLoginsAsync` (`twitch-helix.md`, **exists**) | the `start_raid` pipeline action (the controller route takes the numeric id directly) |
| Read-side ingest events to reconcile against | `PollBeganEvent`/`PollEndedEvent`/`PredictionBeganEvent`/`PredictionLockedEvent`/`PredictionEndedEvent` + `INotificationDispatcher` (`twitch-eventsub.md` §2/§3.4, **exist**) | *(not consumed — `ILiveOpsReconciler` is not built)* |
| Per-action authz gate | `IActionAuthorizationService` (`roles-permissions.md`) | every write controller route |
| Progressive scope grant | `IScopeGrantService` (`identity-auth.md`) | feature-enable scope requests |
| Journaling / bus | `IEventJournal`/`IEventBus` (`event-store.md`) | `start_raid` publishes `RaidSentEvent` (§2); the §2 write-side events are not built |
| Pipeline engine | `ICommandAction`/`PipelineExecutionContext` (`commands-pipelines.md` §3.13/§4.4) | §6 actions |
| Clock | `TimeProvider` | `EndsAt`/`LocksAt` computation (deferred mirror design only) |

No **new** third-party dependency. App JSON for the `[VC:JSON]` mirror columns uses **Newtonsoft.Json** via
hand-rolled converters (already in the stack).

### 8.2 `twitch-helix.md` sub-client methods consumed (shipped — mapping table)

Every live-ops mutation this spec codes against is **shipped** on the granular `twitch-helix.md` category
sub-clients (there is no `ITwitchLiveOpsApi` — the coarse bucket was deliberately split into its constituent
category clients, `twitch-helix.md` §3.4a). Each maps to a Helix endpoint + scope:

| Method (sub-client) | Helix endpoint | Scope |
|---|---|---|
| `Polls.CreatePollAsync` | `POST /polls` | `channel:manage:polls` |
| `Polls.EndPollAsync` | `PATCH /polls` | `channel:manage:polls` |
| `Predictions.CreatePredictionAsync` | `POST /predictions` | `channel:manage:predictions` |
| `Predictions.EndPredictionAsync` (lock/resolve/cancel) | `PATCH /predictions` | `channel:manage:predictions` |
| `Raids.StartRaidAsync` | `POST /raids` | `channel:manage:raids` |
| `Raids.CancelRaidAsync` | `DELETE /raids` | `channel:manage:raids` |
| `Ads.StartCommercialAsync` | `POST /channels/commercial` | `channel:edit:commercial` |
| `Ads.SnoozeNextAdAsync` | `POST /channels/ads/schedule/snooze` | `channel:manage:ads` |
| `Ads.GetAdScheduleAsync` | `GET /channels/ads` | `channel:read:ads` |
| `Polls.GetPollsAsync` / `Predictions.GetPredictionsAsync` | `GET /polls` / `GET /predictions` | `channel:read:polls` / `channel:read:predictions` |
| `Schedule.GetScheduleAsync` | `GET /schedule` | (read) |
| `Schedule.GetICalendarAsync` | `GET /schedule/icalendar` | (read; served as text/calendar) |
| `Schedule.CreateSegmentAsync` | `POST /schedule/segment` | `channel:manage:schedule` |
| `Schedule.UpdateSegmentAsync` | `PATCH /schedule/segment` | `channel:manage:schedule` |
| `Schedule.DeleteSegmentAsync` | `DELETE /schedule/segment` | `channel:manage:schedule` |
| `Schedule.UpdateScheduleSettingsAsync` (vacation) | `PATCH /schedule/settings` | `channel:manage:schedule` |
| `Streams.CreateStreamMarkerAsync` | `POST /streams/markers` | `channel:manage:broadcast` |
| `Clips.CreateClipAsync` | `POST /clips` | `clips:edit` |

> The request/response records for these endpoints are the **hand-written** per-category records in
> `NomNomzBot.Application/Contracts/Twitch/Dtos/` (`twitch-helix.md` §4 — no codegen); listed here only so
> the consumed surface is unambiguous.

### 8.3 Other cross-spec references to reconcile

- **`roles-permissions.md` §7.1** — **done:** the 11 seeded `live-ops:*` `ActionDefinitions` rows in §5.1 are in that table
  (spelled `live-ops:*`, floors from `ActionDefinitionSeeder`), **including `live-ops:schedule:write`** (distinct from
  `stream:schedule:write` — do not merge).
- **`twitch-eventsub.md` §2** — moot until the reconciler is built: `IPredictionLifecycleEvent` (deferred §3 design): confirm whether
  `PredictionBeganEvent`/`PredictionLockedEvent`/`PredictionEndedEvent` already share a common base/marker; if not,
  the reconciler uses three explicit overloads instead of the marker interface (no change required in
  `twitch-eventsub.md` either way — this is a local convenience).
- **Locked schema (Domain F)** — **F.12 `ActivePolls`** and **F.13 `ActivePredictions`** are NOT BUILT (no entity, no
  migration); they return only if the deferred mirror (§1) is built.
- **`identity-auth.md`** — register the six progressive Twitch scopes (`channel:manage:polls`,
  `channel:manage:predictions`, `channel:manage:raids`, `channel:edit:commercial`+`channel:manage:ads`+
  `channel:read:ads`, `channel:manage:schedule`, `clips:edit`; `channel:manage:broadcast` is already in the base
  streamer grant per `CLAUDE.md`) in the progressive-scope catalog, requested on feature-enable.

---

## 9. Decisions (resolved)

> Decisions 1-3 describe the deferred mirror/event design and are **not built**; decisions 4-7 hold as built (with the `live-ops:*` key spelling and the seeded floors in 5 and 6).

1. **Stateful only for polls/predictions; everything else is stateless.** Polls and predictions have a *live,
   user-visible duration* the dashboard must render the instant the create returns (before EventSub), so they get a
   thin F.12/F.13 mirror. Raids, ads, markers, and clips are point-in-time fire-and-forget mutations with no
   meaningful "active" window the bot must hold — no table, the outcome rides only in the §2 event + journal.

2. **The mirror is NOT the source of truth for tallies.** Twitch owns vote/point counts; they arrive on EventSub
   and are journaled by `twitch-eventsub.md`. `ILiveOpsReconciler` folds those onto F.12/F.13 so the mirror
   converges, but the bot never computes results itself. A poll/prediction started outside the bot (Twitch UI) is
   picked up by the reconciler as a stub at `Poll*Began`, so the dashboard still shows it.

3. **Write-side events are DISTINCT from the ingested read-side events.** `PollManagedEvent` ≠ `PollBeganEvent`;
   `RaidStartedEvent` (outbound, this subsystem) ≠ `RaidEvent` (inbound raid, `twitch-eventsub.md`). The write-side
   events carry the **management action + actor + source**; the read-side events carry **Twitch-originated state +
   tallies**. They serve different consumers (activity feed/audit vs. live overlay/projection) and must not be
   collapsed.

4. **Stream schedule is Helix read-through — no local table.** The broadcast calendar lives on Twitch; the bot is a
   thin CRUD proxy (`IStreamScheduleService` calls Helix directly and returns the live result). Caching the
   calendar locally would add a second source of truth with no benefit — schedule edits are infrequent and the
   dashboard reads on demand. This is the deliberate asymmetry vs. polls/predictions (which need a pre-EventSub
   mirror; the schedule has no such latency-visible window).

5. **`live-ops:schedule:write` is a new key, not the existing `stream:schedule:write`.** The existing key gates
   `stream-admin.md`'s internal *title/game/tags* deferral queue (`ScheduledStreamChanges`); this one gates Twitch's
   public `/schedule` calendar. Same FloorLevel/Tier, different meaning — kept separate so a channel that delegates
   metadata-deferral does not inadvertently delegate calendar publishing (and vice-versa).

6. **Floors come from the seed (`ActionDefinitionSeeder`), spelled `live-ops:*`.** Reads (`polls:read`,
   `predictions:read`, `ads:read`, `schedule:read`) floor at Moderator. Capture and run-the-poll actions —
   `marker:create`, `clips:write` and, by an owner policy (moderators may run polls by default, a
   broadcaster-delegated action on the broadcaster's token), `polls:write` — floor at Moderator. The remaining
   broadcast operations — `predictions:write`, `raids:write`, `ads:write`, `schedule:write` — floor at Editor.
   All eleven are `Low` tier and grantable. Broadcasters may raise any floor via `ChannelActionOverride`, never
   lower it below the seeded `FloorLevel`. (The earlier draft floored polls at Editor and used `liveops:*` keys
   such as `poll:manage`, `raid:start`, `ads:run`; those are superseded.)

7. **All Twitch scopes are progressive.** None is requested at login; each is requested the first time its feature
   is used (the create/start call, or feature-enable in the dashboard). Absent the scope, the management endpoint
   returns `FEATURE_DISABLED` with a re-auth prompt; the pipeline action returns `Fail("feature_disabled")` without
   killing the run. (As built: the Helix sub-client's scope pre-check raises `TwitchHelixReauthRequiredEvent` and returns
   `TwitchErrorCodes.MissingScope`; the controller surfaces it via `TwitchResultResponse`, and `start_raid` maps it to a
   re-grant message.)
