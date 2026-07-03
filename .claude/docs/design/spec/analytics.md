# Analytics — Interface Specification

Implementable spec for the **analytics** subsystem: the per-viewer and per-channel read models that fold
journaled domain events into queryable aggregates — viewer profiles, watch sessions/streaks, daily rollups —
plus the read API the dashboard consumes and the SaaS-only cross-channel platform stats. Code from this
directly.

Source of truth: locked schema `2026-06-16-database-schema.md` Domain **M** (Analytics: M.1 ViewerProfiles,
M.2 WatchSessions, M.3 WatchStreaks, M.4 MessageActivityDaily, M.5 CommandUsage, M.7 ViewerEngagementDaily,
M.8 ChannelAnalyticsDaily; M.6 removed). Projection contract: `event-store.md` §3.3 (`IProjection` /
`IProjectionRunner`). Library choices: `2026-06-16-stack-and-dependencies.md`. Conforms to the resolved
cross-cutting decisions in `2026-06-16-decisions-pending-confirmation.md`. Closes gap **M2** (`_GAP-AUDIT.md`).

**Scope correction (from grounding).** The audit (`_GAP-AUDIT.md` M2) over-assigned this subsystem.
Analytics does **not** own: the moderation projections **J.4 `UserModerationHistory` / J.5 `UserTrustScore`**
or the **`HeatScore`/`TrustScore`** formula (owned by `moderation.md` via `IModerationProjectionService` +
the canonical `TrustScoreCalculator`); **N.5 `UsageRecord`** tier-usage metering (owned by
`monetization-billing.md`'s `IUsageMeteringService`); **L.1–L.3** leaderboards (owned by `economy.md`).
Analytics is exactly the **Domain M** read models + a read API over them.

## Binding conventions (every signature below obeys these)

- Namespace root `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way
  (never `.Result`/`.Wait`).
- Fallible operations return `Result` / `Result<T>` (`NomNomzBot.Application.Common.Models`). Never null,
  never throw for expected failure. Error codes reuse `BaseController.ResultResponse`'s known set
  (`NOT_FOUND`, `VALIDATION_FAILED`, `FORBIDDEN`, …).
- **Tenant key `BroadcasterId` is `Guid`** (locked schema §1.1 — `ITenantScoped.BroadcasterId` widened
  `string`→`Guid`). All analytics rows are `Guid`-tenanted; viewer ids are internal `Guid`; Twitch ids are
  indexed `string` attribute columns, never keys.
- Surrogate PKs are `Guid` via `Guid.CreateVersion7()` (M.1 ViewerProfiles, M.3 WatchStreaks); the
  **append-only rollup/log tables use `long` identity** (M.2 WatchSessions, M.4 MessageActivityDaily,
  M.7 ViewerEngagementDaily, M.8 ChannelAnalyticsDaily).
- Repository + `IUnitOfWork`; no raw `DbContext` in controllers. Projection upserts are **idempotent** on the
  table's natural key (replay-safe), never blind insert.
- `[VC:enum]` columns store the short string token. PII columns carry the schema's `[PII-hash]` /
  `[PII-scrub]` tags; analytics never decrypts them — erasure is `gdpr-crypto.md`'s job (§6).
- Responses are `StatusResponseDto<T>` or `PaginatedResponse<T>`; list endpoints page via `PageRequestDto` →
  `PaginationParams` and return `PagedList<T>`.
- Controllers: `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/...")]`, `[Authorize]`, inherit
  `BaseController`, return through `ResultResponse` / `GetPaginatedResponse`.
- DI via typed interfaces (NO MediatR, no Roslyn). M.1/M.3 soft-delete via `SoftDeletableEntity` + global
  filter; append-only tables carry `CreatedAt` only.
- The single injected clock is `TimeProvider` (`platform-conventions.md` §3.11) — drives "today" for daily
  rollup bucketing; never `DateTimeOffset.UtcNow`.

---

## 1. Entities

This subsystem **owns** six Domain M read-model tables, each rebuilt by a projection that folds journaled
events. **Data is permanent** (no retention/auto-purge — `2026-06-16-gdpr-and-data.md`); the only removal is
manual GDPR erasure, which scrubs PII on the M.1 anchor and leaves the no-PII channel aggregate (M.8) intact.

| Table | Kind | Projection | Folded from (journaled events) |
|---|---|---|---|
| **M.1 `ViewerProfiles`** `[soft-delete]` | per-viewer aggregate (anonymization anchor) | `ViewerProfileProjection` | viewer activity, chat message, command executed, reward redeemed, song request fulfilled, follow, subscribe |
| **M.2 `WatchSessions`** `[APPEND-ONLY]` | per-stream attendance window | `WatchSessionProjection` | `stream.online`/`stream.offline` (session bounds) + viewer activity (presence) |
| **M.3 `WatchStreaks`** | consecutive-stream streak | `WatchStreakProjection` | per-stream attendance close (folds from completed M.2 sessions' stream-day) |
| **M.4 `MessageActivityDaily`** | per-viewer daily message count | `MessageActivityDailyProjection` | chat message |
| **M.7 `ViewerEngagementDaily`** | per-viewer daily engagement rollup | `ViewerEngagementDailyProjection` | chat message, command executed, reward redeemed, song request, currency earned/spent, game played |
| **M.8 `ChannelAnalyticsDaily`** (no PII) | per-channel daily rollup | `ChannelAnalyticsDailyProjection` | all of the above + follow/subscribe/cheer + presence (UniqueChatters, PeakViewers) |

References (owned elsewhere, analytics **reads** only): **M.5 `CommandUsage`** (written at execution by
`commands-pipelines.md` — analytics queries it for command drill-downs but does not project it; command
*aggregates* in M.1/M.4/M.7 fold from `CommandExecutedEvent`, not from M.5); **J.4/J.5** (`moderation.md`);
**L.1–L.3** (`economy.md`); **EventJournal/ProjectionCheckpoint** (`event-store.md`); **CryptoKey/erasure**
(`gdpr-crypto.md`).

### 1.1 Watch-session presence (the one modeling decision)

Twitch exposes no reliable per-viewer presence stream, so a watch session is **derived**, not received:
`WatchSessionProjection` opens a session for a viewer on their **first activity event** (chat / command /
redemption) inside a live window (between `stream.online` and `stream.offline`) and extends `EndedAt` on each
subsequent activity; `stream.offline` closes all open sessions for the channel. `PresenceConfirmed = true`
once the viewer has ≥ 2 activity events spaced ≥ 60 s apart in the window (the anti-AFK basis economy's
watch-time earning consumes). `DurationSeconds` = `EndedAt − StartedAt`. This makes watch-time a function of
**demonstrated** presence (chat/interaction), never a lurker heuristic — deterministic and replay-stable.

### 1.2 Daily rollup bucketing

Daily tables (M.4, M.7, M.8) bucket by `ActivityDate` = the **channel-local** calendar date of the event's
`OccurredAt` (the broadcaster's timezone from `Channels`, falling back to UTC). Each projection upserts the
`(BroadcasterId, [ViewerUserId,] ActivityDate)` row, incrementing counters — idempotent on replay because the
journal is replayed in order and each event carries a stable `EventId` the projection dedupes against the
checkpoint (`event-store.md` §3.3: re-apply is an upsert, never a blind `+1`).

---

## 2. Domain events

**None.** Analytics is a pure read-side projector: it consumes the existing event catalogue and emits **no new
domain events** (consistent with `event-store.md` §2 — "the store does not invent per-action events"). Live
dashboard updates are pushed by the projections through `DashboardHub` (SignalR) as a side effect, not as
journaled events. Projection lifecycle (rebuild/lag/fault) is surfaced by the event-store's existing
`ReplayStatusChangedEvent` + `IProjectionRunner` checkpoints, not re-invented here.

---

## 3. Service interfaces

All interfaces in `NomNomzBot.Application/Contracts/Analytics/`; implementations in
`NomNomzBot.Infrastructure/Analytics/` (projections) and `NomNomzBot.Infrastructure/Services/Analytics/`
(read services). Every fallible op returns `Result`/`Result<T>`.

### 3.1 Projections (each implements `event-store.md` §3.3 `IProjection`)

Six classes, all `IsGlobal = false` (per-tenant checkpoint), registered multi like every `IProjection`. Each
declares its `Name` (= `ProjectionCheckpoint.ProjectionName`), its `SubscribedEventTypes` (the runner skips
others), an idempotent `ApplyAsync` (upsert on the table's natural key), and `ResetAsync` (truncate the
scope's rows before a rebuild-from-zero). They hold **no read API** — they only build tables.

| Projection | `Name` | Natural key (upsert) | Builds |
|---|---|---|---|
| `ViewerProfileProjection` | `viewer-profile` | `(BroadcasterId, ViewerUserId)` | M.1 — first/last seen, lifetime totals, follower/sub flags, sub tier |
| `WatchSessionProjection` | `watch-session` | `(BroadcasterId, ViewerUserId, StreamId, StartedAt)` | M.2 — §1.1 derivation |
| `WatchStreakProjection` | `watch-streak` | `(BroadcasterId, UserId)` | M.3 — current/max streak from per-stream attendance |
| `MessageActivityDailyProjection` | `message-activity-daily` | `(BroadcasterId, ViewerUserId, ActivityDate)` | M.4 |
| `ViewerEngagementDailyProjection` | `viewer-engagement-daily` | `(BroadcasterId, ViewerUserId, ActivityDate)` | M.7 |
| `ChannelAnalyticsDailyProjection` | `channel-analytics-daily` | `(BroadcasterId, ActivityDate)` | M.8 |

`ViewerProfileProjection` honors `ViewerProfiles.IsAnalyticsOptedOut`: when a viewer is opted out the projection
still maintains the **anchor row** (so erasure has a target and follower/sub flags stay correct) but **stops
incrementing** the per-viewer engagement counters in M.1/M.4/M.7 for that viewer; the no-PII channel aggregate
(M.8) keeps counting (it carries no viewer identity). Opt-out is read from the profile; the toggle itself is
set via §3.2.

### 3.2 `IViewerAnalyticsService` — per-viewer read + opt-out

```csharp
namespace NomNomzBot.Application.Contracts.Analytics;

public interface IViewerAnalyticsService
{
    // One viewer's aggregate profile (M.1) for this channel. NOT_FOUND if the viewer never appeared.
    Task<Result<ViewerProfileDto>> GetProfileAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    // Ranked/filtered viewer list (M.1) — top by watch-time/messages/commands/redemptions. Paged.
    Task<Result<PagedList<ViewerProfileListItemDto>>> ListProfilesAsync(Guid broadcasterId, ViewerProfileQuery query, PaginationParams paging, CancellationToken ct = default);

    // One viewer's daily engagement series (M.7) over a date range (inclusive, channel-local dates).
    Task<Result<IReadOnlyList<ViewerEngagementDailyDto>>> GetEngagementSeriesAsync(Guid broadcasterId, Guid viewerUserId, DateOnly from, DateOnly to, CancellationToken ct = default);

    // One viewer's attendance streak (M.3).
    Task<Result<WatchStreakDto>> GetStreakAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    // Viewer-controlled opt-out of per-viewer analytics (sets M.1 IsAnalyticsOptedOut). Aggregates (M.8) are
    // unaffected. Idempotent.
    Task<Result> SetAnalyticsOptOutAsync(Guid broadcasterId, Guid viewerUserId, bool optedOut, CancellationToken ct = default);
}
```

### 3.3 `IChannelAnalyticsService` — per-channel read

```csharp
namespace NomNomzBot.Application.Contracts.Analytics;

public interface IChannelAnalyticsService
{
    // Channel daily aggregate series (M.8) over a date range — the chart/time-series source.
    Task<Result<IReadOnlyList<ChannelAnalyticsDailyDto>>> GetDailySeriesAsync(Guid broadcasterId, DateOnly from, DateOnly to, CancellationToken ct = default);

    // Headline summary over a range: totals + deltas vs the preceding equal-length window (folded from M.8).
    Task<Result<ChannelAnalyticsSummaryDto>> GetSummaryAsync(Guid broadcasterId, DateOnly from, DateOnly to, CancellationToken ct = default);

    // Top viewers for the channel over a range by a chosen metric (folds M.7) — leaderboard-adjacent but NOT
    // the economy leaderboard (no currency ranking, no opt-out-config; respects M.1 IsAnalyticsOptedOut).
    Task<Result<IReadOnlyList<TopViewerDto>>> GetTopViewersAsync(Guid broadcasterId, TopViewerMetric metric, DateOnly from, DateOnly to, int top, CancellationToken ct = default);
}
```

### 3.4 `IPlatformAnalyticsService` — SaaS-only cross-channel stats

Platform-global basic stats for the SaaS operator dashboard — cross-tenant, **Plane C** (platform IAM) gated.
On **self-host** the implementation is the `NullPlatformAnalyticsService` adapter that returns
`FEATURE_DISABLED` (a self-host operator sees only their own channel via §3.3; there is no cross-tenant view).
On **SaaS** it reads the global stream / `ChannelAnalyticsDaily` across tenants.

```csharp
namespace NomNomzBot.Application.Contracts.Analytics;

public interface IPlatformAnalyticsService
{
    // Cross-tenant basic stats: active channels, total events processed, daily-active channels, aggregate
    // message/redemption/command volume. No per-viewer PII crosses the tenant boundary.
    Task<Result<PlatformAnalyticsDto>> GetPlatformStatsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

---

## 4. DTOs / contracts

`NomNomzBot.Application/Contracts/Analytics/`, serialized **Newtonsoft.Json**.

### Responses

- `ViewerProfileDto` — `ViewerUserId, ViewerTwitchUserId, DisplayName?, FirstSeenAt?, LastSeenAt?,
  TotalWatchSeconds, TotalMessages, TotalCommandsUsed, TotalRedemptions, TotalSongRequests, IsFollower,
  IsSubscriber, SubTier?, IsAnalyticsOptedOut`.
- `ViewerProfileListItemDto` — `ViewerUserId, DisplayName?, TotalWatchSeconds, TotalMessages, LastSeenAt?`.
- `ViewerEngagementDailyDto` — `ActivityDate, WatchSeconds, MessageCount, CommandCount, RedemptionCount,
  SongRequestCount, CurrencyEarned, CurrencySpent, GamesPlayed`.
- `WatchStreakDto` — `CurrentStreak, MaxStreak, LastSeenDate`.
- `ChannelAnalyticsDailyDto` — `ActivityDate, UniqueChatters, TotalMessages, TotalWatchSeconds, NewFollowers,
  NewSubscribers, BitsCheered, CommandsRun, RedemptionsCount, SongRequests, CurrencyEarnedTotal,
  CurrencySpentTotal, GamesPlayed, PeakViewers?`.
- `ChannelAnalyticsSummaryDto` — the M.8 counters summed over the range **plus** a `Deltas` block (% change vs
  the preceding equal window) and `PeakViewers` (max over range).
- `TopViewerDto` — `ViewerUserId, DisplayName?, MetricValue` (the chosen `TopViewerMetric`).
- `PlatformAnalyticsDto` — `ActiveChannels, DailyActiveChannels, TotalEventsProcessed, TotalMessages,
  TotalRedemptions, TotalCommandsRun` over the range (no per-tenant identity).

### Requests / queries

- `ViewerProfileQuery` — `string? Search` (display-name prefix), `ViewerProfileSort Sort`
  (`watch|messages|commands|redemptions|last_seen`), `bool? FollowersOnly, SubscribersOnly`.
- `TopViewerMetric` enum — `watch_seconds | messages | commands | redemptions | currency_earned`.
- `DateOnly from/to` range params (validated `from ≤ to`, span ≤ 366 days → `VALIDATION_FAILED`).

---

## 5. Controller endpoints

New `AnalyticsController` (channel-scoped) + `PlatformAnalyticsController` (platform) under
`NomNomzBot.Api/Controllers/V1/`, `[ApiVersion("1.0")]`, inherit `BaseController`, `[Authorize]`. Channel
`{channelId}` → `Guid broadcasterId` via tenant middleware + `IChannelAccessService`.

**Role gate** (schema B.3 `ActionDefinitions`). Channel keys are **management** plane (read-only dashboard
data — `Moderator` floor); the platform endpoint is **Plane C** (`[Authorize(Policy="platform:analytics:read")]`,
the policy name IS the IAM permission key). Self-or-Gate-2: a viewer may read **their own** profile/streak/
engagement without a management role (the `{viewerUserId}` equals the caller's user id); otherwise the
management floor applies. Gate-2 = `IActionAuthorizationService.AuthorizeActionAsync`.

### AnalyticsController — `api/v{version}/channels/{channelId}/analytics`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/channel/daily` | `?from&to` | `StatusResponseDto<IReadOnlyList<ChannelAnalyticsDailyDto>>` | management / Moderator · `analytics:read` |
| GET | `/channel/summary` | `?from&to` | `StatusResponseDto<ChannelAnalyticsSummaryDto>` | management / Moderator · `analytics:read` |
| GET | `/channel/top-viewers` | `?metric&from&to&top` | `StatusResponseDto<IReadOnlyList<TopViewerDto>>` | management / Moderator · `analytics:read` |
| GET | `/viewers` | `ViewerProfileQuery`+`PageRequestDto` | `PaginatedResponse<ViewerProfileListItemDto>` | management / Moderator · `analytics:viewer:read` |
| GET | `/viewers/{viewerUserId}` | — | `StatusResponseDto<ViewerProfileDto>` | management / Moderator · `analytics:viewer:read` (self-or-Gate-2) |
| GET | `/viewers/{viewerUserId}/engagement` | `?from&to` | `StatusResponseDto<IReadOnlyList<ViewerEngagementDailyDto>>` | management / Moderator · `analytics:viewer:read` (self-or-Gate-2) |
| GET | `/viewers/{viewerUserId}/streak` | — | `StatusResponseDto<WatchStreakDto>` | management / Moderator · `analytics:viewer:read` (self-or-Gate-2) |
| POST | `/viewers/{viewerUserId}/opt-out` | `{ bool optedOut }` | `StatusResponseDto<object>` | management / Moderator · `analytics:viewer:read` (self-or-Gate-2) |

### PlatformAnalyticsController — `api/v{version}/platform/analytics`

| Verb | Route | Request | Response | Plane / key |
|---|---|---|---|---|
| GET | `/stats` | `?from&to` | `StatusResponseDto<PlatformAnalyticsDto>` | Plane C · `platform:analytics:read` (SaaS only; self-host → `FEATURE_DISABLED`) |

---

## 6. GDPR / anonymization touchpoint

`ViewerProfiles` (M.1) is the schema's **anonymization anchor**. Analytics does not own erasure — on a
`gdpr-crypto.md` erasure request, that subsystem scrubs the M.1 `[PII-scrub]` snapshots
(`UsernameSnapshot`/`DisplayNameSnapshot`) and neutralizes the `[PII-hash]` `ViewerTwitchUserId` for the
subject across M.1/M.3 and the `ArgsSnapshot` on M.5, then crypto-shreds the subject DEK. The **counts survive**
(they are not PII): M.8 carries no viewer identity at all, and M.1/M.4/M.7 keep their aggregates against an
anonymized anchor. Analytics' only obligation is that its projections **never re-materialize** scrubbed PII on
replay — the projections read identity from the (now anonymized) journal payloads via
`IEventPayloadProtector`, so a shredded payload projects as anonymized. No analytics endpoint returns raw PII
beyond the display-name snapshot the dashboard already shows.

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs`. Projections in the `// Event store projections` block
(scoped, multi-registered like every `IProjection`); read services in the "Application services" block
(scoped). Implementations in `NomNomzBot.Infrastructure/Analytics/` and `.../Services/Analytics/`.

```csharp
// Analytics — projections (scoped, multi-registered IProjection)
services.AddScoped<IProjection, ViewerProfileProjection>();
services.AddScoped<IProjection, WatchSessionProjection>();
services.AddScoped<IProjection, WatchStreakProjection>();
services.AddScoped<IProjection, MessageActivityDailyProjection>();
services.AddScoped<IProjection, ViewerEngagementDailyProjection>();
services.AddScoped<IProjection, ChannelAnalyticsDailyProjection>();

// Analytics — read services (scoped: DbContext)
services.AddScoped<IViewerAnalyticsService, ViewerAnalyticsService>();
services.AddScoped<IChannelAnalyticsService, ChannelAnalyticsService>();

// Platform stats — deployment-profile adapter (SaaS impl vs self-host null)
services.AddScoped<IPlatformAnalyticsService, PlatformAnalyticsService>();      // SaaS
// self_host_* profile overrides with NullPlatformAnalyticsService (FEATURE_DISABLED)
```

The projections run under the event-store's existing `IProjectionRunner` (no bespoke hosted service);
checkpoints, lag, rebuild, and pause/resume are the event-store's surface.

---

## 8. Dependencies (from the stack doc)

- **`event-store.md`** — `IProjection`/`IProjectionRunner`/`ProjectionCheckpoint` (the projection runtime),
  `EventJournal` (the only replay source), `IEventPayloadProtector` (reads identity from journal payloads,
  honoring crypto-shred).
- **`commands-pipelines.md`** — emits `CommandExecutedEvent` (folded into M.1/M.4/M.7) and writes M.5
  `CommandUsage` at the source (analytics reads it for drill-downs).
- **`rewards.md`** — `RewardRedeemedEvent` → `TotalRedemptions` / `RedemptionCount`.
- **`twitch-eventsub.md`** — `stream.online`/`stream.offline` (session bounds), follow/subscribe/cheer events
  (M.8 counters), chat-message event (presence + M.4).
- **`economy.md`** — currency earned/spent + game-played events (M.7/M.8 columns). Analytics does **not** read
  economy's tables; it folds the journaled events.
- **`gdpr-crypto.md`** — erasure scrubs the M.1 anchor; analytics projections respect shredded payloads.
- **`monetization-billing.md`** — owns N.5 usage metering; analytics provides **no** metering (decoupled).
- **`TimeProvider`** — channel-local "today" bucketing.

---

## 9. Decisions (resolved)

1. **Analytics owns Domain M only** (M.1, M.2, M.3, M.4, M.7, M.8 as projections). J.4/J.5 + Heat/Trust stay
   with `moderation.md`; N.5 usage stays with `monetization-billing.md`; L.* leaderboards stay with
   `economy.md`. The `_GAP-AUDIT` M2 over-assignment is corrected here.
2. **No HeatScore formula here.** `HeatScore`/`TrustScore` are J.5 (moderation), computed by the canonical
   `TrustScoreCalculator`. Analytics neither stores nor formulas them. (If the heat-accumulation weighting is
   genuinely unspecified, that gap belongs in `moderation.md`, the J.5 owner — not here.)
3. **Watch sessions are derived from demonstrated activity inside live windows** (§1.1), not from a phantom
   presence stream; `PresenceConfirmed` needs ≥ 2 activity events ≥ 60 s apart. Deterministic + replay-stable.
4. **Daily rollups bucket by channel-local date** and upsert idempotently — replay rebuilds identical numbers.
5. **Pure read-side: zero new domain events.** Live updates push via `DashboardHub`; projection lifecycle uses
   the event-store's existing replay events + checkpoints.
6. **Platform (cross-channel) analytics is SaaS-only**, Plane-C gated; self-host gets the
   `NullPlatformAnalyticsService` (`FEATURE_DISABLED`) and sees only its own channel.
7. **Permanent storage, manual erasure only** — no retention/auto-purge; M.1 is the anonymization anchor,
   counts survive erasure (M.8 is PII-free by construction).
