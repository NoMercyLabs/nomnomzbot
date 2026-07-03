# Interface Specification — `broadcaster-liveops` subsystem

**Status:** Directly implementable. Owner codes from this first-try. Closes gap **A — Broadcaster live-ops
writes** (`_GAP-AUDIT.md`): the **write side** of the broadcaster's live-ops surface — Polls, Predictions,
Raids, Ads/Commercials, Stream Schedule, Stream Markers, Clips — now has an owner.

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
  (libs); `2026-06-16-decisions-pending-confirmation.md` (resolved cross-cutting defaults).
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

## 3. Service interfaces

All interfaces in `NomNomzBot.Application/Contracts/LiveOps/`; implementations in
`NomNomzBot.Infrastructure/Services/LiveOps/`. Every fallible op returns `Result`/`Result<T>`, `CancellationToken
ct = default` last. Implementations use repositories + `IUnitOfWork`; the Helix legs go through the named
`twitch-helix.md` sub-client methods (**listed as reconciliation items in §8**). A poll/prediction start that
both mutates Twitch and persists the F.12/F.13 mirror commits the local row in the **same `IUnitOfWork`** after
the Helix call succeeds (Twitch is the slow leg; never persist an active-state row whose Twitch create failed).

Error codes reuse the shared set (`NOT_FOUND`, `VALIDATION_FAILED`, `FORBIDDEN`, `FEATURE_DISABLED`,
`RATE_LIMITED`) plus the live-ops codes named below (`NO_ACTIVE_POLL`, `NO_ACTIVE_PREDICTION`,
`POLL_ALREADY_ACTIVE`, `PREDICTION_ALREADY_ACTIVE`, `NOT_LIVE`, `TWITCH_COOLDOWN`).

### 3.1 `IPollService` — poll create / end / read (stateful)

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public interface IPollService
{
    // Create. Progressive scope channel:manage:polls. Helix POST /polls -> store TwitchPollId, persist F.12
    // (Status=active) in the same IUnitOfWork, emit PollManagedEvent(Action="started"). POLL_ALREADY_ACTIVE if
    // a Status=active row already exists for the tenant. VALIDATION_FAILED on out-of-bounds title/choices/duration.
    Task<Result<PollDto>> StartAsync(Guid broadcasterId, Guid? actorUserId, StartPollRequest request, string source, CancellationToken ct = default);

    // End the active poll. Helix PATCH /polls (status TERMINATED=show result | ARCHIVED=hide). Updates F.12
    // (Status, EndedAt), emits PollManagedEvent(Action="ended"). NO_ACTIVE_POLL if none active.
    Task<Result<PollDto>> EndAsync(Guid broadcasterId, Guid? actorUserId, EndPollRequest request, string source, CancellationToken ct = default);

    // Read the current active poll mirror (F.12, Status=active) for the dashboard. NO_ACTIVE_POLL if none.
    Task<Result<PollDto>> GetActiveAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

### 3.2 `IPredictionService` — prediction create / lock / resolve / cancel / read (stateful)

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public interface IPredictionService
{
    // Create. Progressive scope channel:manage:predictions. Helix POST /predictions -> store TwitchPredictionId,
    // persist F.13 (Status=active), emit PredictionManagedEvent(Action="started"). PREDICTION_ALREADY_ACTIVE if a
    // non-terminal row already exists. 2..10 outcomes required.
    Task<Result<PredictionDto>> StartAsync(Guid broadcasterId, Guid? actorUserId, StartPredictionRequest request, string source, CancellationToken ct = default);

    // Lock the active prediction (no more bets). Helix PATCH /predictions status=LOCKED. Updates F.13
    // (Status=locked, LockedAt), emits PredictionManagedEvent(Action="locked"). NO_ACTIVE_PREDICTION if none.
    Task<Result<PredictionDto>> LockAsync(Guid broadcasterId, Guid? actorUserId, string source, CancellationToken ct = default);

    // Resolve to a winning outcome. Helix PATCH /predictions status=RESOLVED winning_outcome_id. Updates F.13
    // (Status=resolved, WinningOutcomeId, EndedAt), emits PredictionManagedEvent(Action="resolved"). The outcome
    // id must belong to the active prediction -> VALIDATION_FAILED otherwise.
    Task<Result<PredictionDto>> ResolveAsync(Guid broadcasterId, Guid? actorUserId, string winningOutcomeTwitchId, string source, CancellationToken ct = default);

    // Cancel (refund all points). Helix PATCH /predictions status=CANCELED. Updates F.13 (Status=canceled,
    // EndedAt), emits PredictionManagedEvent(Action="canceled").
    Task<Result<PredictionDto>> CancelAsync(Guid broadcasterId, Guid? actorUserId, string source, CancellationToken ct = default);

    Task<Result<PredictionDto>> GetActiveAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

### 3.3 `IRaidService` — start / cancel (stateless)

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public interface IRaidService
{
    // Start an outbound raid. Progressive scope channel:manage:raids. Helix POST /raids (from_broadcaster_id=
    // tenant, to_broadcaster_id=target). Resolves the target login/id via ITwitchChannelsApi.GetUserAsync. Emits
    // RaidStartedEvent. NOT_FOUND if target unknown; TWITCH_COOLDOWN if Twitch rejects (90s outbound cooldown).
    Task<Result<RaidDto>> StartAsync(Guid broadcasterId, Guid? actorUserId, StartRaidRequest request, string source, CancellationToken ct = default);

    // Cancel a pending (not-yet-executed) raid. Helix DELETE /raids. Emits RaidCanceledEvent. NOT_FOUND if no
    // pending raid (Twitch 404).
    Task<Result> CancelAsync(Guid broadcasterId, Guid? actorUserId, string source, CancellationToken ct = default);
}
```

### 3.4 `IAdScheduleService` — start commercial / snooze / read schedule (stateless)

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public interface IAdScheduleService
{
    // Start a commercial. Progressive scope channel:edit:commercial. Helix POST /channels/commercial
    // (length 30..180s, snapped to Twitch's allowed steps). Emits CommercialStartedEvent(Action="commercial_started").
    // NOT_LIVE if the channel is offline (Twitch rejects); TWITCH_COOLDOWN echoes Twitch's retry_after.
    Task<Result<CommercialResultDto>> StartCommercialAsync(Guid broadcasterId, Guid? actorUserId, int lengthSeconds, string source, CancellationToken ct = default);

    // Snooze the next scheduled ad. Progressive scope channel:manage:ads. Helix POST /channels/ads/schedule/snooze.
    // Emits CommercialStartedEvent(Action="ad_snoozed"). VALIDATION_FAILED if no snoozes remain (Twitch).
    Task<Result<AdScheduleDto>> SnoozeNextAdAsync(Guid broadcasterId, Guid? actorUserId, string source, CancellationToken ct = default);

    // Read the ad schedule (next ad, snooze count, duration). Progressive scope channel:read:ads. Helix GET
    // /channels/ads. Read-only, no event. Read-through (no local row).
    Task<Result<AdScheduleDto>> GetScheduleAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

### 3.5 `IStreamScheduleService` — segment CRUD + vacation (Helix read-through, stateless)

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public interface IStreamScheduleService
{
    // Read the broadcast schedule (read-through; no local table). Helix GET /schedule. No scope beyond the read
    // (public-ish). No event.
    Task<Result<StreamScheduleDto>> GetScheduleAsync(Guid broadcasterId, ScheduleQuery query, CancellationToken ct = default);

    // Create a schedule segment. Progressive scope channel:manage:schedule. Helix POST /schedule/segment. Emits
    // ScheduleSegmentChangedEvent(Action="segment_created"). VALIDATION_FAILED on bad start/duration/timezone.
    Task<Result<ScheduleSegmentDto>> CreateSegmentAsync(Guid broadcasterId, Guid? actorUserId, CreateScheduleSegmentRequest request, string source, CancellationToken ct = default);

    // Update a segment. Helix PATCH /schedule/segment. Emits ScheduleSegmentChangedEvent(Action="segment_updated").
    Task<Result<ScheduleSegmentDto>> UpdateSegmentAsync(Guid broadcasterId, Guid? actorUserId, string twitchSegmentId, UpdateScheduleSegmentRequest request, string source, CancellationToken ct = default);

    // Delete a segment. Helix DELETE /schedule/segment. Emits ScheduleSegmentChangedEvent(Action="segment_deleted").
    Task<Result> DeleteSegmentAsync(Guid broadcasterId, Guid? actorUserId, string twitchSegmentId, string source, CancellationToken ct = default);

    // Set or clear vacation. Helix PATCH /schedule/settings. Emits ScheduleSegmentChangedEvent(Action=
    // "vacation_set"|"vacation_cleared").
    Task<Result> SetVacationAsync(Guid broadcasterId, Guid? actorUserId, SetVacationRequest request, string source, CancellationToken ct = default);
}
```

### 3.6 `IStreamMarkerService` — create marker (stateless)

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public interface IStreamMarkerService
{
    // Create a stream marker at the current live position. Progressive scope channel:manage:broadcast. Helix
    // POST /streams/markers. Emits StreamMarkerCreatedEvent. NOT_LIVE if the channel is offline (Twitch rejects —
    // markers require a live VOD).
    Task<Result<StreamMarkerDto>> CreateAsync(Guid broadcasterId, Guid? actorUserId, string? description, string source, CancellationToken ct = default);
}
```

### 3.7 `IClipService` — create clip (stateless)

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public interface IClipService
{
    // Create a clip of the live stream. Progressive scope clips:edit. Helix POST /clips. Returns the clip id +
    // edit URL (Twitch processes the clip asynchronously). Emits ClipCreatedEvent. NOT_LIVE if offline.
    Task<Result<ClipDto>> CreateAsync(Guid broadcasterId, Guid? actorUserId, bool hasDelay, string source, CancellationToken ct = default);
}
```

### 3.8 `ILiveOpsReconciler` — read-side reconcile (consumes twitch-eventsub events)

A scoped bus handler subscribed to the **ingested** read-side events (owned by `twitch-eventsub.md`). It folds
Twitch's authoritative tallies/lifecycle back onto the F.12/F.13 mirror so the dashboard mirror converges with
reality. It has a live side effect (mutating the mirror), so it is a bus handler, not a journal projection.

```csharp
namespace NomNomzBot.Application.Contracts.LiveOps;

public interface ILiveOpsReconciler
{
    // On PollBeganEvent (ingested): if no local F.12 row matches TwitchPollId, insert a mirror stub (the poll was
    // started outside the bot — e.g. Twitch UI); else fill TwitchChoiceIds. Idempotent on (BroadcasterId, TwitchPollId).
    Task<Result> OnPollBeganAsync(PollBeganEvent @event, CancellationToken ct = default);

    // On PollEndedEvent (ingested): finalize F.12 (Status, EndedAt, WinningChoiceTitle, per-choice Votes).
    Task<Result> OnPollEndedAsync(PollEndedEvent @event, CancellationToken ct = default);

    // On PredictionBeganEvent / PredictionLockedEvent / PredictionEndedEvent: upsert/finalize F.13. Idempotent on
    // (BroadcasterId, TwitchPredictionId).
    Task<Result> OnPredictionChangedAsync(IPredictionLifecycleEvent @event, CancellationToken ct = default);
}
```

`PollBeganEvent`/`PollEndedEvent`/`PredictionBeganEvent`/`PredictionLockedEvent`/`PredictionEndedEvent` are the
**existing** `twitch-eventsub.md` §2 records (referenced, not redeclared). `IPredictionLifecycleEvent` is a thin
marker interface this subsystem may introduce in `Application/Contracts/LiveOps/` for the reconciler overload
**only if** the three prediction events do not already share a base — **noted as a cross-spec check** in §8.

---

## 4. DTOs / contracts

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

New `LiveOpsController` family under `NomNomzBot.Api/Controllers/V1/`, all `[ApiVersion("1.0")]`, inherit
`BaseController`, `[Authorize]`, route through `ResultResponse`. Tenant `{broadcasterId:guid}` resolves via tenant
middleware + channel-access check. The controller passes the JWT `actorUserId` and `source="dashboard"`/`"api"`
to the services.

**Role gate.** All write routes are **management plane**. **Gate 1** = `[Authorize]` + tenant resolution (entry;
any management level ≥ Moderator). **Gate 2** = `IActionAuthorizationService.AuthorizeActionAsync(userId,
broadcasterId, actionKey)` enforces the per-route floor (403 `FORBIDDEN` below it) — each key seeded global in
`ActionDefinitions` (§5.1 deltas). Effective level = `MAX(community standing, management role, active permit
grant)`.

> One controller class **per resource** keeps the file single-responsibility; they share the
> `api/v{version:apiVersion}/channels/{broadcasterId:guid}/liveops` route prefix.

### `PollsController` — `.../liveops/polls`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/active` | — | `StatusResponseDto<PollDto>` | management / Moderator · `liveops:poll:read` |
| POST | `/` | `StartPollRequest` | `StatusResponseDto<PollDto>` (201) | management / Editor · `liveops:poll:manage` |
| POST | `/end` | `EndPollRequest` | `StatusResponseDto<PollDto>` | management / Editor · `liveops:poll:manage` |

### `PredictionsController` — `.../liveops/predictions`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/active` | — | `StatusResponseDto<PredictionDto>` | management / Moderator · `liveops:prediction:read` |
| POST | `/` | `StartPredictionRequest` | `StatusResponseDto<PredictionDto>` (201) | management / Editor · `liveops:prediction:manage` |
| POST | `/lock` | — | `StatusResponseDto<PredictionDto>` | management / Editor · `liveops:prediction:manage` |
| POST | `/resolve` | `{ string winningOutcomeId }` | `StatusResponseDto<PredictionDto>` | management / Editor · `liveops:prediction:manage` |
| POST | `/cancel` | — | `StatusResponseDto<PredictionDto>` | management / Editor · `liveops:prediction:manage` |

### `RaidsController` — `.../liveops/raids`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| POST | `/` | `StartRaidRequest` | `StatusResponseDto<RaidDto>` | management / Editor · `liveops:raid:start` |
| DELETE | `/` | — | `StatusResponseDto<object>` | management / Editor · `liveops:raid:start` |

### `AdsController` — `.../liveops/ads`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/schedule` | — | `StatusResponseDto<AdScheduleDto>` | management / Moderator · `liveops:ads:read` |
| POST | `/commercial` | `{ int lengthSeconds }` | `StatusResponseDto<CommercialResultDto>` | management / Editor · `liveops:ads:run` |
| POST | `/snooze` | — | `StatusResponseDto<AdScheduleDto>` | management / Editor · `liveops:ads:run` |

### `ScheduleController` — `.../liveops/schedule`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | `ScheduleQuery` | `StatusResponseDto<StreamScheduleDto>` | management / Moderator · `liveops:schedule:read` |
| POST | `/segments` | `CreateScheduleSegmentRequest` | `StatusResponseDto<ScheduleSegmentDto>` (201) | management / Editor · `liveops:schedule:write` |
| PATCH | `/segments/{twitchSegmentId}` | `UpdateScheduleSegmentRequest` | `StatusResponseDto<ScheduleSegmentDto>` | management / Editor · `liveops:schedule:write` |
| DELETE | `/segments/{twitchSegmentId}` | — | `StatusResponseDto<object>` | management / Editor · `liveops:schedule:write` |
| PUT | `/vacation` | `SetVacationRequest` | `StatusResponseDto<object>` | management / Editor · `liveops:schedule:write` |

> **False-friend disambiguation (load-bearing).** The existing `roles-permissions.md` key **`stream:schedule:write`**
> (Editor(30)/Low) gates the **internal metadata queue** owned by `stream-admin.md` — the
> `ScheduledStreamChanges` table that defers *title/game/tags* edits to a future instant. It has **nothing** to do
> with Twitch's `/schedule` broadcast calendar. This subsystem therefore uses a **new, distinct** key
> **`liveops:schedule:write`** for the Helix stream-schedule segment/vacation writes. The two must not be merged:
> one defers internal channel metadata, the other publishes the public broadcast calendar.

### `MarkersController` — `.../liveops/markers`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| POST | `/` | `{ string? description }` | `StatusResponseDto<StreamMarkerDto>` (201) | management / Moderator · `liveops:marker:create` |

### `ClipsController` — `.../liveops/clips`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| POST | `/` | `{ bool hasDelay }` | `StatusResponseDto<ClipDto>` (201) | management / Moderator · `liveops:clip:create` |

Markers and clips floor at **Moderator** (low-risk capture actions a mod legitimately triggers mid-stream); polls,
predictions, raids, ads, and schedule writes floor at **Editor** (channel-identity broadcast operations — Twitch's
own default scopes these to people who run the stream, not chat moderators).

### 5.1 `ActionDefinitions` seed deltas (RETURNED — add to `roles-permissions.md` §7.1)

This subsystem requires the following **new** `[GLOBAL, seed]` `ActionDefinitions` rows. Per the task rules they
are **returned as deltas**, not edited into `roles-permissions.md` here. `Plane = management`, seeded idempotently
on the `ActionKey` unique index; `Id = Guid.CreateVersion7()` at seed time; `DefaultLevel = FloorLevel`;
`FloorTier` uses the `DangerTier` enum (`Low`/`Tos`/`Critical`). `liveops:schedule:write` is **distinct** from the
existing `stream:schedule:write` (see disambiguation above) and must be added as a separate row.

(Full table restated in the final summary under `ACTION KEYS NEEDED`.)

---

## 6. Pipeline actions

New file `NomNomzBot.Infrastructure/Pipeline/Actions/LiveOpsActions.cs`, each implementing the **single canonical
`ICommandAction`** (`commands-pipelines.md` §3.13: `string Type`/`Category`/`Description` +
`Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)`). These let a command / pipeline /
reward-redemption **trigger** a live-ops action automatically. They read params from `context.Parameters` (the
step's resolved `ConfigJson`), take the tenant from `context.BroadcasterId` (already `Guid`), and call the matching
§3 service with `source="pipeline"` and `actorUserId=null` (event triggers run with **broadcaster authority**, so
the management floor passes — the channel itself is the actor). `Category = "Live Ops"`. Registered **transient**
(stateless) in the `ICommandAction` block.

Security: live-ops actions are **broadcast-effecting**. The engine's tainted-variable rule
(`commands-pipelines.md` §4.4) applies — a `{{webhook.*}}`/attacker-authored token feeding a raid target or
poll/prediction text is fail-closed by the engine before `ExecuteAsync` runs.

| Action class | `Type` | `context.Parameters` keys | Behavior |
|---|---|---|---|
| `StartPollAction` | `start_poll` | `title`, `choices` (CSV or list), `duration_seconds`, `channel_points_per_vote?` | Builds `StartPollRequest` (template-substituted), calls `IPollService.StartAsync`. `ActionResult.Ok("poll started")`; `Fail` on Helix/validation error or `POLL_ALREADY_ACTIVE`. Requires `channel:manage:polls` (progressive) — missing scope → `Fail("feature_disabled")`. |
| `StartPredictionAction` | `start_prediction` | `title`, `outcomes` (CSV/list, 2..10), `window_seconds` | Calls `IPredictionService.StartAsync`. Same shape. Requires `channel:manage:predictions`. |
| `StartCommercialAction` | `start_commercial` | `length_seconds` (30..180) | Calls `IAdScheduleService.StartCommercialAsync`. `Fail` with `NOT_LIVE`/`TWITCH_COOLDOWN` surfaced as the error message. Requires `channel:edit:commercial`. |
| `CreateMarkerAction` | `create_marker` | `description?` | Calls `IStreamMarkerService.CreateAsync`. `Fail("not_live")` when offline. Requires `channel:manage:broadcast`. |
| `StartRaidAction` | `start_raid` | `target_login` | Calls `IRaidService.StartAsync`. Tainted-token guarded (raid target is a security-sensitive param). `Fail` on `NOT_FOUND`/`TWITCH_COOLDOWN`. Requires `channel:manage:raids`. |

> No pipeline action for prediction lock/resolve/cancel, schedule CRUD, ad snooze, or clip create — these are
> human-decision or out-of-band operations, not automation primitives (resolve needs a chosen winner; schedule is
> calendar editing). They remain dashboard/API only. (Clip-on-trigger could be added later behind its own action
> if a use case appears; it is intentionally out of the initial action set — YAGNI.)

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs`, "Application services" block. Services **scoped** (consume
`IApplicationDbContext`/repositories/`IUnitOfWork`/the Helix sub-clients); pipeline actions **transient**; the
reconciler **scoped** (bus handler). Implementations in `NomNomzBot.Infrastructure/Services/LiveOps/`.

```csharp
// Broadcaster live-ops — application services (scoped)
services.AddScoped<IPollService, PollService>();
services.AddScoped<IPredictionService, PredictionService>();
services.AddScoped<IRaidService, RaidService>();
services.AddScoped<IAdScheduleService, AdScheduleService>();
services.AddScoped<IStreamScheduleService, StreamScheduleService>();
services.AddScoped<IStreamMarkerService, StreamMarkerService>();
services.AddScoped<IClipService, ClipService>();

// Read-side reconciler (bus handler subscribed to twitch-eventsub Poll*/Prediction* events)
services.AddScoped<ILiveOpsReconciler, LiveOpsReconciler>();

// Repositories for the two stateful mirror tables (match existing AddScoped<XRepository>() pattern)
services.AddScoped<ActivePollRepository>();
services.AddScoped<ActivePredictionRepository>();

// Pipeline actions (transient, stateless) — alongside the existing ICommandAction registrations
services.AddTransient<ICommandAction, StartPollAction>();
services.AddTransient<ICommandAction, StartPredictionAction>();
services.AddTransient<ICommandAction, StartCommercialAction>();
services.AddTransient<ICommandAction, CreateMarkerAction>();
services.AddTransient<ICommandAction, StartRaidAction>();
```

New `IApplicationDbContext` `DbSet`s: `DbSet<ActivePoll> ActivePolls`, `DbSet<ActivePrediction> ActivePredictions`.
The `Poll*`/`Prediction*` ingested-event → `ILiveOpsReconciler` wiring rides the existing bus-handler registration
convention (`twitch-eventsub.md` dispatch / `commands-pipelines.md` event-response dispatch); no bespoke hosted
service.

---

## 8. Dependencies & cross-spec reconciliation

### 8.1 Consumed interfaces

| Need | Interface (owner) | Use here |
|---|---|---|
| Helix mutations (polls/predictions/raids/ads/schedule/markers/clips) | `ITwitchHelixClient` sub-clients (`twitch-helix.md`) | every service's Helix leg — **but the methods do not exist yet** (§8.2) |
| Target user resolution (raid login→id) | `ITwitchChannelsApi.GetUserAsync` (`twitch-helix.md`, **exists**) | `IRaidService.StartAsync` |
| Read-side ingest events to reconcile against | `PollBeganEvent`/`PollEndedEvent`/`PredictionBeganEvent`/`PredictionLockedEvent`/`PredictionEndedEvent` + `INotificationDispatcher` (`twitch-eventsub.md` §2/§3.4, **exist**) | `ILiveOpsReconciler` |
| Per-action authz gate | `IActionAuthorizationService` (`roles-permissions.md`) | every write controller route |
| Progressive scope grant | `IScopeGrantService` (`identity-auth.md`) | feature-enable scope requests |
| Journaling / bus | `IEventJournal`/`IEventBus` (`event-store.md`) | §2 events |
| Pipeline engine | `ICommandAction`/`ActionContext` (`commands-pipelines.md` §3.13/§4.4) | §6 actions |
| Clock | `TimeProvider` | `EndsAt`/`LocksAt` computation |

No **new** third-party dependency. App JSON for the `[VC:JSON]` mirror columns uses **Newtonsoft.Json** via
hand-rolled converters (already in the stack).

### 8.2 MISSING `twitch-helix.md` sub-client methods (reconciliation items — do NOT edit twitch-helix.md here)

The current `twitch-helix.md` sub-clients (`ITwitchChannelsApi`, `ITwitchModerationApi`,
`ITwitchSubscriptionsApi`) expose **none** of the live-ops mutations. This spec codes against the following
methods, which **must be added to `twitch-helix.md`** (suggested home: a **new `ITwitchLiveOpsApi` sub-client** on
`ITwitchHelixClient`, since none of the three existing sub-clients is a natural fit). Each maps to a Helix
endpoint + scope and should be idempotency-guarded per the existing `twitch-helix.md` §3.3 mutation convention.

| Proposed method | Helix endpoint | Scope |
|---|---|---|
| `CreatePollAsync` | `POST /polls` | `channel:manage:polls` |
| `EndPollAsync` | `PATCH /polls` | `channel:manage:polls` |
| `CreatePredictionAsync` | `POST /predictions` | `channel:manage:predictions` |
| `UpdatePredictionAsync` (lock/resolve/cancel) | `PATCH /predictions` | `channel:manage:predictions` |
| `StartRaidAsync` | `POST /raids` | `channel:manage:raids` |
| `CancelRaidAsync` | `DELETE /raids` | `channel:manage:raids` |
| `StartCommercialAsync` | `POST /channels/commercial` | `channel:edit:commercial` |
| `SnoozeNextAdAsync` | `POST /channels/ads/schedule/snooze` | `channel:manage:ads` |
| `GetAdScheduleAsync` | `GET /channels/ads` | `channel:read:ads` |
| `GetScheduleAsync` | `GET /schedule` | (read) |
| `CreateScheduleSegmentAsync` | `POST /schedule/segment` | `channel:manage:schedule` |
| `UpdateScheduleSegmentAsync` | `PATCH /schedule/segment` | `channel:manage:schedule` |
| `DeleteScheduleSegmentAsync` | `DELETE /schedule/segment` | `channel:manage:schedule` |
| `UpdateScheduleSettingsAsync` (vacation) | `PATCH /schedule/settings` | `channel:manage:schedule` |
| `CreateStreamMarkerAsync` | `POST /streams/markers` | `channel:manage:broadcast` |
| `CreateClipAsync` | `POST /clips` | `clips:edit` |

> The wire DTOs (snake_case Helix request/response models) for these endpoints are generated/committed under
> `twitch-helix.md`'s codegen convention (§4) — out of scope for this spec; listed here only so the missing surface
> is unambiguous.

### 8.3 Other cross-spec references to reconcile

- **`roles-permissions.md` §7.1** — add the 14 `ActionDefinitions` seed rows in §5.1 / the `ACTION KEYS NEEDED`
  table, **including the new `liveops:schedule:write`** (distinct from `stream:schedule:write` — do not merge).
- **`twitch-eventsub.md` §2** — `IPredictionLifecycleEvent` (§3.8): confirm whether
  `PredictionBeganEvent`/`PredictionLockedEvent`/`PredictionEndedEvent` already share a common base/marker; if not,
  the reconciler uses three explicit overloads instead of the marker interface (no change required in
  `twitch-eventsub.md` either way — this is a local convenience).
- **Locked schema (Domain F)** — add **F.12 `ActivePolls`** and **F.13 `ActivePredictions`** (the `SCHEMA DELTAS`
  table). One clean migration at the end of the rebuild, per the schema-not-locked convention.
- **`identity-auth.md`** — register the six progressive Twitch scopes (`channel:manage:polls`,
  `channel:manage:predictions`, `channel:manage:raids`, `channel:edit:commercial`+`channel:manage:ads`+
  `channel:read:ads`, `channel:manage:schedule`, `clips:edit`; `channel:manage:broadcast` is already in the base
  streamer grant per `CLAUDE.md`) in the progressive-scope catalog, requested on feature-enable.

---

## 9. Decisions (resolved)

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

5. **`liveops:schedule:write` is a new key, not the existing `stream:schedule:write`.** The existing key gates
   `stream-admin.md`'s internal *title/game/tags* deferral queue (`ScheduledStreamChanges`); this one gates Twitch's
   public `/schedule` calendar. Same FloorLevel/Tier, different meaning — kept separate so a channel that delegates
   metadata-deferral does not inadvertently delegate calendar publishing (and vice-versa).

6. **Floors: capture actions at Moderator, broadcast-identity actions at Editor.** Markers/clips are low-risk
   capture a moderator legitimately triggers; polls/predictions/raids/ads/schedule are broadcast operations Twitch's
   own model scopes to people who run the stream — floored at Editor. Reads (active poll/prediction, ad schedule,
   calendar) floor at Moderator. Broadcasters may raise any floor via `ChannelActionOverride`, never lower it below
   the seeded `FloorLevel`.

7. **All Twitch scopes are progressive.** None is requested at login; each is requested the first time its feature
   is used (the create/start call, or feature-enable in the dashboard). Absent the scope, the management endpoint
   returns `FEATURE_DISABLED` with a re-auth prompt; the pipeline action returns `Fail("feature_disabled")` without
   killing the run.
