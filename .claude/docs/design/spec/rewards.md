# Rewards — Interface Specification

Implementable spec for the **rewards** subsystem: Twitch channel-point custom rewards (the local
source-of-truth `Rewards`), the redemption fact log (`RewardRedemptions`), the EventSub redemption →
pipeline trigger, and the **managed vs unmanaged** lifecycle that decides whether the bot may create,
update, fulfill, or refund a reward on Twitch. Code from this directly.

Source of truth: locked schema `2026-06-16-database-schema.md` Domain **F.5 Rewards** / **F.6
RewardRedemptions**, plus Domain **O** (`EventJournal`) for the redemption fact stream and Domain **F.7
EventSubSubscriptions** (owned by `twitch-eventsub.md`). Library choices:
`2026-06-16-stack-and-dependencies.md`. This spec conforms to the resolved cross-cutting decisions in
`2026-06-16-decisions-pending-confirmation.md`. Closes gap **B7 / M1** (`_GAP-AUDIT.md`): F.5/F.6 and the
`channel.channel_points_custom_reward_redemption.add` handler now have an owner.

## Binding conventions (every signature below obeys these)

- Namespace root `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way
  (never `.Result`/`.Wait`).
- Fallible operations return `Result` / `Result<T>` (`NomNomzBot.Application.Common.Models`). Never null,
  never throw for expected failure. Error codes reuse `BaseController.ResultResponse`'s known set
  (`NOT_FOUND`, `VALIDATION_FAILED`, `FORBIDDEN`, `ALREADY_EXISTS`, `RATE_LIMITED`, `FEATURE_DISABLED`, …)
  plus the reward-specific codes named in §3 (`UNMANAGED_REWARD`, `REDEMPTION_ALREADY_RESOLVED`,
  `TWITCH_REWARD_CONFLICT`).
- **Tenant key `BroadcasterId` is `Guid`** (locked schema §1.1 — `ITenantScoped.BroadcasterId` widened
  `string`→`Guid` as part of this rebuild). All reward entities are `Guid`-tenanted. Reward/redeemer/user
  ids are internal `Guid`; Twitch ids (`TwitchRewardId`, `TwitchRedemptionId`, `RedeemerTwitchUserId`) are
  indexed `string` attribute columns, never keys.
- Surrogate PKs are `Guid` via `Guid.CreateVersion7()`; the **append-only redemption log uses `long`
  identity** (`RewardRedemptions.Id`).
- Repository + `IUnitOfWork`; no raw `DbContext` in controllers. A reward create/update that both mirrors to
  Twitch and persists locally commits the local row in the **same `IUnitOfWork`** after the Helix call
  succeeds (Twitch is the slow leg; never persist a managed row whose Twitch create failed).
- `[VC:enum]` columns store the short string token (`RewardRedemptions.Status` ∈
  `unfulfilled|fulfilled|canceled`), not the int.
- Responses are `StatusResponseDto<T>` (`NomNomzBot.Api.Models`) or `PaginatedResponse<T>`; list endpoints
  page via `PageRequestDto` → `PaginationParams` and return `PagedList<T>` from services.
- Controllers: `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/...")]`, `[Authorize]`, inherit
  `BaseController`, return through `ResultResponse` / `GetPaginatedResponse`.
- DI via typed interfaces (NO MediatR, no Roslyn). Soft-delete via `SoftDeletableEntity` + global filter on
  `Rewards`; `RewardRedemptions` is append-only (carries `CreatedAt` only, never mutated in place — status
  transitions are recorded as new outcome events folded by the projection).
- The single injected clock is `TimeProvider` (`platform-conventions.md` §3.11); never `DateTimeOffset.UtcNow`.

---

## 1. Entities

This subsystem **owns** two schema tables and declares the reward domain events. It does not redefine tables
owned elsewhere.

- **F.5 `Rewards`** `[soft-delete]` — local source-of-truth for a channel-point reward, mirrored to/from
  Twitch. **Schema delta (this spec):** the `IsPlatform` column is renamed **`IsManaged`** with inverted
  semantics — `IsManaged = true` means *the bot's own `client_id` created this reward on Twitch and controls
  its lifecycle*; `false` means *the reward exists on Twitch but the bot only reacts to it* (the old
  `IsPlatform = true` "Twitch-native, observe-only" case is exactly `IsManaged = false`). No other F.5 column
  changes; `ShouldSkipRequestQueue`, `PipelineId`, `TwitchRewardId`, `Cost`, `IsPaused`, the per-stream/per-user
  caps and cooldown stay as locked.
- **F.6 `RewardRedemptions`** `[APPEND-ONLY]` — one row per channel-point redemption, the redemption fact log.
  Built as an `IProjection` (§3.4) folded from journaled redemption events; FK `EventId` → `EventJournal.EventId`.
  Consumed by `economy.md` (earning rule `Source = redemption`), `analytics.md`
  (`ViewerProfiles.TotalRedemptions`, daily rollups), and leaderboards. (This resolves the `event-store.md`
  §1.1 aside that listed F.6 as "owned by analytics/economy" — **rewards.md is the owner**; analytics/economy
  are read-side consumers.)

References (owned elsewhere, never mutated here): **F.7 `EventSubSubscriptions`** (`twitch-eventsub.md`),
**Pipelines/PipelineSteps** (`commands-pipelines.md`), **EventJournal** (`event-store.md`), **Channels/Users**
(`identity-auth.md`).

### 1.1 Managed vs unmanaged — the load-bearing distinction

A channel-point reward on Twitch can be updated, deleted, and have its redemptions marked
fulfilled/canceled **only by the application `client_id` that created it** (Twitch Helix rule:
`Update Custom Reward`, `Delete Custom Reward`, and `Update Redemption Status` all reject — `403
Forbidden` — a reward not created by the calling client, and `Get Custom Reward Redemption` /
`Update Redemption Status` operate only on the caller's own rewards). This single platform constraint is
why every reward carries `IsManaged`:

| | **Managed** (`IsManaged = true`) | **Unmanaged** (`IsManaged = false`) |
|---|---|---|
| Origin | Bot created it via Helix `POST channel_points/custom_rewards` | Streamer (or another app) created it on Twitch |
| `TwitchRewardId` | Always set (returned by the create call) | Set once observed (matched at redemption / sync) |
| Update / delete / pause on Twitch | ✅ bot may, via Helix | ❌ `UNMANAGED_REWARD` — bot cannot; dashboard edit is local-metadata only (pipeline attach, display) |
| Fulfill / refund a redemption | ✅ bot may, via Helix `Update Redemption Status` | ❌ Helix would `403`; `fulfill_redemption`/`refund_redemption` pipeline actions **no-op with a logged warning** |
| Redemption → pipeline trigger | ✅ | ✅ (reacting is always allowed — EventSub fires for every reward regardless of creator) |
| Required Twitch scope | `channel:manage:redemptions` (+ `channel:read:redemptions`) | `channel:read:redemptions` only |

**Discovery.** Managed rewards are created proactively by the bot, so they always have a local row first.
Unmanaged rewards surface at **redemption time**: EventSub `channel.channel_points_custom_reward_redemption.add`
fires for any reward. If the incoming reward matches no local row by `TwitchRewardId`, the redemption handler
**auto-creates an unmanaged stub** (`IsManaged = false`, `TwitchRewardId` set, `Title`/`Cost` from the payload,
no `PipelineId`) so the redemption has a `RewardId` to FK and the streamer can later attach a pipeline. Stub
creation is idempotent on `(BroadcasterId, TwitchRewardId)`.

**Auto-fulfill.** No new column governs auto-fulfill — the existing F.5 `ShouldSkipRequestQueue` does:
- `ShouldSkipRequestQueue = true` → Twitch marks the redemption **fulfilled immediately** and it never enters
  the broadcaster's request queue; the bot receives it already `fulfilled` and **cannot refund** it. The
  pipeline runs fire-and-forget.
- `ShouldSkipRequestQueue = false` → the redemption arrives `unfulfilled` and sits in Twitch's queue; the
  pipeline (via `fulfill_redemption` / `refund_redemption`) or the dashboard resolves it. If the pipeline
  finishes without resolving and no resolving action ran, the redemption **stays `unfulfilled`** (visible in
  Twitch's native queue for manual handling) — the bot never silently fulfills.

---

## 2. Domain events

Events live in `NomNomzBot.Domain/Events/Rewards/`, implement `IDomainEvent`, inherit the canonical
`DomainEventBase` (`platform-conventions.md` §2.0 — `Guid EventId` UUIDv7, `Guid BroadcasterId`
(`Guid.Empty` = platform-level; always tenant-set here), `DateTimeOffset OccurredAt`; this spec references the
base and never redeclares those members). They are the **bus** contract; `event-store.md` journals them.

`RewardRedeemedEvent` is the **per-topic domain event** that `twitch-eventsub.md`'s `INotificationDispatcher`
(§3.4) maps the `channel.channel_points_custom_reward_redemption.add` topic onto and publishes — declared here
because rewards owns the reward vocabulary; the dispatcher references it. The other three are emitted by this
subsystem's own services.

```csharp
namespace NomNomzBot.Domain.Events;

/// <summary>A viewer redeemed a channel-point reward. The per-topic event the EventSub dispatcher maps
/// `channel.channel_points_custom_reward_redemption.add` to. RewardId is the internal surrogate (auto-stubbed
/// for unmanaged rewards); the Twitch ids are carried for fulfill/refund + dedupe.</summary>
public sealed record RewardRedeemedEvent : DomainEventBase
{
    public required Guid RewardId { get; init; }                  // internal F.5 id (stub created if new)
    public required string TwitchRewardId { get; init; }
    public required string TwitchRedemptionId { get; init; }
    public required string RewardTitle { get; init; }
    public required Guid RedeemerUserId { get; init; }
    public required string RedeemerTwitchUserId { get; init; }    // [PII-hash]
    public required string RedeemerDisplayName { get; init; }     // [PII-scrub]
    public string? UserInput { get; init; }                       // [PII-scrub] free text
    public required int Cost { get; init; }
    public required string Status { get; init; }                  // unfulfilled|fulfilled (skip-queue rewards)
    public required bool IsManaged { get; init; }
    public Guid? StreamId { get; init; }
    public required DateTimeOffset RedeemedAt { get; init; }
}

/// <summary>A managed redemption was marked FULFILLED on Twitch (by a pipeline action, the dashboard, or an
/// API call). Never emitted for unmanaged rewards.</summary>
public sealed record RewardRedemptionFulfilledEvent : DomainEventBase
{
    public required Guid RewardId { get; init; }
    public required string TwitchRedemptionId { get; init; }
    public required string ResolvedBy { get; init; }              // "pipeline" | "dashboard" | "api"
    public Guid? ResolvedByUserId { get; init; }                  // null when pipeline/system
}

/// <summary>A managed redemption was CANCELED on Twitch — the viewer's channel points are returned by Twitch.
/// Never emitted for unmanaged rewards.</summary>
public sealed record RewardRedemptionRefundedEvent : DomainEventBase
{
    public required Guid RewardId { get; init; }
    public required string TwitchRedemptionId { get; init; }
    public required int RefundedCost { get; init; }
    public required string ResolvedBy { get; init; }              // "pipeline" | "dashboard" | "api"
    public Guid? ResolvedByUserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>A managed reward's Twitch configuration changed (published, updated, paused, resumed, or retired).
/// Drives the dashboard activity feed and overlay reward-catalogue cache invalidation. Unmanaged rewards never
/// emit this (their config lives on Twitch, outside the bot).</summary>
public sealed record RewardConfigurationChangedEvent : DomainEventBase
{
    public required Guid RewardId { get; init; }
    public string? TwitchRewardId { get; init; }
    public required string ChangeKind { get; init; }             // published|updated|paused|resumed|retired
}
```

---

## 3. Service interfaces

All interfaces in `NomNomzBot.Application/Contracts/Rewards/`; implementations in
`NomNomzBot.Infrastructure/Services/Rewards/`. Every fallible op returns `Result`/`Result<T>`. Helix calls go
through `ITwitchChannelPointsApi` (`twitch-helix.md`) — this subsystem never builds raw Helix requests.

### 3.1 `IRewardService` — managed reward lifecycle + local metadata

```csharp
namespace NomNomzBot.Application.Contracts.Rewards;

public interface IRewardService
{
    // Create. Managed=true -> Helix POST channel_points/custom_rewards (requires channel:manage:redemptions),
    // store returned TwitchRewardId, persist F.5 row (IsManaged=true) in the same IUnitOfWork, emit
    // RewardConfigurationChangedEvent(published). Managed=false -> persist a local-only stub for metadata/pipeline
    // attach (no Helix). TWITCH_REWARD_CONFLICT if Twitch rejects a duplicate title.
    Task<Result<RewardDetailDto>> CreateAsync(Guid broadcasterId, CreateRewardRequest request, CancellationToken ct = default);

    // Update. Managed -> Helix PATCH custom_rewards (cost/title/prompt/cooldown/caps/enabled) + local upsert,
    // emit RewardConfigurationChangedEvent(updated). Unmanaged -> only local metadata (display, PipelineId);
    // any field Twitch owns returns UNMANAGED_REWARD.
    Task<Result<RewardDetailDto>> UpdateAsync(Guid broadcasterId, Guid rewardId, UpdateRewardRequest request, CancellationToken ct = default);

    // Pause/resume. Managed only -> Helix PATCH is_paused; emit RewardConfigurationChangedEvent(paused|resumed).
    // Unmanaged -> UNMANAGED_REWARD.
    Task<Result<RewardDetailDto>> SetPausedAsync(Guid broadcasterId, Guid rewardId, bool paused, CancellationToken ct = default);

    // Delete. Managed -> Helix DELETE custom_rewards then soft-delete local, emit ...Changed(retired).
    // Unmanaged -> soft-delete the local row only (the Twitch reward is the streamer's; the bot just stops
    // tracking it). Redemptions already logged keep RewardTitleSnapshot.
    Task<Result> DeleteAsync(Guid broadcasterId, Guid rewardId, CancellationToken ct = default);

    Task<Result<RewardDetailDto>> GetAsync(Guid broadcasterId, Guid rewardId, CancellationToken ct = default);
    Task<Result<PagedList<RewardListItemDto>>> ListAsync(Guid broadcasterId, RewardFilter filter, PaginationParams paging, CancellationToken ct = default);

    // Attach/detach a pipeline to a reward (managed or unmanaged — reacting is always allowed). PipelineId must
    // belong to the tenant (validated against commands-pipelines). null detaches.
    Task<Result<RewardDetailDto>> AttachPipelineAsync(Guid broadcasterId, Guid rewardId, Guid? pipelineId, CancellationToken ct = default);

    // Reconcile the bot's MANAGED rewards with Twitch: Helix GET custom_rewards?only_manageable_rewards=true,
    // upsert/repair drift (cost/title/paused), and flag local managed rows missing on Twitch as retired. Does
    // NOT pull unmanaged rewards (those arrive via redemption). Idempotent.
    Task<Result<RewardSyncReportDto>> SyncWithTwitchAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

### 3.2 `IRewardRedemptionService` — fulfill / refund / read

```csharp
namespace NomNomzBot.Application.Contracts.Rewards;

public interface IRewardRedemptionService
{
    // Mark a redemption FULFILLED. Managed reward only -> Helix Update Redemption Status = FULFILLED, emit
    // RewardRedemptionFulfilledEvent. UNMANAGED_REWARD if the reward isn't bot-created (no-op).
    // REDEMPTION_ALREADY_RESOLVED if not currently `unfulfilled`.
    Task<Result> FulfillAsync(Guid broadcasterId, string twitchRedemptionId, RedemptionResolution resolution, CancellationToken ct = default);

    // Mark a redemption CANCELED (Twitch returns the viewer's points). Managed only -> Helix Update Redemption
    // Status = CANCELED, emit RewardRedemptionRefundedEvent. Same guards as Fulfill.
    Task<Result> RefundAsync(Guid broadcasterId, string twitchRedemptionId, RedemptionResolution resolution, CancellationToken ct = default);

    // Read the redemption fact log (F.6 projection), filterable by reward/status/time/redeemer.
    Task<Result<PagedList<RewardRedemptionDto>>> ListRedemptionsAsync(Guid broadcasterId, RedemptionFilter filter, PaginationParams paging, CancellationToken ct = default);
}
```

`RedemptionResolution` = `record(string ResolvedBy, Guid? ResolvedByUserId, string? Reason)` — `"pipeline"`
when raised by a pipeline action (UserId null), `"dashboard"`/`"api"` with the acting management user.

### 3.3 `RewardRedeemedPipelineTrigger` — redemption → pipeline (the B7 trigger)

A scoped handler subscribed (via the bus) to `RewardRedeemedEvent`. Not a projection — it has a live side
effect (running a pipeline), so it is a bus handler, not a journal fold.

```csharp
namespace NomNomzBot.Application.Contracts.Rewards;

public interface IRewardRedeemedPipelineTrigger
{
    // On RewardRedeemedEvent: resolve the reward (auto-stub if the TwitchRewardId is unknown), and if it has a
    // PipelineId, build a PipelineRequest (TriggerKind="reward_redemption", RewardId/RedemptionId set,
    // InitialVariables seeded: user, user.id, reward, reward.id, redemption.id, cost, input) and call
    // IPipelineEngine.ExecuteAsync. No pipeline attached -> no-op (the redemption is still logged by the
    // projection). Idempotent on TwitchRedemptionId (re-delivery must not double-run).
    Task<Result> OnRedeemedAsync(RewardRedeemedEvent @event, CancellationToken ct = default);
}
```

### 3.4 `RewardRedemptionProjection : IProjection` — the F.6 fact log

Implements the `event-store.md` §3.3 `IProjection` contract. `SubscribedEventTypes` = `{ RewardRedeemed,
RewardRedemptionFulfilled, RewardRedemptionRefunded }`. `IsGlobal = false` (per-tenant). `ApplyAsync` upserts
keyed on `(BroadcasterId, TwitchRedemptionId)` — idempotent for replay:
- `RewardRedeemedEvent` → insert the F.6 row (status from the event; snapshots `RewardTitleSnapshot`,
  `RedeemerDisplayNameSnapshot`, `CostSnapshot`, `UserInput`), FK `EventId` to the journaled row.
- `RewardRedemptionFulfilledEvent` → set `Status = fulfilled`.
- `RewardRedemptionRefundedEvent` → set `Status = canceled`.

`ResetAsync` truncates F.6 for the scope before a rebuild. The redemption log is a derived read model —
rebuilt from `EventJournal` only, never the authoritative store of the points math (that is Twitch's).

---

## 4. DTOs / contracts

`NomNomzBot.Application/Contracts/Rewards/` (requests/responses), serialized **Newtonsoft.Json**.

### Responses

- `RewardListItemDto` — `Id, TwitchRewardId?, Title, Cost?, IsEnabled, IsPaused, IsManaged, PipelineId?,
  ShouldSkipRequestQueue`.
- `RewardDetailDto` — list fields **plus** `Description?, Prompt?, IsUserInputRequired, BackgroundColor?,
  MaxPerStream?, MaxPerUserPerStream?, GlobalCooldownSeconds?, CreatedAt, UpdatedAt`.
- `RewardRedemptionDto` — `Id, RewardId?, TwitchRedemptionId, RewardTitleSnapshot, RedeemerUserId,
  RedeemerDisplayNameSnapshot?, UserInput?, CostSnapshot?, Status, StreamId?, RedeemedAt`.
- `RewardSyncReportDto` — `Reconciled:int, Repaired:int, RetiredMissing:int` (drift summary).

### Requests / commands

- `CreateRewardRequest` — `Title (req), bool IsManaged, Cost?, Prompt?, bool IsUserInputRequired,
  BackgroundColor?, bool IsEnabled = true, MaxPerStream?, MaxPerUserPerStream?, GlobalCooldownSeconds?,
  bool ShouldSkipRequestQueue = false, Guid? PipelineId`. Validation: `IsManaged = true` requires `Cost ≥ 1`
  (Twitch min); `ShouldSkipRequestQueue = true` forbidden with `IsUserInputRequired = true` (Twitch rejects).
- `UpdateRewardRequest` — same fields as create, all optional (PATCH semantics) **except** `IsManaged` is
  immutable (a reward can't switch creator). Changing managed-owned fields on an unmanaged reward →
  `UNMANAGED_REWARD`.
- `RewardFilter` — `string? Search, bool? IsManaged, bool? IsEnabled`.
- `RedemptionFilter` — `Guid? RewardId, string? Status, Guid? RedeemerUserId, DateTimeOffset? From, To`.
- `FulfillRedemptionRequest` / `RefundRedemptionRequest` — `{ string? Reason }` (the acting user comes from the
  JWT, not the body).

---

## 5. Controller endpoints

New `RewardsController` under `NomNomzBot.Api/Controllers/V1/`, `[ApiVersion("1.0")]`, inherits
`BaseController`, `[Authorize]`, routes through `ResultResponse`/`GetPaginatedResponse`. Tenant `{channelId}`
resolves to `Guid broadcasterId` via tenant middleware + `IChannelAccessService` (caller must control the
channel).

**Role gate** (schema B.3 `ActionDefinitions`). All keys below are **management**-plane (dashboard config /
moderation), seeded global `ActionDefinitions` (added to `roles-permissions.md` §7.1). Gate 1 = `[Authorize]` +
tenant resolution; Gate 2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)`
enforces the per-route floor (403 `FORBIDDEN` below). Effective level =
`MAX(community standing, management role, active permit grant)`.

### RewardsController — `api/v{version}/channels/{channelId}/rewards`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | `RewardFilter`+`PageRequestDto` | `PaginatedResponse<RewardListItemDto>` | management / Moderator · `reward:read` |
| GET | `/{rewardId}` | — | `StatusResponseDto<RewardDetailDto>` | management / Moderator · `reward:read` |
| POST | `/` | `CreateRewardRequest` | `StatusResponseDto<RewardDetailDto>` (201) | management / Broadcaster · `reward:manage` |
| PATCH | `/{rewardId}` | `UpdateRewardRequest` | `StatusResponseDto<RewardDetailDto>` | management / Broadcaster · `reward:manage` |
| POST | `/{rewardId}/pause` | `{ bool paused }` | `StatusResponseDto<RewardDetailDto>` | management / Broadcaster · `reward:manage` |
| POST | `/{rewardId}/pipeline` | `{ Guid? pipelineId }` | `StatusResponseDto<RewardDetailDto>` | management / Broadcaster · `reward:manage` |
| DELETE | `/{rewardId}` | — | `StatusResponseDto<object>` | management / Broadcaster · `reward:manage` |
| POST | `/sync` | — | `StatusResponseDto<RewardSyncReportDto>` | management / Broadcaster · `reward:sync` |
| GET | `/redemptions` | `RedemptionFilter`+`PageRequestDto` | `PaginatedResponse<RewardRedemptionDto>` | management / Moderator · `reward:redemption:read` |
| POST | `/redemptions/{twitchRedemptionId}/fulfill` | `FulfillRedemptionRequest` | `StatusResponseDto<object>` | management / Moderator · `reward:redemption:fulfill` |
| POST | `/redemptions/{twitchRedemptionId}/refund` | `RefundRedemptionRequest` | `StatusResponseDto<object>` | management / Moderator · `reward:redemption:refund` |

Fulfill/refund on an unmanaged reward return `403 FORBIDDEN` (`UNMANAGED_REWARD`) — the dashboard hides the
buttons for unmanaged rewards, but the gate fails closed regardless.

---

## 6. Pipeline actions

New file `NomNomzBot.Infrastructure/Pipeline/Actions/RewardActions.cs`, each implementing the **single
canonical `ICommandAction`** (`commands-pipelines.md` §3.13). These let a reward's own pipeline decide the
redemption outcome (the legacy `RewardContext.FulfillAsync/RefundAsync` callbacks, now as pipeline actions).
Both resolve the redemption from `context.RedemptionId` + `context.RewardId` (set by the trigger) and the
tenant from `context.BroadcasterId` (already `Guid`).

| Action | `ActionType` | Config params | Behavior |
|---|---|---|---|
| `FulfillRedemptionAction` | `fulfill_redemption` | — | Calls `IRewardRedemptionService.FulfillAsync` for the triggering redemption (`ResolvedBy="pipeline"`). On an **unmanaged** reward it is a **no-op with a logged warning** (`ActionResult.Success`, never fails the pipeline — the reward author may not know the reward isn't bot-created). No-op (Success) when not triggered by a redemption. |
| `RefundRedemptionAction` | `refund_redemption` | `reason:string?` | Calls `RefundAsync` (CANCELED, points returned). Same managed-only / no-op-warn semantics. |

Registered transient (stateless) in the `ICommandAction` block. Surfaced in the pipeline-builder UI catalog
(frontend renders from this action set).

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs`, "Application services" block (scoped — all consume
`IApplicationDbContext`/repositories/`IUnitOfWork`/`ITwitchChannelPointsApi`). Implementations in
`NomNomzBot.Infrastructure/Services/Rewards/`.

```csharp
// Rewards — application services (scoped: use DbContext + UnitOfWork + Helix)
services.AddScoped<IRewardService, RewardService>();
services.AddScoped<IRewardRedemptionService, RewardRedemptionService>();
services.AddScoped<IRewardRedeemedPipelineTrigger, RewardRedeemedPipelineTrigger>();

// Redemption fact-log projection (multi-registered like every IProjection)
services.AddScoped<IProjection, RewardRedemptionProjection>();

// Pipeline actions (transient, stateless) — alongside the existing ICommandAction registrations
services.AddTransient<ICommandAction, FulfillRedemptionAction>();
services.AddTransient<ICommandAction, RefundRedemptionAction>();
```

The `RewardRedeemedEvent` → `IRewardRedeemedPipelineTrigger` wiring rides the existing bus-handler
registration convention (`commands-pipelines.md` event-response dispatch); no bespoke hosted service.

---

## 8. Dependencies (from the stack doc)

- **`ITwitchChannelPointsApi`** (`twitch-helix.md`) — Helix `Create/Update/Delete Custom Reward`, `Get Custom
  Rewards`, `Update Redemption Status`. The reward subsystem never calls Helix directly.
- **`INotificationDispatcher`** (`twitch-eventsub.md` §3.4) — maps `channel.channel_points_custom_reward_
  redemption.add` to `RewardRedeemedEvent`. The `.update` topic is informational (Twitch echoes status changes
  the bot itself made) and is dropped — the bot's own fulfill/refund already emitted the outcome event.
- **`IPipelineEngine`** (`commands-pipelines.md`) — runs the attached pipeline; `PipelineRequest` already
  carries `RewardId`/`RedemptionId`.
- **`IEventJournal` / `IEventBus`** (`event-store.md`) — journaling + projection rebuild.
- **`TimeProvider`** — the clock.
- **Progressive scopes** (`identity-auth.md` `IScopeGrantService`): reacting to redemptions needs
  `channel:read:redemptions` (also required by the EventSub subscription); creating/updating/deleting managed
  rewards and fulfilling/refunding needs `channel:manage:redemptions`. The manage scope is requested
  **progressively** — the first time the streamer creates a managed reward or fulfills a redemption — not at
  login. Absent the scope, managed endpoints return `FEATURE_DISABLED` with a re-auth prompt; unmanaged
  reacting keeps working.

---

## 9. Decisions (resolved)

1. **`IsPlatform` → `IsManaged` (renamed, inverted).** One boolean carries the whole managed/unmanaged
   distinction; the column is renamed in schema F.5. No separate "platform reward" concept survives — a reward
   is either bot-created-and-controlled or observe-and-react.
2. **Fulfill/refund/update/delete are managed-only**, enforced both by the Gate-2 `UNMANAGED_REWARD` result and
   by the pipeline actions no-op-warning — because Twitch forbids a non-creator client from touching the reward
   or its redemptions. This is a platform rule, not a policy choice.
3. **F.6 `RewardRedemptions` is a projection** owned here, rebuilt from the journal (`RewardRedeemed` +
   fulfilled/refunded outcome events), keyed `(BroadcasterId, TwitchRedemptionId)`. It is **not** an
   authoritative points store (Twitch owns the points); it is the queryable fact log for pipelines, economy
   earning, analytics, and leaderboards.
4. **Auto-fulfill uses the existing `ShouldSkipRequestQueue`**, not a new column. Skip-queue rewards arrive
   already fulfilled (no refund possible); queued rewards stay `unfulfilled` until a pipeline action, the
   dashboard, or an API call resolves them — never silently fulfilled by the bot.
5. **Unmanaged rewards auto-stub at first redemption** (`IsManaged = false`, idempotent on
   `(BroadcasterId, TwitchRewardId)`) so every redemption has a `RewardId` and the streamer can attach a
   pipeline retroactively.
6. **`RewardRedeemedEvent` is declared here** (rewards owns the reward vocabulary) and mapped by the EventSub
   dispatcher — resolving B7's "no owner for F.5/F.6 + the redemption handler + the reward→pipeline trigger".
