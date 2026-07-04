# Community & Dashboard — Interface Specification

Implementable spec for **two read-only aggregation surfaces**: the **community/viewer** surface
(`CommunityController` — chatters/followers/subscribers/VIPs/moderators from real Twitch data, viewer detail
folding analytics + standing + role + recent activity) and the **dashboard home** surface
(`DashboardController` — live-stream summary, today's stats, recent activity feed, top viewers/earners, active
alerts). Both **own no schema and no domain events** — they compose existing read models, live Helix reads, and
the journal. Code from this directly.

Source of truth: the read models these controllers aggregate are owned by their respective specs —
`twitch-helix.md` (live Helix chatters/followers/subs/VIPs/moderators reads), `analytics.md` (Domain **M**
projections: M.1 `ViewerProfiles`, M.7 `ViewerEngagementDaily`, M.8 `ChannelAnalyticsDaily`), `roles-permissions.md`
(B.2 `ChannelCommunityStandings`, B.1 `ChannelMemberships`), `event-store.md` (O.1 `EventJournal` filtered read),
`economy.md` (L.1–L.3 leaderboards). Library choices: `2026-06-16-stack-and-dependencies.md`. Conforms to the
resolved cross-cutting decisions in `2026-06-16-decisions-pending-confirmation.md`. These two controllers exist
today as thin shells; this spec gives them a typed service layer over the now-owned read models (replacing the
ad-hoc Helix/DbContext calls the legacy shells made).

## Binding conventions (every signature below obeys these)

- Namespace root `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way
  (never `.Result`/`.Wait`).
- Fallible operations return `Result` / `Result<T>` (`NomNomzBot.Application.Common.Models`). Never null,
  never throw for expected failure. Error codes reuse `BaseController.ResultResponse`'s known set
  (`NOT_FOUND`, `VALIDATION_FAILED`, `FORBIDDEN`, `FEATURE_DISABLED`, `RATE_LIMITED`, …) plus the Helix-surfaced
  codes these services propagate unchanged from `ITwitchHelixClient` (`missing_scope`, `no_token`,
  `rate_limited`, `not_found`).
- **Tenant key `BroadcasterId` is `Guid`** (locked schema §1.1 — `ITenantScoped.BroadcasterId` widened
  `string`→`Guid`). Viewer/user ids are internal `Guid`; Twitch ids are indexed `string` attribute columns,
  never keys — surfaced on DTOs as `*TwitchUserId` strings.
- **No new schema, no new surrogate PKs.** Neither service persists anything (§1 states this explicitly); both
  are pure aggregation over read models owned elsewhere. No `IUnitOfWork` write, no soft-delete concern.
- Repository / `IApplicationDbContext` reads only; no raw `DbContext` in controllers — the read services hold
  the queries. List endpoints page via `PageRequestDto` → `PaginationParams` and return `PagedList<T>` from
  services (rendered as `PaginatedResponse<T>`). **Live Helix lists** (chatters/followers/subscribers) page
  through Twitch's own cursor — these services translate `PaginationParams` ↔ the Helix `TwitchPageRequest`
  cursor (§3.3) so the controller surface stays uniform.
- `[VC:enum]` read columns surface as their domain enum on the DTO (`CommunityStanding`, `ManagementRole`).
- Responses are `StatusResponseDto<T>` or `PaginatedResponse<T>`; serialized **Newtonsoft.Json**.
- Controllers: `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/...")]`, `[Authorize]`, inherit
  `BaseController`, return through `ResultResponse` / `GetPaginatedResponse`.
- DI via typed interfaces (NO MediatR, no Roslyn). The single injected clock is `TimeProvider`
  (`platform-conventions.md` §3.11) — drives the channel-local "today" boundary for the dashboard's daily
  stats; never `DateTimeOffset.UtcNow`.

---

## 1. Entities

**None new — both services own zero schema.** They are read-only aggregators; everything they return is folded
from tables and read models owned by other specs, or read live from Helix. No table, column, PK, projection, or
soft-delete filter is introduced here. The complete set of read dependencies (never mutated by this subsystem):

| Read dependency | Owner spec | What this subsystem reads from it |
|---|---|---|
| Live Helix chatters / followers / subscribers / VIPs / moderators | `twitch-helix.md` §3.2–3.4 | The community lists — `GetChattersAsync`, `GetFollowersAsync`, `GetSubscribersAsync`, `GetVipsAsync`, `GetModeratorsAsync` (real Twitch data; **no seed/fake list, ever**) |
| Live channel/stream state | `twitch-helix.md` §3.2 | `GetChannelInformationAsync` / `GetStreamAsync` for the dashboard live-summary widget |
| **M.1 `ViewerProfiles`** (per-viewer aggregate) | `analytics.md` §3.2 | `IViewerAnalyticsService.GetProfileAsync` — the viewer detail's lifetime totals, first/last seen, follower/sub flags |
| **M.7 `ViewerEngagementDaily`** | `analytics.md` §3.2 | viewer-detail engagement series (optional drill-down range) |
| **M.8 `ChannelAnalyticsDaily`** | `analytics.md` §3.3 | `IChannelAnalyticsService.GetSummaryAsync` / `GetTopViewersAsync` — dashboard "today's stats" + top viewers |
| **B.2 `ChannelCommunityStandings`** | `roles-permissions.md` §3.5 | `ICommunityStandingService.GetStandingAsync` — viewer detail's `CommunityStanding` |
| **B.1 `ChannelMemberships`** | `roles-permissions.md` §3.4 | `IRoleResolver.ResolveAccessAsync` — viewer detail's `ManagementRole` (+ full plane breakdown) |
| **O.1 `EventJournal`** (filtered read) | `event-store.md` §3.1 | `IEventJournal.QueryAsync(EventJournalQuery)` → `PagedList<EventRecord>` — viewer recent-activity + dashboard recent-activity feed |
| **L.1–L.3 leaderboards** | `economy.md` §3.8 | `IEconomyLeaderboardService.GetRankingAsync` — dashboard "top earners" widget |

> The legacy `CommunityController`/`DashboardController` reached into `ITwitchApiService` and `DbContext`
> directly. This spec repoints them at the **owned** read services above; the controllers themselves never touch
> Helix or EF directly (the read services do).

---

## 2. Domain events

**None.** Both services are pure read-side aggregators — no state change, so nothing to emit (consistent with
`analytics.md` §2 and `event-store.md` §2: read surfaces do not invent per-action events). Live dashboard
freshness is pushed by the **existing** `DashboardHub` (SignalR), driven by the projections and event handlers
that already journal their own events — this subsystem neither owns nor extends that hub's event vocabulary; it
serves the REST read snapshot the dashboard loads on open and the hub keeps fresh.

---

## 3. Service interfaces

Interfaces in `NomNomzBot.Application/Contracts/Community/` and `NomNomzBot.Application/Contracts/Dashboard/`;
implementations in `NomNomzBot.Infrastructure/Services/Community/` and `.../Services/Dashboard/`. Every fallible
op returns `Result`/`Result<T>`. Neither service calls Helix or EF directly for anything another spec owns — they
**compose** the typed read services in §1. No method writes.

### 3.1 `ICommunityService` — viewer/community surface

```csharp
namespace NomNomzBot.Application.Contracts.Community;

public interface ICommunityService
{
    // Live present-viewer list (Helix chatters). Maps PaginationParams to the Helix cursor (§3.3); each item
    // is enriched with the local CommunityStanding/ManagementRole if the viewer is known locally (else
    // Everyone / null). missing_scope (moderator:read:chatters) propagates unchanged — NOT a silent empty list.
    Task<Result<PagedList<CommunityMemberDto>>> ListChattersAsync(Guid broadcasterId, PaginationParams paging, CancellationToken ct = default);

    // Live followers (Helix), newest first, paged via the Helix cursor. Enriched as above.
    Task<Result<PagedList<CommunityMemberDto>>> ListFollowersAsync(Guid broadcasterId, PaginationParams paging, CancellationToken ct = default);

    // Live subscribers (Helix). Requires channel:read:subscriptions — propagates missing_scope (closes the
    // "subscriber count always 0" silent-failure). Each item carries Tier/IsGift from the Helix DTO.
    Task<Result<PagedList<CommunitySubscriberDto>>> ListSubscribersAsync(Guid broadcasterId, PaginationParams paging, CancellationToken ct = default);

    // Live VIPs (Helix GetVipsAsync — single page of up to 100, returned as one PagedList page).
    Task<Result<PagedList<CommunityMemberDto>>> ListVipsAsync(Guid broadcasterId, PaginationParams paging, CancellationToken ct = default);

    // Live moderators (Helix GetModeratorsAsync). Same shape as VIPs.
    Task<Result<PagedList<CommunityMemberDto>>> ListModeratorsAsync(Guid broadcasterId, PaginationParams paging, CancellationToken ct = default);

    // One viewer's full detail: the analytics ViewerProfile (M.1) + resolved CommunityStanding (B.2) +
    // ManagementRole / plane breakdown (B.1 via IRoleResolver) + the viewer's recent activity (EventJournal
    // rows where ActorUserId == viewer, newest first, capped). NOT_FOUND if the viewer never appeared in this
    // channel (no ViewerProfile anchor). Composes IViewerAnalyticsService + ICommunityStandingService +
    // IRoleResolver + IEventJournal — never re-derives any of them.
    Task<Result<ViewerDetailDto>> GetViewerAsync(Guid broadcasterId, Guid viewerUserId, int recentActivityLimit = 25, CancellationToken ct = default);
}
```

### 3.2 `IDashboardService` — dashboard home aggregation

```csharp
namespace NomNomzBot.Application.Contracts.Dashboard;

public interface IDashboardService
{
    // The full dashboard-home snapshot in one call (the page-open payload): live-stream summary + today's
    // stats + recent activity feed + top viewers + top earners + active alerts. Each sub-section is best-effort
    // and degrades independently (§3.4): a missing-scope or rate-limited Helix leg yields a null LiveStream
    // with a Degraded note rather than failing the whole snapshot. Returns Result.Failure only on a hard
    // tenant/authorization fault.
    Task<Result<DashboardSnapshotDto>> GetSnapshotAsync(Guid broadcasterId, CancellationToken ct = default);

    // Just the live-stream + today's-stats header (cheap, polled more often than the full snapshot).
    Task<Result<DashboardSummaryDto>> GetSummaryAsync(Guid broadcasterId, CancellationToken ct = default);

    // The channel-wide recent activity feed (EventJournal rows for this tenant, newest first, paged).
    // Distinct from the viewer-scoped feed in ICommunityService.GetViewerAsync (which filters by ActorUserId).
    Task<Result<PagedList<ActivityItemDto>>> GetRecentActivityAsync(Guid broadcasterId, PaginationParams paging, CancellationToken ct = default);
}
```

### 3.3 Helix-cursor ↔ `PaginationParams` mapping (the one mechanical decision)

Helix list reads (`GetChattersAsync`, `GetFollowersAsync`, `GetSubscribersAsync`) page by an **opaque cursor**
(`TwitchPageRequest.After`), not by `(page, pageSize)`. To keep the controller surface uniform with the locally
paged endpoints, `ICommunityService` treats `PaginationParams.PageSize` as the Helix `first` and accepts the
opaque Helix `After` cursor as the page token, surfacing the next cursor on the `PagedList` so the dashboard can
request the next page. `PagedList<T>.TotalCount` carries Helix's reported `Total` where Twitch provides it
(followers/subs) and is `-1` ("unknown, cursor-driven") for chatters (Twitch reports no total for the chatter
list). VIPs/moderators are bounded single-page Helix reads and return one `PagedList` page. This is the only
adaptation; no Helix call is re-implemented here (all go through `ITwitchHelixClient`, `twitch-helix.md`).

### 3.4 Degradation & enrichment rules (binding behavior)

- **No fake data — hard rule.** Every community list comes from a live Helix read. A Helix failure surfaces as a
  `Result.Failure(ErrorCode)` (or, for the dashboard snapshot's best-effort legs, a `Degraded` note + null
  section) — **never** a fabricated viewer/subscriber/follower list. `missing_scope` is propagated, not masked.
- **Enrichment is a left-join, never a gate.** A Helix chatter/follower with no local `ViewerProfile`/standing
  row is still returned (standing `Everyone`, `ManagementRole` null) — the community list is the Twitch truth,
  decorated where local data exists, not filtered to locally-known users.
- **Dashboard snapshot is best-effort per section.** `GetSnapshotAsync` composes five independent reads; any one
  failing (Helix scope/rate-limit, an empty leaderboard) nulls that section with a typed `Degraded` reason and
  the rest still render. Only a tenant/authorization fault fails the whole call.
- **Recent activity is journal-sourced, display-safe.** Feed items are projected from `EventRecord` to a thin
  `ActivityItemDto` (type token, actor display snapshot, occurred-at, a short summary) — the raw `PayloadJson`
  and any `[PII-hash]`/encrypted payload fields are **never** surfaced through these read endpoints; only the
  already-display-safe snapshot fields the journal carries are mapped.

---

## 4. DTOs / contracts

Responses in `NomNomzBot.Application/Contracts/Community/` and `.../Contracts/Dashboard/`, serialized
**Newtonsoft.Json**. Cross-spec types (`ViewerProfileDto`, `ResolvedAccessDto`, `LeaderboardEntryDto`,
`ChannelAnalyticsSummaryDto`, `TopViewerDto`, `TwitchSubscriberDto`) are **referenced, not redefined** — they
live in their owner specs.

### Community responses

```csharp
namespace NomNomzBot.Application.Contracts.Community;

// One row in a community list (chatters/followers/VIPs/mods). Helix identity + local enrichment.
public sealed record CommunityMemberDto(
    Guid? UserId,                    // internal id when the viewer is known locally; null if Helix-only
    string TwitchUserId,             // Helix is the source of truth for identity
    string Login,
    string DisplayName,
    CommunityStanding Standing,      // resolved B.2 standing; Everyone when unknown locally
    ManagementRole? ManagementRole,  // resolved B.1 role; null when not a manager
    bool IsFollower,
    bool IsSubscriber);

// Subscriber list row — adds the Helix subscription facts.
public sealed record CommunitySubscriberDto(
    Guid? UserId,
    string TwitchUserId,
    string Login,
    string DisplayName,
    string Tier,                     // "1000" | "2000" | "3000"
    bool IsGift,
    string? GifterTwitchUserId,
    CommunityStanding Standing,
    ManagementRole? ManagementRole);

// The viewer-detail composite. Each block is the owner spec's type, referenced not redefined.
public sealed record ViewerDetailDto(
    ViewerProfileDto Profile,                          // analytics.md M.1
    CommunityStanding CommunityStanding,               // roles-permissions.md B.2
    ManagementRole? ManagementRole,                    // roles-permissions.md B.1
    ResolvedAccessDto Access,                          // roles-permissions.md — full plane breakdown
    IReadOnlyList<ActivityItemDto> RecentActivity);    // EventJournal rows for this viewer, newest first
```

### Dashboard responses

```csharp
namespace NomNomzBot.Application.Contracts.Dashboard;

// A display-safe projection of one EventJournal row (event-store.md EventRecord) — never the raw payload.
public sealed record ActivityItemDto(
    Guid EventId,
    string EventType,                // e.g. "channel.follow", "reward.redeemed"
    string? ActorDisplayName,        // already-display-safe snapshot; null for system events
    string? ActorTwitchUserId,
    string Summary,                  // short human-readable line built from the type + safe snapshot fields
    DateTime OccurredAt);

// Live stream/channel header (from Helix; null section when Helix leg is degraded).
public sealed record LiveStreamSummaryDto(
    bool IsLive,
    string? Title,
    string? GameName,
    int ViewerCount,
    DateTime? StartedAt,
    int FollowerCount,
    int SubscriberCount);

// "Today's stats" — the channel-local current-day slice of M.8 (analytics.md ChannelAnalyticsSummaryDto folded
// to a single day). Referenced shape, surfaced as the dashboard's today block.
public sealed record DashboardSummaryDto(
    LiveStreamSummaryDto? LiveStream,                  // null + Degraded note if Helix leg failed
    ChannelAnalyticsSummaryDto? Today,                 // analytics.md — today's M.8 summary; null if unavailable
    IReadOnlyList<DashboardSectionNote> Degraded);     // per-section degradation reasons (empty = all healthy)

// The full page-open snapshot.
public sealed record DashboardSnapshotDto(
    LiveStreamSummaryDto? LiveStream,
    ChannelAnalyticsSummaryDto? Today,                 // analytics.md M.8 today summary
    IReadOnlyList<ActivityItemDto> RecentActivity,     // newest-first, capped
    IReadOnlyList<TopViewerDto> TopViewers,            // analytics.md — top viewers (today/range)
    IReadOnlyList<LeaderboardEntryDto> TopEarners,     // economy.md — currency leaderboard ranking
    IReadOnlyList<DashboardAlertDto> Alerts,           // active alerts (see below)
    IReadOnlyList<DashboardSectionNote> Degraded);

// An active alert surfaced on the dashboard — composed from existing health/connection signals, NOT a new
// store. Sourced from the integration-connection health the dashboard already observes (e.g. a Twitch
// needs_reauth / missing-scope state, a paused projection). Read-only; no alert table is introduced.
public sealed record DashboardAlertDto(
    string Kind,                     // "twitch_reauth" | "missing_scope" | "projection_paused" | ...
    string Severity,                 // "info" | "warning" | "error"
    string Message,
    string? ActionHint);             // e.g. "Reconnect Twitch" — a UI affordance hint, not a route

public sealed record DashboardSectionNote(
    string Section,                  // "live_stream" | "today" | "top_earners" | ...
    string Reason);                  // e.g. "missing_scope", "rate_limited", "feature_disabled"
```

> `DashboardAlertDto` composes signals the platform already produces (Twitch connection status from
> `IntegrationConnections`, paused/faulted projections from `IProjectionRunner` checkpoints); it owns **no**
> storage. If a richer alert subsystem is ever introduced it would own its own table and this DTO would read it —
> but as specified the dashboard derives alerts on the fly from existing health reads.

---

## 5. Controller endpoints

Two controllers under `NomNomzBot.Api/Controllers/V1/`, `[ApiVersion("1.0")]`, inherit `BaseController`,
`[Authorize]`, route through `ResultResponse`/`GetPaginatedResponse`. Channel `{channelId}` resolves to
`Guid broadcasterId` via tenant middleware + `IChannelAccessService` (Gate 1; caller must control the channel).

**Role gate** (schema B.3 `ActionDefinitions`). Both surfaces are **management**-plane, read-only dashboard
data. **Gate 1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's). **Gate 2** =
`IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route
floor before the service call (403 `FORBIDDEN` below floor). Keys are seeded global `ActionDefinitions` (added to
`roles-permissions.md` §7.1 — see §7 deltas below); a broadcaster may raise a floor via `ChannelActionOverride`
but not below the seeded `FloorLevel`. Effective level = `MAX(community standing, management role, active permit
grant)`.

### CommunityController — `api/v{version}/channels/{channelId}/community`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/chatters` | `PageRequestDto` | `PaginatedResponse<CommunityMemberDto>` | management / Moderator · `community:read` |
| GET | `/followers` | `PageRequestDto` | `PaginatedResponse<CommunityMemberDto>` | management / Moderator · `community:read` |
| GET | `/subscribers` | `PageRequestDto` | `PaginatedResponse<CommunitySubscriberDto>` | management / Moderator · `community:read` |
| GET | `/vips` | `PageRequestDto` | `PaginatedResponse<CommunityMemberDto>` | management / Moderator · `community:read` |
| GET | `/moderators` | `PageRequestDto` | `PaginatedResponse<CommunityMemberDto>` | management / Moderator · `community:read` |
| GET | `/viewers/{viewerUserId}` | `?recentActivityLimit=` | `StatusResponseDto<ViewerDetailDto>` | management / Moderator · `community:read` |

### DashboardController — `api/v{version}/channels/{channelId}/dashboard`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<DashboardSnapshotDto>` | management / Moderator · `dashboard:read` |
| GET | `/summary` | — | `StatusResponseDto<DashboardSummaryDto>` | management / Moderator · `dashboard:read` |
| GET | `/activity` | `PageRequestDto` | `PaginatedResponse<ActivityItemDto>` | management / Moderator · `dashboard:read` |

A Helix-sourced read that hits a missing scope returns `403`/`409`/`429` problem-details by error code
(`missing_scope`→403, `no_token`→409, `rate_limited`→429, `not_found`→404) for the dedicated community list
endpoints; the **dashboard** snapshot/summary endpoints instead degrade per section (§3.4) and return `200` with
`Degraded` notes, because the page must render even when one widget's source is unavailable.

---

## 6. Pipeline actions

**None.** Both subsystems are read-only HTTP aggregation surfaces — they expose no pipeline action and no
template variable. (Community/standing data consumed inside pipelines is read via the owner services'
template-variable helpers, not added here.)

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs`, "Application services" block — **scoped** (per-request,
read-only; they compose other scoped read services). No projection registration (these own no read model), no
`ICommandAction`, no deployment-profile adapter pair (pure read aggregation, identical on self-host and SaaS).

```csharp
// Community & Dashboard — read-only aggregation services (scoped: compose owned read services)
services.AddScoped<ICommunityService, CommunityService>();
services.AddScoped<IDashboardService, DashboardService>();
```

Both implementations constructor-inject the **already-registered** owner interfaces — `ITwitchHelixClient`
(`twitch-helix.md`), `IViewerAnalyticsService` / `IChannelAnalyticsService` (`analytics.md`),
`ICommunityStandingService` / `IRoleResolver` (`roles-permissions.md`), `IEventJournal` (`event-store.md`),
`IEconomyLeaderboardService` (`economy.md`) — and depend on interfaces only (SOLID; no concrete service, no raw
`DbContext`). No new registration is added to any other spec's block.

---

## 8. Dependencies (from the stack doc)

- **`ITwitchHelixClient`** (`twitch-helix.md`) — `Channels.GetChattersAsync`/`GetFollowersAsync`/
  `GetChannelInformationAsync`/`GetVipsAsync`, `Streams.GetStreamAsync`, `Moderation.GetModeratorsAsync`,
  `Subscriptions.GetSubscribersAsync`/`GetSubscriberCountAsync`. Live Twitch reads (no seed data). Scope and
  rate-limit failures propagate as `Result.Failure(ErrorCode)`.
- **`IViewerAnalyticsService` / `IChannelAnalyticsService`** (`analytics.md`) — `ViewerProfileDto` (M.1) for
  viewer detail; `ChannelAnalyticsSummaryDto` (M.8) and `TopViewerDto` for the dashboard today/top-viewer blocks.
- **`ICommunityStandingService` / `IRoleResolver`** (`roles-permissions.md`) — `CommunityStanding` (B.2) and
  `ResolvedAccessDto`/`ManagementRole` (B.1) for the viewer detail + list enrichment.
- **`IEventJournal`** (`event-store.md`) — `QueryAsync(EventJournalQuery)` → `PagedList<EventRecord>` for the
  recent-activity feeds (viewer-scoped via `ActorUserId`, channel-wide via `BroadcasterId`). Read-only; this
  subsystem never appends.
- **`IEconomyLeaderboardService`** (`economy.md`) — `GetRankingAsync` → `LeaderboardEntryDto` for the
  top-earners widget.
- **`IActionAuthorizationService` / `IChannelAccessService`** (`roles-permissions.md`) — Gate 1 + Gate 2.
- **`TimeProvider`** — channel-local "today" boundary for the dashboard daily stats. Never
  `DateTimeOffset.UtcNow`.

**Net new dependency for this subsystem: zero** — it adds two read services that compose already-registered
interfaces.

---

## 9. Decisions (resolved)

1. **Both controllers are pure read-only aggregators that own nothing** — no schema, no domain events, no
   pipeline actions, no DI adapter pairs. They give the existing `CommunityController`/`DashboardController`
   shells a typed service layer (`ICommunityService`/`IDashboardService`) over read models owned by their proper
   specs, replacing the legacy ad-hoc Helix/`DbContext` calls.
2. **Community lists are live Helix truth, enriched by left-join — never seeded.** The list endpoints return the
   real Twitch chatter/follower/subscriber/VIP/moderator data, decorated with local standing/role where it
   exists; a Helix scope/rate failure surfaces as an error code, never a fabricated list (the hard "no fake
   community data" rule). This also closes the legacy "subscriber count always 0" silent-empty behavior by
   propagating `missing_scope`.
3. **Viewer detail is a composite of owned read models** — `ViewerProfileDto` (analytics M.1) +
   `CommunityStanding` (B.2) + `ManagementRole`/`ResolvedAccessDto` (B.1) + recent `EventJournal` activity. Each
   block is the owner spec's type, referenced not redefined.
4. **Dashboard snapshot is best-effort per section** — five independent read legs, each degrading to a null
   section + typed `Degraded` note rather than failing the whole page; only a tenant/authorization fault fails
   the call. The dedicated community-list endpoints, by contrast, fail hard with the Helix error code (they have
   no fallback to degrade into).
5. **Recent-activity feed is journal-sourced and display-safe** — projected from `EventRecord` to a thin
   `ActivityItemDto`; raw payloads and `[PII-hash]`/encrypted fields are never surfaced through these read
   endpoints.
6. **Active alerts are derived, not stored** — `DashboardAlertDto` composes existing connection-health and
   projection-checkpoint signals on the fly; no alert table is introduced.
7. **Two action keys, both `management` / Moderator(10) / Low / grantable** — `community:read` and
   `dashboard:read`, seeded in `roles-permissions.md` §7.1 (§7 deltas). Read-only dashboard data sits at the
   Moderator floor like every other read key in the management plane.
