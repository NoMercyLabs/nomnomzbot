# Rewards — Interface Specification

Implementable spec for the **rewards** subsystem: Twitch channel-point custom rewards (the local
source-of-truth `Rewards`), the redemption queue read model (`Redemptions`), the EventSub redemption →
pipeline trigger, and the **manageable vs read-only** lifecycle that decides whether the bot may create,
update, fulfill, or refund a reward on Twitch. This document describes the code **as built**; where the
original design differed, the built shape wins and is what is written here.

Source of truth: locked schema `2026-06-16-database-schema.md` Domain **F.5 Rewards** / **F.6
Redemptions**, plus Domain **O** (`EventJournal`) for the redemption fact stream and Domain **F.7
EventSubSubscriptions** (owned by `twitch-eventsub.md`). Library choices:
`2026-06-16-stack-and-dependencies.md`. This spec conforms to the resolved cross-cutting decisions in
`2026-06-16-decisions-resolved.md`. Closes gap **B7 / M1** (`_GAP-AUDIT.md`): F.5/F.6 and the
`channel.channel_points_custom_reward_redemption.add` handler now have an owner.

## Binding conventions (every signature below obeys these)

- Namespace root `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way
  (never `.Result`/`.Wait`). Layout: entities `NomNomzBot.Domain/Rewards/Entities/`, events
  `NomNomzBot.Domain/Rewards/Events/`, contracts + DTOs `NomNomzBot.Application/Rewards/{Services,Dtos}/`,
  implementations, handlers, projections, actions and jobs `NomNomzBot.Infrastructure/Rewards/`.
- Fallible operations return `Result` / `Result<T>` (`NomNomzBot.Application.Common.Models`). Never null,
  never throw for expected failure. Error codes reuse `BaseController.ResultResponse`'s known set
  (`NOT_FOUND`, `VALIDATION_FAILED`, `FORBIDDEN`, `ALREADY_EXISTS`, …) plus the reward-specific codes
  `MIGRATION_PENDING_EXTERNAL_REMOVAL` and `REWARD_SYNC_CONFLICT`; a Helix refusal passes its own code through
  (`TWITCH_ERROR` when none). There is **no `UNMANAGED_REWARD` code** — a write to a read-only reward is
  `FORBIDDEN`.
- **Tenant key `BroadcasterId` is `Guid`** on every entity. The reward service methods take the channel as a
  `string` (the route value) and parse it to `Guid` at the top (`VALIDATION_FAILED` when malformed);
  `IRedemptionTimerService.StartAsync` takes a `Guid`. Twitch ids (`TwitchRewardId`, `RedemptionId`, user ids) are
  `string` attribute columns, never keys.
- Surrogate PKs are `Guid`; new entities use `Guid.CreateVersion7()` (some legacy `Guid.NewGuid()` calls remain);
  the `Redemptions` read model uses a `long` identity `Id`.
- Repository / `IApplicationDbContext`; no raw `DbContext` in controllers beyond the leaderboard read. A managed
  create/update mirrors to Twitch **first**, then persists the local row from what Helix confirmed (Twitch is the
  slow, authoritative leg; never persist a managed row whose Twitch create failed).
- `Redemption.Status` stores the short string token `unfulfilled|fulfilled|canceled`, not an int.
- Responses are `StatusResponseDto<T>` (`NomNomzBot.Api.Models`) or `PaginatedResponse<T>`; list endpoints
  page via `PageRequestDto` → `PaginationParams` and return `PagedList<T>` from services.
- Controllers: `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/...")]`, `[Authorize]`, inherit
  `BaseController`, return through `ResultResponse` / `GetPaginatedResponse`.
- DI via typed interfaces (NO MediatR, no Roslyn). Soft-delete via `SoftDeletableEntity` + global filter on
  `Rewards`. `Redemptions` is a **mutable read model** (a status change updates the row; a projection reset
  hard-clears the channel's rows and a replay re-folds them from the journal) — it is not append-only and not
  soft-deleted.
- The single injected clock is `TimeProvider` (`platform-conventions.md` §3.11); never `DateTimeOffset.UtcNow`.

---

## 1. Entities

This subsystem **owns** these tables and declares the reward domain events. It does not redefine tables owned
elsewhere.

- **F.5 `Rewards`** `[soft-delete]` — local source-of-truth for a channel-point reward, mirrored to/from
  Twitch. Notable columns: `Title`, `Cost?`, `Description?` (the viewer-facing **Prompt**), `Response?` (chat
  template announced on redemption), `IsEnabled`, `IsPaused`, `IsUserInputRequired`, `BackgroundColor?`,
  `MaxPerStream?`, `MaxPerUserPerStream?`, `GlobalCooldownSeconds?`, `TwitchRewardId?` (unique with
  `BroadcasterId`), `PipelineId?` (saved-pipeline binding), `PipelineJson?` (inline fallback),
  `TimerDurationSeconds?` (§10), `PendingMigrationRequestedAt?` (§1.1), and the platform-content provenance
  columns (`PlatformSourceDefinitionId`, `PlatformSourceVersion`, `PlatformSourceHash`, `PlatformSourceSyncedAt`).
  **The ownership flag is `IsManageable`** — `true` means *the bot's own `client_id` created this reward on
  Twitch and controls its lifecycle*; `false` means *the reward exists on Twitch but the bot only reacts to it*.
  `IsPlatform` is a **separate, unrelated** provenance boolean (bot-created vs external-app metadata); the two
  never merged and there is no `IsManaged` column anywhere. There is **no `ShouldSkipRequestQueue` column**.
- **F.6 `Redemptions`** — one row per channel-point redemption, the redemption queue read model. `long Id`,
  `BroadcasterId`, `RedemptionId` (the real Twitch redemption id; unique with `BroadcasterId`; the projection's
  upsert key), `RewardId` (Twitch's reward UUID as a string, **not** a FK to `Rewards`), `RewardTitle`, `UserId`,
  `UserDisplayName`, `Cost`, `UserInput?`, `Status`, `RedeemedAt`, `UpdatedAt`. Folded from journaled events by
  `RewardRedemptionProjection` (§3.4). Consumed by the Rewards page queue and the blast-radius count (§14).
- **`RedemptionTimers`** and **`WatchStreaks`** — see §10 and §11.
- `Channel.RewardsSyncedAt?` — throttle stamp for the background import (§1.2).

References (owned elsewhere, never mutated here): **F.7 `EventSubSubscriptions`** (`twitch-eventsub.md`),
**Pipelines/PipelineSteps** (`commands-pipelines.md`), **EventJournal** (`event-store.md`), **Channels/Users**
(`identity-auth.md`).

### 1.1 Manageable vs read-only — the load-bearing distinction

A channel-point reward on Twitch can be updated, deleted, and have its redemptions marked
fulfilled/canceled **only by the application `client_id` that created it** (Twitch Helix rule:
`Update Custom Reward`, `Delete Custom Reward`, and `Update Redemption Status` reject a reward not created by the
calling client). This single platform constraint is why every reward carries `IsManageable`:

| | **Manageable** (`IsManageable = true`) | **Read-only** (`IsManageable = false`) |
|---|---|---|
| Origin | Bot created it via Helix `POST channel_points/custom_rewards` (or took control, below) | Streamer (or another app) created it on Twitch |
| `TwitchRewardId` | Always set (returned by the create call) | Set once imported / observed |
| Update / delete / pause on Twitch | Yes, via Helix | No — a Twitch-facing field patch returns `FORBIDDEN` ("created outside the bot and is read-only; convert it to bot-controlled first"); only bot-local bindings (`PipelineId`, `TimerDurationSeconds`, `Response`) change |
| Fulfill / refund a redemption | Yes, via Helix `Update Redemption Status` | Helix rejects it; the dashboard hides the buttons and the Helix refusal surfaces as the result |
| Redemption → pipeline trigger | Yes | Yes (reacting is always allowed — EventSub fires for every reward regardless of creator) |
| Required Twitch scope | `channel:manage:redemptions` (+ `channel:read:redemptions`) | `channel:read:redemptions` only |

Manageability is **not** a field on Twitch's reward payload. It is derived by set membership: Helix
`GET custom_rewards?only_manageable_rewards=true` returns exactly the rewards this client created, and a reward is
stored manageable iff its id is in that subset.

**Taking control — `IRewardService.RecreateUnderBotAsync`.** Twitch gives no way to transfer a reward to a
different `client_id`; the only way to make a read-only reward bot-manageable is to **recreate an equivalent
reward** under the bot's client (title / cost / prompt / enabled copied across) and **re-point the same local
row** at it (new `TwitchRewardId`, `IsManageable = true`, `IsPlatform = true`), so the pipeline, response, limits
and cooldowns configured on the row carry over. The original external reward and its redemption history stay on
Twitch untouched. Twitch requires every reward title to be unique among **all** of a broadcaster's rewards, and a
read-only reward cannot be renamed, disabled or deleted through the bot's client — so a straight recreate always
400s while the original holds the title. Rather than a dead end the method **parks** the request:
`Reward.PendingMigrationRequestedAt` is stamped, nothing is lost, and the result is
`MIGRATION_PENDING_EXTERNAL_REMOVAL` telling the operator to rename or delete the reward in the Twitch Creator
Dashboard. The dashboard swaps "Take control" for "Finalize migration" (`RewardDetail.IsMigrationPending`). The
title check re-reads Twitch's **live** list (never the local table), and a Twitch duplicate-title rejection on the
create call itself parks the same way. Retrying the same action completes the recreate once the title is free and
clears the marker; the next import (§1.2) also completes any parked take-over whose original was deleted or renamed
on Twitch, with no second click. `ALREADY_EXISTS` when the reward is already manageable.

**Auto-fulfill.** There is no bot-side auto-fulfill setting. A redemption's queue behavior is whatever the reward is
configured to do on Twitch. A queued redemption stays `unfulfilled` until a pipeline step, the dashboard, a
redemption timer (§10), or the streamer in Twitch's own queue resolves it — the bot never silently fulfills.

### 1.2 Discovery — how a reward gets a local row

There is **no redemption-time auto-stub**. A redemption whose reward has no local row still journals and folds into
`Redemptions`, and the generic event response runs (§3.3), but no `Rewards` row is created by the redemption. A
reward gets its local row through exactly three paths:

1. **Bulk import — `IRewardService.ImportFromTwitchAsync`.** Reads Helix `custom_rewards?only_manageable_rewards=false`
   for the **full** set, then a second `=true` read for the manageable subset, and upserts by `TwitchRewardId`,
   then by `Title` (so a reward already recreated under the bot reconciles onto its own row instead of a second one
   sharing its predecessor's title). Idempotent; a failed Helix read surfaces its real error rather than persisting
   "everything unmanaged". Triggered by `POST /rewards/import`, **and** automatically: `IRewardService.ListAsync`
   runs it in the background of a list call, throttled by `Channel.RewardsSyncedAt` (null or older than 6 hours →
   import, best-effort — a failure is logged at debug and never fails the list — then stamp `RewardsSyncedAt`
   regardless of outcome so a dead connection is not retried on every page load). A streamer's first visit to
   Rewards therefore surfaces their pre-existing rewards as read-only ("Take control") rows, and a reward created
   or removed on Twitch's own dashboard later reflects here within the throttle window.
2. **Onboarding seed — `RewardSeedOnOnboardingHandler`** (`IEventHandler<ChannelOnboardedEvent>`). Runs
   `SyncWithTwitchAsync` once when a channel finishes onboarding. `SyncWithTwitchAsync` reconciles the **manageable**
   subset only (`only_manageable_rewards=true`) and also **pushes** any local manageable reward that Twitch has
   never seen (no `TwitchRewardId`, e.g. from a bundle import created with `pushToTwitch: false`) to Helix,
   best-effort. Independently resilient: a failure is caught and logged, never propagated.
3. **EventSub lifecycle — `RewardLifecycleHandler`.** `channel.channel_points_custom_reward.add/update/remove`
   become `RewardCreatedEvent` / `RewardUpdatedEvent` / `RewardRemovedEvent`. `RewardCreatedEvent` inserts a row if
   the `TwitchRewardId` is unknown (`IsManageable` left `false` until the next import/sync classifies it);
   `RewardUpdatedEvent` syncs title / cost / enabled / paused onto the row and derives the pause/enable
   transitions (§12); `RewardRemovedEvent` **keeps the row** (its config survives for re-creation) but sets
   `IsEnabled = false` and clears `TwitchRewardId`.

**The dashboard's action-required inbox** (`IActionRequiredInboxService`,
`NomNomzBot.Application/Notifications/Services/`) has an `unmanaged_rewards` category
(`UnmanagedRewardSource`): one grouped item per channel (not one per reward) whenever `IsManageable = false` rows
with a `TwitchRewardId` exist, severity `info`, deep-linking to `/rewards`. Its stable dismiss key hashes the
**set** of read-only reward ids, so dismissing today's set does not hide a **different** reward that becomes
read-only later.

---

## 2. Domain events

Events live in `NomNomzBot.Domain/Rewards/Events/`, are `sealed class … : DomainEventBase` with `required`
init-only properties (the canonical base carries `EventId`, `BroadcasterId` and `OccurredAt` — never redeclared
here), and are the **bus** contract that `event-store.md` journals. The Twitch ids on them are plain `string`s.

```csharp
namespace NomNomzBot.Domain.Rewards.Events;

// The per-topic event the EventSub translator maps
// `channel.channel_points_custom_reward_redemption.add` to. RewardId is Twitch's reward UUID.
public sealed class RewardRedeemedEvent : DomainEventBase
{
    public required string RewardId { get; init; }
    public required string RewardTitle { get; init; }
    public required string RedemptionId { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required int Cost { get; init; }
    public string? UserInput { get; init; }
}

// `channel.channel_points_custom_reward_redemption.update` — a queued redemption moved to fulfilled/canceled
// (by anyone: the dashboard, a pipeline step, the streamer in Twitch's own queue). Implements IProviderScopedEvent.
public sealed class RewardRedemptionUpdatedEvent : DomainEventBase, IProviderScopedEvent
{
    public string Provider { get; init; }                       // defaults to Twitch
    public required string RedemptionId { get; init; }
    public required string RewardId { get; init; }
    public required string RewardTitle { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string Status { get; init; }                // "fulfilled" | "canceled"
}
```

Reward CRUD mirrored from Twitch's own feed (§1.2 path 3), each `sealed class … : DomainEventBase`:
`RewardCreatedEvent` and `RewardUpdatedEvent` (`TwitchRewardId, Title, Cost, IsEnabled, IsPaused`) and
`RewardRemovedEvent` (`TwitchRewardId, Title`). Also on the bus: `AfterRewardProcessedEvent`
(`RewardId, RedemptionId, Succeeded, Duration`), raised by `RewardRedeemedHandler` after a redemption's pipeline ran. A refund is `RewardRedemptionUpdatedEvent` with status `canceled`. The extra Twitch events are in §12.

There is **no** `RewardRedemptionFulfilledEvent`, `RewardRedemptionRefundedEvent` or
`RewardConfigurationChangedEvent`. A fulfil/refund made through the bot is observed through Twitch's own
`.update` echo (§3.4), not a bot-emitted outcome event.

---

## 3. Service interfaces

Contracts in `NomNomzBot.Application/Rewards/Services/`; implementations in `NomNomzBot.Infrastructure/Rewards/`,
registered by the `I<X>Service` convention (`AddServicesByConvention`). Every fallible op returns
`Result`/`Result<T>`. Helix calls go through `ITwitchChannelPointsApi` (`twitch-helix.md`:
`Create/Update/Delete CustomRewardAsync`, `GetCustomRewardsAsync`, `GetCustomRewardRedemptionsAsync`,
`UpdateRedemptionStatusAsync`) — this subsystem never builds raw Helix requests.

### 3.1 `IRewardService` — reward lifecycle, redemption queue, local metadata

One service owns both the reward lifecycle and the queue actions (there is no separate redemption service).

```csharp
public interface IRewardService
{
    // Create. Checks the LIVE Twitch list for a same-titled reward first (ALREADY_EXISTS, fails closed when it
    // cannot verify), then Helix-first: POST custom_rewards, persist the row from what Helix CONFIRMED
    // (IsManageable = true). pushToTwitch:false is only for a bulk/offline path (bundle import) that gets the
    // reward onto Twitch later via SyncWithTwitchAsync.
    Task<Result<RewardDetail>> CreateAsync(string broadcasterId, CreateRewardRequest request,
        bool pushToTwitch = true, CancellationToken ct = default);

    // Update (PATCH and PUT both land here). Twitch-facing fields on a manageable reward -> Helix PATCH first,
    // then the local row; on a read-only reward those fields return FORBIDDEN. Bot-local bindings
    // (Response, PipelineId, TimerDurationSeconds) are always writable. IsPaused syncs locally too.
    Task<Result<RewardDetail>> UpdateAsync(string broadcasterId, string rewardId,
        UpdateRewardRequest request, CancellationToken ct = default);

    // Delete. Manageable -> Helix DELETE first ("already gone on Twitch" is the wanted outcome, not a failure),
    // then soft-delete the row. Read-only -> soft-delete the local row only.
    Task<Result> DeleteAsync(string broadcasterId, string rewardId, CancellationToken ct = default);

    // Counted blast radius of the delete (§14).
    Task<Result<BlastRadiusDto>> GetDeleteBlastRadiusAsync(string broadcasterId, string rewardId,
        CancellationToken ct = default);

    Task<Result<PagedList<RewardDetail>>> ListAsync(string broadcasterId, PaginationParams pagination,
        CancellationToken ct = default);          // also runs the throttled background import (§1.2)
    Task<Result<RewardDetail>> GetAsync(string broadcasterId, string rewardId, CancellationToken ct = default);

    // The redemption queue read model, newest first, optionally filtered to one status.
    Task<Result<PagedList<RedemptionListItem>>> ListRedemptionsAsync(string broadcasterId, string? status,
        PaginationParams pagination, CancellationToken ct = default);

    // Fulfil/refund a queued redemption: twitchStatus = "FULFILLED" | "CANCELED". Resolves the reward id from the
    // Redemptions row, calls Helix Update Redemption Status, then updates the local row optimistically so the
    // pending lane drops it at once (the .update echo folds the same status idempotently). NOT_FOUND when the
    // redemption row is unknown; a Helix refusal (e.g. a read-only reward) returns its own code.
    Task<Result> SetRedemptionStatusAsync(string broadcasterId, string redemptionId, string twitchStatus,
        CancellationToken ct = default);

    Task<Result> SyncWithTwitchAsync(string broadcasterId, CancellationToken ct = default);      // §1.2 path 2
    Task<Result> ImportFromTwitchAsync(string broadcasterId, CancellationToken ct = default);    // §1.2 path 1
    Task<Result<RewardDetail>> RecreateUnderBotAsync(string broadcasterId, string rewardId,
        CancellationToken ct = default);                                                          // §1.1
}
```

There is no `AttachPipelineAsync`: a pipeline is bound through `PipelineId` on create/update (`Guid.Empty` on update
clears it; the id is validated to belong to the channel).

### 3.2 `IRedemptionTimerService` — countdowns

See §10.

### 3.3 `RewardRedeemedHandler` — redemption → behavior (the B7 trigger)

`IEventHandler<RewardRedeemedEvent>` (`NomNomzBot.Infrastructure/Rewards/EventHandlers/`), auto-discovered by the
handler scan — there is no `IRewardRedeemedPipelineTrigger` interface. It looks the reward up by
`(BroadcasterId, TwitchRewardId)` and seeds the variables `user`, `user.id`, `reward`, `reward.id`, `redemption.id`,
`cost`, `input`. Then, in order:

1. **Local kill switch** (§15): `Reward.IsEnabled = false` → return; nothing below runs.
2. **Timer** (§10): `TimerDurationSeconds > 0` → start a countdown (idempotent per redemption). Orthogonal to 3.
3. **What runs**, first that applies wins: (a) the **bound saved pipeline** (`PipelineId` → that pipeline's compiled
   `GraphJsonCache`, run with its `PipelineId` so block-kind steps use the tree walker; a missing/deleted graph logs a
   warning and degrades to b/c); (b) the inline `PipelineJson`; (c) `Response` text wrapped as a one-step
   `send_message` pipeline; (d) none of those → the generic event response
   `channel.channel_points_custom_reward_redemption.add` through `IEventResponseExecutor` (also the path for a
   redemption with no local reward row).
4. A pipeline run goes through `IPipelineEngine.ExecuteAsync` with `RedemptionId`, `RewardId`, `ChannelEventId`
   (the redemption's activity-feed row id, for replay correlation) and the seeded variables. An exception is caught
   and logged; it never propagates.

### 3.4 `RewardRedemptionProjection : IProjection` — the F.6 queue

Implements the `event-store.md` §3.3 `IProjection` contract; `Name = "reward-redemption"`, `IsGlobal = false`.
`SubscribedEventTypes` = `{ RewardRedeemedEvent, RewardRedemptionUpdatedEvent }`. `ApplyAsync` upserts by
`(BroadcasterId, RedemptionId)` — idempotent for replay — and creates the row on either event if it is missing (a
status update can arrive before its add was journaled):
- `RewardRedeemedEvent` → `Status = unfulfilled`, `Cost`, `UserInput`, `RedeemedAt` (authoritative here).
- `RewardRedemptionUpdatedEvent` → `Status` from the event (`fulfilled` / `canceled`).

`ResetAsync` hard-clears the scope's rows before a rebuild. The queue is a derived read model — rebuilt from
`EventJournal` only, never the authoritative store of the points math (that is Twitch's).

The `.update` topic is **not dropped**: `channel.channel_points_custom_reward_redemption.update` is translated to
`RewardRedemptionUpdatedEvent`, folded here, and broadcast to open dashboard sessions by
`RedemptionStatusBroadcastHandler` so the pending queue drops the row on **every** session, whoever made the Helix
call (including the dashboard's own `SetRedemptionStatusAsync`, whose optimistic write this echo confirms).

---

## 4. DTOs / contracts

`NomNomzBot.Application/Rewards/Dtos/RewardDtos.cs`, serialized **Newtonsoft.Json**.

### Responses

- `RewardDetail` — `Id, Title, Prompt?, Response?, Cost, IsEnabled, IsManageable, IsUserInputRequired, IsPaused,
  IsMigrationPending, BackgroundColor?, ImageUrl?, MaxPerStream?, MaxPerUserPerStream?, GlobalCooldownSeconds?,
  TimerDurationSeconds?, PipelineId?, CreatedAt, UpdatedAt`. A list row carries this **same** shape as get/create.
- `RedemptionListItem` — `RedemptionId, RewardId, RewardTitle, UserId, UserDisplayName, UserAvatarUrl?, Cost,
  UserInput?, Status, RedeemedAt`.
- `RedemptionTimerDto` — see §10.

### Requests

- `CreateRewardRequest` — `Title` (required), `Cost` (required), `Prompt?`, `Response?`, `IsUserInputRequired`,
  `BackgroundColor?`, `MaxPerStream?`, `MaxPerUserPerStream?`, `GlobalCooldownSeconds?`, `TimerDurationSeconds?`,
  `PipelineId?`.
- `UpdateRewardRequest` — every field optional (PATCH semantics): the create fields plus `IsEnabled?`, `IsPaused?`.
  `Response`: `null` leaves unchanged, `""` clears. `TimerDurationSeconds`: `0` clears. `PipelineId`: `Guid.Empty`
  clears. There is no `IsManaged`/`IsManageable` on either request — a reward cannot change creator except through
  `RecreateUnderBotAsync`.
- Fulfil / refund carry no body: the acting user comes from the JWT and the status is the route.

---

## 5. Controller endpoints

`RewardsController` under `NomNomzBot.Api/Controllers/V1/`, `[ApiVersion("1.0")]`, `[Authorize]`,
`[Tags("Rewards")]`, inherits `BaseController`, routes through `ResultResponse`/`GetPaginatedResponse`. The tenant
`{channelId}` is a route segment on the built route.

**Role gate.** Every action carries `[RequireAction("<key>")]` (Gate-2, resolved by the seeded
`ActionDefinitions`, `roles-permissions.md`): `reward:read` (default Moderator, floor Vip),
`reward:manage` (Broadcaster), `reward:sync` (Broadcaster). The seeder also defines `reward:redemption:read|fulfill|refund`
(Moderator), but **the controller does not use them** — fulfil / refund / timers are gated by `reward:manage`. Gate-1
is `[Authorize]` + tenant resolution.

### RewardsController — `api/v{version}/channels/{channelId}/rewards`

| Verb | Route | Request | Response | Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | `PageRequestDto` (query) | `PaginatedResponse<RewardDetail>` | `reward:read` |
| GET | `/{rewardId}` | — | `StatusResponseDto<RewardDetail>` | `reward:read` |
| POST | `/` | `CreateRewardRequest` | `StatusResponseDto<RewardDetail>` (201) | `reward:manage` |
| PATCH | `/{rewardId}` | `UpdateRewardRequest` | `StatusResponseDto<RewardDetail>` | `reward:manage` |
| PUT | `/{rewardId}` | `UpdateRewardRequest` | `StatusResponseDto<RewardDetail>` | `reward:manage` |
| GET | `/{rewardId}/blast-radius` | — | `StatusResponseDto<BlastRadiusDto>` | `reward:manage` |
| DELETE | `/{rewardId}` | — | 204 | `reward:manage` |
| POST | `/sync` | — | `StatusResponseDto<object>` | `reward:sync` |
| POST | `/import` | — | `StatusResponseDto<object>` | `reward:sync` |
| POST | `/{rewardId}/recreate` | — | `StatusResponseDto<RewardDetail>` | `reward:sync` |
| GET | `/redemptions` | `?status=` + `PageRequestDto` | `PaginatedResponse<RedemptionListItem>` | `reward:read` |
| POST | `/redemptions/{redemptionId}/fulfill` | — | `StatusResponseDto<object>` | `reward:manage` |
| POST | `/redemptions/{redemptionId}/refund` | — | `StatusResponseDto<object>` | `reward:manage` |
| GET | `/redemption-timers` | — | `StatusResponseDto<IReadOnlyList<RedemptionTimerDto>>` | `reward:read` |
| POST | `/redemption-timers/{timerId:guid}/pause` | — | `StatusResponseDto<RedemptionTimerDto>` | `reward:manage` |
| POST | `/redemption-timers/{timerId:guid}/resume` | — | `StatusResponseDto<RedemptionTimerDto>` | `reward:manage` |
| POST | `/redemption-timers/{timerId:guid}/complete` | — | `StatusResponseDto<RedemptionTimerDto>` | `reward:manage` |
| POST | `/redemption-timers/{timerId:guid}/cancel` | — | `StatusResponseDto<RedemptionTimerDto>` | `reward:manage` |
| GET | `/leaderboard` | — | `StatusResponseDto<List<LeaderboardEntryDto>>` | `reward:read` |

There are **no** `/{rewardId}/pause` or `/{rewardId}/pipeline` routes: pause/resume is `IsPaused` on the PATCH/PUT
body, and a pipeline binding is `PipelineId` on create/PATCH/PUT. `sync` and `import` carry the `WriteExpensive`
rate-limit policy. `GetRewardDeleteBlastRadius` and `DeleteReward` are
`[DestructiveAction(HasCountedBlastRadius = true)]` (§14).

---

## 6. Pipeline actions

`NomNomzBot.Infrastructure/Rewards/PipelineActions/`, each implementing the **single canonical `ICommandAction`**
(`commands-pipelines.md` §3.13). They let a reward's own pipeline decide the redemption outcome.

| Class | `ActionType` | Config params | Behavior |
|---|---|---|---|
| `RedemptionFulfillAction` | `redemption_fulfill` | — | Marks the **triggering** redemption FULFILLED through `IRewardService.SetRedemptionStatusAsync` — the same path the dashboard's fulfil button uses, so Helix and the local queue stay in lockstep. |
| `RedemptionRefundAction` | `redemption_refund` | — | Marks it CANCELED (the viewer's points come back), same path. The legacy-parity failure closer (a song reward whose track failed to queue, a TTS reward with empty input). |

Neither takes parameters: the redemption id is read from `PipelineExecutionContext.RedemptionId` first, then the
`{redemption.id}` variable (`RedemptionContext.ResolveRedemptionId`) — `RewardRedeemedHandler` seeds both, and the
variable also survives the generic event-response path. **Typed failures, not no-ops:** the pipeline was not
triggered by a redemption, or the redemption is no longer pending (already fulfilled/canceled — the service
refuses), or Helix refuses (a read-only reward). A pipeline that wants to survive that wraps the step in
`continue_on_error` or a `try` block. Surfaced in the pipeline-builder catalogue under the
`pipeline.category.rewards` category; the `reward` resource-picker kind is fed by `RewardOptionProvider` (reward cost
and paused/active state as secondary text).

---

## 7. DI registration

Nothing here is registered by hand in `NomNomzBot.Infrastructure/DependencyInjection.cs`:

- `IRewardService` / `IRedemptionTimerService` bind by the `I<X>Service` convention (`AddServicesByConvention`).
- `RewardRedeemedHandler`, `RewardLifecycleHandler`, `RewardSeedOnOnboardingHandler`, `WatchStreakHandler` and the
  other `IEventHandler<T>` classes are picked up by the handler scan (scoped).
- `RewardRedemptionProjection` and `WatchStreakProjection` are multi-registered `IProjection`s found by the scan.
- `RedemptionFulfillAction` / `RedemptionRefundAction` are found by the transient `ICommandAction` scan.
- `RedemptionTimerExpiryService` (a `BackgroundService`) is found by the hosted-worker scan.

---

## 8. Dependencies (from the stack doc)

- **`ITwitchChannelPointsApi`** (`twitch-helix.md`) — Helix `Create/Update/Delete Custom Reward`, `Get Custom
  Rewards` (with `only_manageable_rewards`), `Get Custom Reward Redemption`, `Update Redemption Status`. The reward
  subsystem never calls Helix directly.
- **EventSub translators** (`twitch-eventsub.md`; `ChannelPointsTranslators`, `AdBreakBitsUserTranslators`,
  `ChatTranslators`) — map `channel.channel_points_custom_reward_redemption.add` to `RewardRedeemedEvent`, `.update`
  to `RewardRedemptionUpdatedEvent` (folded and broadcast, §3.4 — **not** dropped), the reward CRUD topics to
  `RewardCreated/Updated/Removed`, plus the extra topics of §12. `BotLifecycleService` subscribes to them.
- **`IPipelineEngine`** (`commands-pipelines.md`) — runs the bound pipeline; `PipelineRequest` carries
  `RewardId`/`RedemptionId`/`ChannelEventId`.
- **`IEventResponseExecutor`** — the generic redemption response and the `reward.*` lifecycle responses.
- **`IEventJournal` / `IEventBus`** (`event-store.md`) — journaling + projection rebuild.
- **`TimeProvider`** — the clock (the timer maths and the 6-hour import throttle).
- **Progressive scopes** (`identity-auth.md` `IScopeGrantService`): reacting to redemptions needs
  `channel:read:redemptions` (also required by the EventSub subscription); creating/updating/deleting manageable
  rewards and fulfilling/refunding needs `channel:manage:redemptions`, requested **progressively** — not at login.
  Read-only reacting keeps working without it.

---

## 9. Decisions (resolved)

1. **`IsManageable` is a new boolean beside the unchanged `IsPlatform`** (migration `AddRewardIsManageable`,
   2026-07-06) — not a rename or inversion of `IsPlatform`. `IsManageable` carries the whole manageable/read-only
   distinction; `IsPlatform` stays an unrelated provenance flag.
2. **Update / delete / fulfil / refund are manageable-only**, because Twitch forbids a non-creator client from
   touching the reward or its redemptions. A Twitch-facing write to a read-only reward is `FORBIDDEN`; a queue action
   on one surfaces Helix's refusal. This is a platform rule, not a policy choice.
3. **F.6 `Redemptions` is a journal-folded read model** owned here, keyed `(BroadcasterId, RedemptionId)`, mutable,
   rebuilt by replay. It is **not** an authoritative points store (Twitch owns the points).
4. **No auto-fulfill setting.** Queue behavior is what Twitch is configured to do; resolution is a pipeline step,
   the dashboard, a redemption timer, or Twitch's own queue — never a silent bot fulfil.
5. **Discovery is three explicit paths** (§1.2 — bulk import incl. the throttled background one, the onboarding
   seed, the EventSub lifecycle feed). There is no redemption-time auto-stub.
6. **Take control re-points the same row** at a recreated bot-owned reward and parks on a title conflict
   (`MIGRATION_PENDING_EXTERNAL_REMOVAL`) instead of failing; the next import finishes it.
7. **One service, one controller.** Reward lifecycle and the redemption queue both live in `IRewardService`; fulfil
   and refund are `reward:manage`.

---

## 10. Redemption timers ("streamer does X for Y")

A reward with `TimerDurationSeconds` set is a **time-limited reward**: every redemption starts a `RedemptionTimer`
for that many seconds (clamped to a 24 h ceiling; `0` clears it). When the countdown completes the redemption is
marked FULFILLED on Twitch. That fulfil is **best-effort**: a read-only reward or a dead token logs a warning with the
Helix code and never undoes the completed timer.

**`RedemptionTimer`** (`RedemptionTimers`, tenant-scoped): `Id`, `BroadcasterId`, `RedemptionId` (unique with
`BroadcasterId`), `RewardId`, `RewardTitle`, `RedeemedByDisplayName`, `DurationSeconds`, `RemainingSeconds`,
`RunningSince?`, `Status` (`running | paused | completed | canceled` — `RedemptionTimerStatus`), `StartedAt`,
`UpdatedAt`. Remaining time is **clock-derived, never tick-decremented**: while running it is `RemainingSeconds`
minus the time since `RunningSince`; a pause folds the elapsed time into `RemainingSeconds` and clears
`RunningSince`. The row is therefore written only on state changes and a restart never loses a countdown. Terminal
rows stay as the channel's timer history.

**`IRedemptionTimerService`:** `StartAsync` (idempotent per redemption — an EventSub redelivery returns the existing
timer), `ListAsync` (active first, then recent terminal; `RemainingSeconds` is the live value at response time),
`PauseAsync`, `ResumeAsync`, `CompleteAsync` (marks it completed **and** fulfils the redemption), `CancelAsync`
(abandons without fulfilling — refunding stays the operator's separate call), `CompleteDueAsync` (the ticker's pass).
`RedemptionTimerDto` = `Id, RedemptionId, RewardId, RewardTitle, RedeemedBy, DurationSeconds, RemainingSeconds, Status,
StartedAt`. **`RedemptionTimerExpiryService`** ticks every 2 s and calls `CompleteDueAsync` so completion side effects
fire close to the moment they are due. Routes are in §5.

## 11. Watch streaks

Twitch's native watch-streak milestone arrives as EventSub `channel.chat.notification` with
`notice_type = watch_streak` and is translated by `ChatTranslators` into `WatchStreakReceivedEvent`
(`UserId, UserLogin, UserDisplayName, StreakMonths, ChannelPointsEarned, CustomMessage?`).

- **`WatchStreak`** (`WatchStreaks`, tenant-scoped, one row per `(BroadcasterId, UserId)`): `CurrentStreak`,
  `MaxStreak` (high-water mark), `LastSeenDate`, `UserDisplayName?`.
- **`WatchStreakProjection`** (`Name = "watch-streak"`) folds the event into the row (upsert, advance
  `CurrentStreak`, keep `MaxStreak` at the max); a reset clears the scope and a replay rebuilds it.
- **`WatchStreakHandler`** upserts the row live **and** runs the `engagement.watch_streak` event response — the same
  key the preset catalogue and dashboard expose, so a streamer's configured response reaches this real Twitch event.
  Variables: `user.id`, `user.login`, `user.name`, `streak.months`, `streak.points`, `streak.message`.
- Watch streaks are counted in the channel-viewer blast radius and the per-user data paths.

## 12. Extra events

Beyond the custom-reward topics, these Twitch topics are subscribed (`BotLifecycleService`), translated, and
published as bus events (so they are journaled and available to outbound webhooks and event responses):

- **`AutomaticRewardRedeemedEvent`** — `channel.channel_points_automatic_reward_redemption.add` (v2): Twitch's
  built-in rewards (`send_highlighted_message`, `random_sub_emote_unlock`, `celebration`, …). Carries
  `RedemptionId, UserId, UserLogin, UserDisplayName, RewardType, Cost, UnlockedEmoteId?, Message?`.
- **`CustomPowerUpRedeemedEvent`** — `channel.custom_power_up_redemption.add`: a streamer-defined Bits Power-up.
  Carries `RedemptionId, UserId, UserLogin, UserDisplayName, PowerUpId, PowerUpTitle, Bits, Status, UserInput?`.
- **`BitsUsedEvent`** — `channel.bits.use`: the unified Bits event covering cheers and Power-up redemptions
  (`Type` = `cheer` | `power_up`, `Bits`, `MessageText?`).
- **`RewardRedemptionUpdatedEvent`** — the `.update` topic (§3.4).
- **Reward state responses.** `RewardLifecycleHandler` compares the last-known local state with the incoming Twitch
  state and, on an actual flip only, dispatches the opt-in event responses `reward.paused`, `reward.resumed`,
  `reward.enabled`, `reward.disabled` (variables `reward`, `reward.id`, `cost`) through `IEventResponseExecutor`
  **after** persisting the sync — no handler-ordering race. A title- or cost-only update dispatches nothing.

## 13. Leaderboard

`GET /rewards/leaderboard` (`reward:read`) returns the **top 50 chatters ranked by message count** as
`LeaderboardEntryDto(Rank, UserId, InternalUserId?, DisplayName, Points)` — `Points` is the chat message count, not
channel points. It groups `ChatMessages` by user, sorts in memory (the SQLite EF provider cannot translate
`GroupBy` + ordered projection), and resolves display names and the internal `User.Id` (the key the analytics viewer
endpoint uses, so a leaderboard row can open that viewer's detail) by joining on `User.TwitchUserId`. It reads from
the controller's `IApplicationDbContext`, not a service.

## 14. Blast radius (delete)

Deleting a reward is `[DestructiveAction(HasCountedBlastRadius = true)]` (S-CONSEQ). The dashboard **must** call
`GET /rewards/{rewardId}/blast-radius` and render the result before the delete confirm can proceed.
`IRewardService.GetDeleteBlastRadiusAsync` returns a `BlastRadiusDto` of **real, counted** dependents:
`Redemptions` (with a sample of redeemer names) and `RedemptionTimers`, both keyed on `Reward.TwitchRewardId`
because they carry Twitch's reward UUID, not our row id. A reward never synced to Twitch has no `TwitchRewardId`, so
nothing can reference it — a verified zero, not a skipped check. `IsMinimum` is `false` (the total is exhaustive).
`NOT_FOUND` when the reward is not in this tenant. `DeleteAsync` itself deletes a manageable reward on Twitch first
and clears `TwitchRewardId` before the soft-delete, so the next sync cannot trip the
`(BroadcasterId, TwitchRewardId)` unique index.

## 15. Local kill switch

`Reward.IsEnabled = false` is the **bot-side kill switch** for a reward: `RewardRedeemedHandler` returns before any
bot behavior — no redemption timer, no bound pipeline, no `Response` text, no generic event response — even though
Twitch itself already granted the redemption (logged at debug). It is separate from Twitch's own pause state
(`IsPaused`, synced from the update feed, which makes Twitch stop accepting redemptions). For a manageable reward
`IsEnabled` is also a Twitch-facing field and is pushed to Helix on update; for a read-only reward it cannot be
changed through the bot (`FORBIDDEN`). A reward with **no** local row is unaffected — it falls through to the generic
event response.
