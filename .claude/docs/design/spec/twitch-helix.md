# Interface Specification — `twitch-helix` subsystem

**Status:** As-built reference (checked 2026-09-30). The 26 façade sub-clients, the operator methods, the conduits sub-client, the scope bookkeeping and the diagnostics routes below are in the tree. Parts that are not built are marked **unbuilt**.
**Subsystem area:** Helix API client (`IHttpClientFactory` + resilience), channel info, followers, subs, bans, scopes, rate-limit handling.
**Source of truth:** locked DB schema `2026-06-16-database-schema.md`, design `2026-06-16-twitch-rebuild.md`, stack `2026-06-16-stack-and-dependencies.md`, decisions `2026-06-16-decisions-resolved.md`.

**Binding conventions** (apply throughout, stated once): namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way (never `.Result`/`.Wait`); `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; **Newtonsoft.Json** for app JSON / `[VC:JSON]` columns; surrogate PKs `guid` via `Guid.CreateVersion7()`; Twitch ids are indexed attribute columns; tenant key `BroadcasterId` is `Guid`; soft-delete (`IsDeleted`+`DeletedAt`) global filter.

> **Scope boundary.** This subsystem is the **Helix request/response client and its supporting concerns**: HTTP pipeline, per-token adaptive rate limiting, scope pre-checks, typed error mapping, and the read/write Helix calls for channel info / followers / subs / bans / moderators / VIPs / rewards / categories. **Out of scope** (owned by sibling subsystems, referenced but not (re)defined here): EventSub transport (`ITwitchEventSubService`), OAuth token issuance/refresh/vault (`ITwitchAuthService`, `IntegrationTokens`, `CryptoKey`), IRC retirement, pipeline action wiring. This spec **replaced** the legacy `ITwitchApiService`/`TwitchApiService` pair — retired entirely, no shim (§3, §8); the built surface is `ITwitchHelixClient` (`server/src/NomNomzBot.Application/Contracts/Twitch/ITwitchHelixClient.cs`) + the granular sub-clients — migrating `Task<bool>`/`Task<T?>` returns to `Task<Result<T>>` and the inline header-poll rate limiting to a dedicated handler.

---

## 1. Entities

This subsystem **reads from and writes to** locked-schema tables; it **owns none exclusively** (Helix is a projection/mutation layer over Twitch state mirrored into these tables by EventSub + sync jobs). The sub-clients are pure Helix I/O and write nothing themselves; the scope-gap bookkeeping rows are written by handlers that react to the events in §2 (see §3.6). All field definitions are authoritative in `2026-06-16-database-schema.md`; referenced here by name + the fields this subsystem touches.

| Table | Schema ref | Role for this subsystem | Key fields touched |
|---|---|---|---|
| `Channels` | A.2 | Read tenant root; resolve `TwitchChannelId` ↔ `BroadcasterId (Guid)`; write `GameId`, `GameName`, `Title`, `Tags`, `Language`, `IsLive` on channel-info sync | `Id (guid PK)`, `TwitchChannelId (string50)`, `Title`, `GameId`, `GameName`, `Tags ([VC:JSON] List<string>)`, `Language`, `IsLive (bool)` |
| `Streams` | F.1 | Read `IsLive`/current session for stream-info calls; written by EventSub (not here) | `Id`, `BroadcasterId`, `TwitchStreamId`, `Title`, `GameId`, `GameName`, `ViewerCountPeak`, `StartedAt`, `EndedAt` |
| `TwitchSubscribers` | F.2 | Defined in the locked schema but **not built** — no subscriber mirror table/DbSet exists in code; sub lists/counts are served live from `GET /subscriptions` by consumers (the sub-clients are pure Helix I/O) | — |
| `TwitchFollowers` | F.3 | Defined in the locked schema but **not built** — no follower mirror table/DbSet exists in code; follower lists/counts are served live from `GET /channels/followers` by consumers | — |
| `Rewards` | F.5 | Read/mirror channel-point rewards from Helix `GET /channel_points/custom_rewards` | `Id`, `BroadcasterId`, `TwitchRewardId`, `Title`, `Cost`, `IsEnabled`, `IsPaused` |
| `IntegrationConnections` | E.1 | **Read** granted `Scopes ([VC:JSON])`, `Status`, `ProviderAccountId` for scope pre-check; **write** `LastErrorAt`, `LastRefreshedAt`, `ConsecutiveFailureCount`, `Status` on Helix call outcomes | `Id`, `BroadcasterId (guid, Null=global)`, `Provider (=twitch)`, `ProviderAccountId`, `Status`, `Scopes ([VC:JSON] List<string>)`, `LastRefreshedAt`, `LastErrorAt`, `ConsecutiveFailureCount` |
| `IntegrationTokens` | E.2 | **Read-only** — decrypted access token obtained via `ITwitchAuthService` (this subsystem never reads `CipherText` directly) | (via auth service) |
| `ChannelMissingScope` | (identity-auth §3.4a) | **Write** (by `MissingScopeRecordingHandler`, not by a sub-client) — one row per `(BroadcasterId, Scope)` the first time a Helix call short-circuits or fails with `missing_scope`; feeds the dashboard banner and the one-time chat notice | `BroadcasterId`, `Scope`, `Feature`, `DetectedAt`, `ChatNotifiedAt` |

> **No table is owned by the sub-clients.** Rate-limit buckets are **in-memory** and per token (`TwitchRateLimiter`, per-process — not persisted; single-node). **Design intent, unbuilt:** on SaaS, `ITwitchRateLimiter` delegates to the distributed `IRateLimiter` (`scaling-qos.md` §4.2) — a global `helix:app` bucket (720/60s) plus a per-channel `helix:ch:{id}` fair sub-budget. There is no `IdempotencyKey` guard on Helix mutations: a retry of a ban/timeout/shoutout is left to Twitch's own semantics. Granted-scope state is read from `IntegrationConnections.Scopes`, never duplicated.

---

## 2. Domain events

Emitted on the in-process `IEventBus` (`NomNomzBot.Domain.Interfaces.IEventBus`). All inherit the canonical `DomainEventBase` (`platform-conventions.md` §2.0 — provides `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`; events do **not** redeclare these). The three types live in `NomNomzBot.Domain.Twitch.Events`, as `sealed class` types with `required … { get; init; }` members (the field set below is exact; the code block shows the shape as a record). The publisher sets the inherited `BroadcasterId` to the owning channel — `Guid.Empty` for app/bot-token, non-tenant events (rate-limit / circuit-breaker). Twitch ids ride alongside as strings; no PII free-text beyond display-name snapshots already permitted by schema.

```csharp
namespace NomNomzBot.Domain.Twitch.Events;

/// Raised when a Helix call returns 401 and the single refresh-and-retry also fails,
/// or when a required scope is absent. Published by TwitchTokenResolver.HasScopeAsync on every failed scope
/// pre-check and consumed by MissingScopeRecordingHandler (§3.6).
public sealed record TwitchHelixReauthRequiredEvent(
    string Provider,            // "twitch"
    string ServiceName,         // "twitch" | "twitch_bot"
    string Reason,              // "unauthorized" | "missing_scope" | "token_revoked"
    string? MissingScope        // e.g. "channel:read:subscriptions"; null unless Reason="missing_scope" (also set with Reason="no_token" when the connection lookup itself fails)
) : DomainEventBase;

/// Raised when the adaptive limiter or a 429 forces a Helix call to be throttled/queued.
/// Consumed by observability + UI "rate limited" surfacing. Not persisted.
// App/bot-token (non-tenant) calls publish with the inherited BroadcasterId = Guid.Empty.
public sealed record TwitchHelixRateLimitedEvent(
    string TokenBucketKey,      // hashed token-bucket identity (never the raw token)
    int RemainingBeforeThrottle,
    DateTimeOffset ResetsAt,
    bool WasHardLimited         // true if a real 429 was received (vs proactive throttle)
) : DomainEventBase;

/// Raised when the circuit breaker opens for the Helix client after sustained failures.
// Platform-level (not tenant-scoped): published with the inherited BroadcasterId = Guid.Empty.
public sealed record TwitchHelixCircuitOpenedEvent(
    string ClientName,          // "twitch-helix"
    DateTimeOffset OpenedAt,
    TimeSpan BreakDuration
) : DomainEventBase;
```

> `TwitchSubscriber*` / `TwitchFollower*` add/remove events are **owned by the EventSub subsystem** (driven by `channel.subscribe` / `channel.follow`), not emitted here. This subsystem's sub/follower calls are **pure reads served straight from Twitch** — the sub-clients write no local rows (no subscriber/follower mirror table exists in the shipped code) and emit no per-row events.

---

## 3. Service interface(s)

All in `NomNomzBot.Application.Contracts.Twitch`. `ITwitchHelixClient` is the top-level façade exposing the **category sub-clients** by name (§3.1; the built surface is the 26 granular clients, not four coarse buckets; a 27th, `ITwitchEventSubConduitsApi`, is registered beside them but is not on the façade — §3.4b). The legacy `ITwitchApiService` has been **retired entirely** — there is no compatibility shim. Every caller (`DashboardController`, `StreamController`, `CommunityController`, `ChannelsController`, `ModerationService`, `RewardService`, the pipeline actions, the event handlers, `HelixChatProvider`) now targets the sub-clients directly (no-backwards-compat: the codebase has no external consumers yet, so the interface was deleted rather than shimmed). Every method returns `Task<Result<T>>` (or `Task<Result>` for void mutations). `Result.Failure` carries `ErrorCode` ∈ `{ "no_token", "missing_scope", "unauthorized", "rate_limited", "not_found", "conflict", "twitch_error", "transport" }` (the closed `TwitchErrorCodes` set).

### 3.1 `ITwitchHelixClient` — top-level façade

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public interface ITwitchHelixClient
{
    ITwitchChannelsApi Channels { get; }
    ITwitchUsersApi Users { get; }
    ITwitchSearchApi Search { get; }
    ITwitchStreamsApi Streams { get; }
    ITwitchSubscriptionsApi Subscriptions { get; }
    ITwitchChannelPointsApi ChannelPoints { get; }
    ITwitchModerationApi Moderation { get; }
    ITwitchModeratorsApi Moderators { get; }
    ITwitchPollsApi Polls { get; }
    ITwitchPredictionsApi Predictions { get; }
    ITwitchRaidsApi Raids { get; }
    ITwitchChatApi Chat { get; }
    ITwitchChatAssetsApi ChatAssets { get; }
    ITwitchBitsApi Bits { get; }
    ITwitchClipsApi Clips { get; }
    ITwitchVideosApi Videos { get; }
    ITwitchScheduleApi Schedule { get; }
    ITwitchAdsApi Ads { get; }
    ITwitchCharityApi Charity { get; }
    ITwitchGoalsApi Goals { get; }
    ITwitchHypeTrainApi HypeTrain { get; }
    ITwitchTeamsApi Teams { get; }
    ITwitchGamesApi Games { get; }
    ITwitchContentClassificationApi ContentClassification { get; }
    ITwitchWhispersApi Whispers { get; }
    ITwitchGuestStarApi GuestStar { get; }
}
```
*Behavior:* pure accessor; exposes the domain sub-clients for IntelliSense discovery. Holds no state; each sub-client shares the same `HttpClient("twitch-helix")`, rate limiter, identity resolver, and token resolver.

> **Surface grouping (implemented):** the built surface is **26 granular per-category sub-clients** (one per Helix theme — the full channel-admin endpoint coverage), not the four coarse buckets (`Channels`/`Moderation`/`Subscriptions`/`LiveOps`) the early draft sketched. §3.2–§3.4a below remain accurate as the **behavioral catalogue** of the endpoints (scopes, state changes, events), organised by theme; the façade simply exposes each category sub-client by name. The deliberate `ITwitchLiveOpsApi` "live-ops writes" bucket (§3.4a) was split into its constituent category clients (`Polls`/`Predictions`/`Raids`/`Ads`/`Schedule`/`Clips`/…) so each stays a single-responsibility Helix I/O wrapper (deps: transport + identity + token only).

### 3.2 `ITwitchChannelsApi` — channel info, editors, follow relationships, channel updates

Method order below mirrors the interface files exactly (names, parameter order, defaults).

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public interface ITwitchChannelsApi
{
    Task<Result<TwitchChannelInformation>> GetChannelInformationAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result> ModifyChannelInformationAsync(Guid broadcasterId, ModifyChannelInformationRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchChannelEditor>>> GetChannelEditorsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchFollowedChannel>>> GetFollowedChannelsAsync(Guid broadcasterId, string? filterTwitchBroadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchChannelFollower>>> GetChannelFollowersAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<int>> GetChannelFollowerCountAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

| Method | Behavior (state change / events / side effects) |
|---|---|
| `GetChannelInformationAsync` | Read-only `GET /channels` (app token first, user-token fallback; no scope). Pure Helix I/O — the sub-client holds no DB/event-bus dependency, writes nothing and emits nothing; mirroring is a consumer-service responsibility (`ChannelInfoSeedOnOnboardingHandler` seeds `Channels.Title/GameName/Language` on `ChannelOnboardedEvent`; `DashboardController`/`StreamController` read it live for display). Resolves `Guid broadcasterId`→`TwitchChannelId` first; `not_found` if channel unknown locally. |
| `ModifyChannelInformationAsync` | `PATCH /channels` (title/game/language/delay/tags/content-classification-labels/branded — all fields optional, only the set ones are sent). Pure Helix I/O — pre-checks `channel:manage:broadcast`, pushes the patch, writes nothing locally, emits nothing, and carries no idempotency guard. Consumers own the local mirror: `StreamController` updates the in-memory `ChannelContext` (`CurrentTitle`/`CurrentGame`) on success; persisted `Channels.Title/GameName` reconcile via `StreamStatusPollingService.ApplyStreamState` while live. |
| `GetChannelEditorsAsync` | Read-only `GET /channels/editors` — users with editor access. Requires `channel:read:editors`. Pure Helix I/O; no state change. |
| `GetFollowedChannelsAsync` | Read-only paged `GET /channels/followed` — channels the tenant follows, newest first; `filterTwitchBroadcasterId` (raw Twitch id) narrows to one target channel to check a specific follow. Requires `user:read:follows`. Pure Helix I/O. |
| `GetChannelFollowersAsync` | Read-only paged `GET /channels/followers`. Returns one page + cursor + total. Pure Helix I/O — writes nothing (no follower table exists and no shipped consumer mirrors follower rows today; `CommunityController`'s followers tab serves this page straight from Twitch, joining `Users`/`ChatMessages` in memory). Requires `moderator:read:followers`. |
| `GetChannelFollowerCountAsync` | Read-only `GET /channels/followers?first=1`; returns `total`. Requires `moderator:read:followers` (pre-checked). No state change. |

### 3.2a `ITwitchUsersApi` — user lookups, profile description, block list, extensions

```csharp
public interface ITwitchUsersApi
{
    Task<Result<IReadOnlyList<TwitchUser>>> GetUsersByIdsAsync(IReadOnlyList<string> twitchUserIds, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchUser>>> GetUsersByLoginsAsync(IReadOnlyList<string> logins, CancellationToken ct = default);
    Task<Result<TwitchUser>> UpdateDescriptionAsync(Guid broadcasterId, string description, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchBlockedUser>>> GetBlockListAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result> BlockUserAsync(Guid broadcasterId, string targetTwitchUserId, string? sourceContext = null, string? reason = null, CancellationToken ct = default);
    Task<Result> UnblockUserAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchInstalledExtension>>> GetInstalledExtensionsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchActiveExtensions>> GetActiveExtensionsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchActiveExtensions>> UpdateActiveExtensionsAsync(Guid broadcasterId, UpdateUserExtensionsRequest request, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `GetUsersByIdsAsync` / `GetUsersByLoginsAsync` | Read-only `GET /users` (batch `id=`/`login=` params → `IReadOnlyList<TwitchUser>`). App token; no scope; email never requested. Unknown ids simply come back absent from the list (empty list = success, not `not_found`). |
| `UpdateDescriptionAsync` | `PUT /users?description=` — sets the tenant's own profile description; returns the updated user. Requires `user:edit`. |
| `GetBlockListAsync` | Read paged `GET /users/blocks`. Requires `user:read:blocked_users`. |
| `BlockUserAsync` / `UnblockUserAsync` | `PUT` / `DELETE /users/blocks`; block takes optional `source_context` (`chat`/`whisper`) and `reason` (`harassment`/`spam`/`other`). Status-only. Requires `user:manage:blocked_users`. |
| `GetInstalledExtensionsAsync` | Read — every installed extension (active + inactive). Requires `user:read:broadcast` or `user:edit:broadcast` (Twitch only includes inactive ones with the latter). |
| `GetActiveExtensionsAsync` | Read — activated extensions per slot type. App token with an explicit `user_id`; no scope. |
| `UpdateActiveExtensionsAsync` | `PUT /users/extensions` — activates/deactivates/moves via the `data`-wrapped slot maps; returns the post-update state. Requires `user:edit:broadcast`. |

### 3.2b `ITwitchSearchApi` — category & channel discovery

```csharp
public interface ITwitchSearchApi
{
    Task<Result<TwitchPage<TwitchSearchCategory>>> SearchCategoriesAsync(string query, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchSearchChannel>>> SearchChannelsAsync(string query, bool? liveOnly, TwitchPageRequest page, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `SearchCategoriesAsync` | Read paged `GET /search/categories`. App token; no scope; keyed on the query string, no tenant. |
| `SearchChannelsAsync` | Read paged `GET /search/channels` — channels that streamed within the past 6 months; `liveOnly` filters to currently-live. App token; no scope. |

### 3.2c `ITwitchStreamsApi` — live streams, stream key, followed streams, markers

```csharp
public interface ITwitchStreamsApi
{
    Task<Result<TwitchPage<TwitchStream>>> GetStreamsAsync(TwitchStreamsFilter filter, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchStream>> GetStreamAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<string>> GetStreamKeyAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchStream>>> GetFollowedStreamsAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchStreamMarker>> CreateStreamMarkerAsync(Guid broadcasterId, string? description = null, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchStreamMarkerGroup>>> GetStreamMarkersAsync(Guid broadcasterId, string? videoId, TwitchPageRequest page, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `GetStreamsAsync` | Read paged `GET /streams` with repeated `user_id`/`user_login`/`game_id`/`language` + `type` filters (`TwitchStreamsFilter`). App token; no scope; subject-agnostic public read. |
| `GetStreamAsync` | Read-only `GET /streams?user_id=`. Twitch lists a stream in `data[]` **only while live** — an empty `data[]` surfaces as a `Result` **failure** with `TwitchErrorCodes.NotFound` (`not_found`), which callers read as "offline" (`IsSuccess` = live). The sub-client itself writes nothing (pure Helix I/O); `StreamStatusPollingService.ApplyStreamState` maps `IsSuccess`/`IsFailure` onto `Channels.IsLive` (+ Title/GameName freshness). `Streams` lifecycle rows stay EventSub-owned. |
| `GetStreamKeyAsync` | Read `GET /streams/key` — the channel's RTMP stream key string. Requires `channel:read:stream_key`. |
| `GetFollowedStreamsAsync` | Read paged `GET /streams/followed` — the live streams the tenant follows. Requires `user:read:follows`. |
| `CreateStreamMarkerAsync` | `POST /streams/markers` — marks the current live position, optional description; returns the created marker. Requires `channel:manage:broadcast`. |
| `GetStreamMarkersAsync` | Read paged `GET /streams/markers` — markers from the most recent stream, or a specific VOD via `videoId`. The response nests `user → videos[] → markers[]`, so each page item is a `TwitchStreamMarkerGroup`. Requires `user:read:broadcast`. |

### 3.3 `ITwitchModerationApi` — bans/timeouts, unban requests, blocked terms, message deletion, Shield Mode, warnings, suspicious users, AutoMod

Moderator/VIP rosters live on §3.3a, rewards/redemptions on §3.3b, shoutouts/announcements on §3.3c, chatters/emotes/badges on §3.3d. The plain methods use the broadcaster's user token; endpoints needing both `broadcaster_id` and `moderator_id` send the single resolved Twitch id for both (the tenant moderates their own channel). The 14 `*AsOperatorAsync` methods (listed after the table) run on the logged-in operator's OWN token against a raw Twitch channel id, so a moderator acts in any channel Twitch made them a moderator of (chat-client.md §3.5).

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public interface ITwitchModerationApi
{
    // ─ Bans / timeouts ─
    Task<Result<TwitchBanResult>> BanUserAsync(Guid broadcasterId, string targetTwitchUserId, string? reason, CancellationToken ct = default);
    Task<Result<TwitchBanResult>> BanAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string targetTwitchUserId, string? reason, CancellationToken ct = default);
    Task<Result<TwitchBanResult>> TimeoutUserAsync(Guid broadcasterId, string targetTwitchUserId, int durationSeconds, string? reason, CancellationToken ct = default);
    Task<Result> UnbanUserAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchBannedUser>>> GetBannedUsersAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    // (operator variants of unban / timeout / unban-requests / blocked terms / shield mode / warnings / suspicious users follow the AsOperator list below the table)

    // ─ Unban requests ─
    Task<Result<TwitchPage<TwitchUnbanRequest>>> GetUnbanRequestsAsync(Guid broadcasterId, string status, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchUnbanRequest>> ResolveUnbanRequestAsync(Guid broadcasterId, string unbanRequestId, string status, string? resolutionText, CancellationToken ct = default);

    // ─ Blocked terms ─
    Task<Result<TwitchPage<TwitchBlockedTerm>>> GetBlockedTermsAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchBlockedTerm>> AddBlockedTermAsync(Guid broadcasterId, string text, CancellationToken ct = default);
    Task<Result> RemoveBlockedTermAsync(Guid broadcasterId, string blockedTermId, CancellationToken ct = default);

    // ─ Chat message deletion ─
    Task<Result> DeleteChatMessageAsync(Guid broadcasterId, string messageId, CancellationToken ct = default);
    Task<Result> DeleteAllChatMessagesAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result> DeleteChatMessageAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string messageId, CancellationToken ct = default);

    // ─ Shield Mode / warnings / suspicious users ─
    Task<Result<TwitchShieldModeStatus>> GetShieldModeStatusAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchShieldModeStatus>> UpdateShieldModeStatusAsync(Guid broadcasterId, bool isActive, CancellationToken ct = default);
    Task<Result<TwitchWarningResult>> WarnChatUserAsync(Guid broadcasterId, string targetTwitchUserId, string reason, CancellationToken ct = default);
    Task<Result<TwitchSuspiciousUserStatus>> AddSuspiciousStatusAsync(Guid broadcasterId, string targetTwitchUserId, string status, CancellationToken ct = default);
    Task<Result<TwitchSuspiciousUserStatus>> RemoveSuspiciousStatusAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);

    // ─ AutoMod ─
    Task<Result<IReadOnlyList<TwitchAutoModStatus>>> CheckAutoModStatusAsync(Guid broadcasterId, IReadOnlyList<(string MsgId, string MsgText)> messages, CancellationToken ct = default);
    Task<Result> ManageHeldAutoModMessageAsync(Guid broadcasterId, string messageId, bool approve, CancellationToken ct = default);
    Task<Result<TwitchAutoModSettings>> GetAutoModSettingsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchAutoModSettings>> UpdateAutoModSettingsAsync(Guid broadcasterId, UpdateAutoModSettingsRequest settings, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `BanUserAsync` | `POST /moderation/bans` (permanent). Requires `moderator:manage:banned_users`. Returns the applied `TwitchBanResult`. Pure Helix I/O — local `ModerationActions` rows are written by the moderation subsystem reacting to the resulting EventSub `channel.ban`, not here. |
| `BanAsOperatorAsync` (and the rest of the operator family, see below) | Same endpoint on the **operator's own token** (`moderator_id` = the operator) against a raw Twitch `broadcasterTwitchId` — works in ANY channel Twitch has made the operator a moderator of, tenant or not. Twitch enforces the mod relationship; no privilege escalation. |
| `TimeoutUserAsync` | `POST /moderation/bans` with `duration`. Same scope/side-effect note as ban; `TwitchBanResult.EndTime` carries the timeout end. |
| `UnbanUserAsync` | `DELETE /moderation/bans`. Requires `moderator:manage:banned_users`. |
| `GetBannedUsersAsync` | Read paged `GET /moderation/banned`. Requires `moderation:read`. No state change. |
| `GetUnbanRequestsAsync` | Read paged `GET /moderation/unban_requests` filtered by `status` (pending / approved / denied / …). Requires `moderator:read:unban_requests`. |
| `ResolveUnbanRequestAsync` | `PATCH /moderation/unban_requests` — approves or denies, optionally with resolution text. Requires `moderator:manage:unban_requests`. |
| `GetBlockedTermsAsync` | Read paged `GET /moderation/blocked_terms`. Requires `moderator:read:blocked_terms` (or manage). |
| `AddBlockedTermAsync` | `POST /moderation/blocked_terms`. Requires `moderator:manage:blocked_terms`. Returns the created term (Twitch assigns its id). |
| `RemoveBlockedTermAsync` | `DELETE /moderation/blocked_terms`. Requires `moderator:manage:blocked_terms`. |
| `DeleteChatMessageAsync` | `DELETE /moderation/chat` for one `message_id`. Requires `moderator:manage:chat_messages`. |
| `DeleteAllChatMessagesAsync` | `DELETE /moderation/chat` omitting `message_id` — clears the chat room. Requires `moderator:manage:chat_messages`. |
| `DeleteChatMessageAsOperatorAsync` | Same endpoint on the operator's own token against a raw Twitch `broadcasterTwitchId` — a dashboard deletion is attributed to the acting moderator, not the broadcaster. |
| `GetShieldModeStatusAsync` / `UpdateShieldModeStatusAsync` | `GET` / `PUT /moderation/shield_mode`. Requires `moderator:read:shield_mode` / `moderator:manage:shield_mode`; update returns the new status. |
| `WarnChatUserAsync` | `POST /moderation/warnings`. Requires `moderator:manage:warnings`. Twitch-native warn — the warned user must acknowledge before chatting again. |
| `AddSuspiciousStatusAsync` / `RemoveSuspiciousStatusAsync` | Flags a chatter `ACTIVE_MONITORING`/`RESTRICTED` or clears the flag. Requires `moderator:manage:suspicious_users`. |
| `CheckAutoModStatusAsync` | `POST /moderation/enforcements/status` — tests whether each message would be held by AutoMod. Requires `moderation:read`. |
| `ManageHeldAutoModMessageAsync` | `POST /moderation/automod/message` with `action`=`ALLOW`/`DENY`. Requires `moderator:manage:automod`. Releases or drops a held AutoMod message. |
| `GetAutoModSettingsAsync` / `UpdateAutoModSettingsAsync` | `GET` / `PUT /moderation/automod/settings` — overall level or the nine per-category levels; update returns the applied settings. Requires `moderator:read:automod_settings` / `moderator:manage:automod_settings`. |

**Operator family (as-built).** Every method below takes `Guid operatorUserId, string broadcasterTwitchId` first, sends `TwitchHelixAuth.Operator` (the operator's own user token, resolved by `ITwitchTokenResolver.GetUserTokenAsync`) and never resolves the channel from a `Guid`, because the target channel may not be a tenant. Twitch enforces the moderator relationship, so there is no privilege escalation; a scope the operator's grant lacks surfaces as a typed failure. Each has the same scope and endpoint as its plain twin above.

```csharp
Task<Result<TwitchBanResult>> BanAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string targetTwitchUserId, string? reason, CancellationToken ct = default);
Task<Result> UnbanAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string targetTwitchUserId, CancellationToken ct = default);
Task<Result<TwitchBanResult>> TimeoutAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string targetTwitchUserId, int durationSeconds, string? reason, CancellationToken ct = default);
Task<Result<TwitchPage<TwitchUnbanRequest>>> GetUnbanRequestsAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string status, TwitchPageRequest page, CancellationToken ct = default);
Task<Result<TwitchUnbanRequest>> ResolveUnbanRequestAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string unbanRequestId, string status, string? resolutionText, CancellationToken ct = default);
Task<Result<TwitchPage<TwitchBlockedTerm>>> GetBlockedTermsAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, TwitchPageRequest page, CancellationToken ct = default);
Task<Result<TwitchBlockedTerm>> AddBlockedTermAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string text, CancellationToken ct = default);
Task<Result> RemoveBlockedTermAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string blockedTermId, CancellationToken ct = default);
Task<Result> DeleteChatMessageAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string messageId, CancellationToken ct = default);
Task<Result<TwitchShieldModeStatus>> GetShieldModeStatusAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, CancellationToken ct = default);
Task<Result<TwitchShieldModeStatus>> UpdateShieldModeStatusAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, bool isActive, CancellationToken ct = default);
Task<Result<TwitchWarningResult>> WarnChatUserAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string targetTwitchUserId, string reason, CancellationToken ct = default);
Task<Result<TwitchSuspiciousUserStatus>> AddSuspiciousStatusAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string targetTwitchUserId, string status, CancellationToken ct = default);
Task<Result<TwitchSuspiciousUserStatus>> RemoveSuspiciousStatusAsOperatorAsync(Guid operatorUserId, string broadcasterTwitchId, string targetTwitchUserId, CancellationToken ct = default);
```

The operator family is not limited to `ITwitchModerationApi`: `ITwitchChatAssetsApi.GetUserEmotesAsOperatorAsync` (§3.3d) follows the same pattern. `TwitchHelixAuth` has five modes: `App`, `User`, `Operator`, `BotApp` (the platform app token, used by the conduits sub-client and the badge-bearing chat send) and `UserStrict` (the broadcaster's own token with no bot fallback).

### 3.3a `ITwitchModeratorsApi` — moderator & VIP rosters, moderated channels

```csharp
public interface ITwitchModeratorsApi
{
    Task<Result<TwitchPage<TwitchModerator>>> GetModeratorsAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result> AddModeratorAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result> RemoveModeratorAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchVip>>> GetVipsAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result> AddVipAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result> RemoveVipAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchModeratedChannel>>> GetModeratedChannelsAsync(Guid userId, TwitchPageRequest page, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `GetModeratorsAsync` | Read paged `GET /moderation/moderators`. Requires `moderation:read`. |
| `AddModeratorAsync` / `RemoveModeratorAsync` | `POST` / `DELETE /moderation/moderators`. Requires `channel:manage:moderators`. |
| `GetVipsAsync` | Read paged `GET /channels/vips`. Requires `channel:read:vips`. |
| `AddVipAsync` / `RemoveVipAsync` | `POST` / `DELETE /channels/vips`. Requires `channel:manage:vips`. Add surfaces `twitch_error` on Twitch's VIP-slot-limit 422. |
| `GetModeratedChannelsAsync` | Read paged `GET /moderation/channels` — channels the **user** (`Guid userId`, resolved to its Twitch id internally) moderates. Requires `user:read:moderated_channels`. |

### 3.3b `ITwitchChannelPointsApi` — custom-reward CRUD, redemptions

The app that created a reward is the only app that may read (manageable-filtered), update, or delete it or its redemptions.

```csharp
public interface ITwitchChannelPointsApi
{
    Task<Result<TwitchCustomReward>> CreateCustomRewardAsync(Guid broadcasterId, CreateCustomRewardRequest request, CancellationToken ct = default);
    Task<Result<TwitchCustomReward>> UpdateCustomRewardAsync(Guid broadcasterId, string rewardId, UpdateCustomRewardRequest request, CancellationToken ct = default);
    Task<Result> DeleteCustomRewardAsync(Guid broadcasterId, string rewardId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchCustomReward>>> GetCustomRewardsAsync(Guid broadcasterId, IReadOnlyList<string>? rewardIds = null, bool onlyManageableRewards = false, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchCustomRewardRedemption>>> GetCustomRewardRedemptionsAsync(Guid broadcasterId, string rewardId, string? status, IReadOnlyList<string>? redemptionIds, string? sort, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchCustomRewardRedemption>>> UpdateRedemptionStatusAsync(Guid broadcasterId, string rewardId, IReadOnlyList<string> redemptionIds, UpdateRedemptionStatusRequest request, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `CreateCustomRewardAsync` / `UpdateCustomRewardAsync` / `DeleteCustomRewardAsync` | `POST` / `PATCH` / `DELETE /channel_points/custom_rewards`. Requires `channel:manage:redemptions`. Pure Helix I/O — mirroring into local `Rewards` is the rewards module's job (import / take-control flows), not this sub-client's. |
| `GetCustomRewardsAsync` | Read `GET /channel_points/custom_rewards`, optionally filtered to specific reward ids and/or `only_manageable_rewards=true`. Requires `channel:read:redemptions`. No state change. |
| `GetCustomRewardRedemptionsAsync` | Read paged `GET /channel_points/custom_rewards/redemptions` — by lifecycle `status` (`CANCELED`/`FULFILLED`/`UNFULFILLED`) or specific redemption ids, optional `OLDEST`/`NEWEST` sort. Requires `channel:read:redemptions`. |
| `UpdateRedemptionStatusAsync` | `PATCH /channel_points/custom_rewards/redemptions` — moves the given UNFULFILLED redemptions to `FULFILLED`/`CANCELED`; returns the updated redemptions. Requires `channel:manage:redemptions`. Local `RewardRedemptions.Status` is updated by the rewards subsystem via EventSub, not here. |

### 3.3c `ITwitchChatApi` — announcements, shoutouts, chat settings, name color, pinned messages

Plain "Send Chat Message" is intentionally absent — it is owned by `HelixChatProvider` (§6).

```csharp
public interface ITwitchChatApi
{
    Task<Result> SendAnnouncementAsync(Guid broadcasterId, string message, string? color, CancellationToken ct = default);
    Task<Result> SendShoutoutAsync(Guid broadcasterId, string toTwitchBroadcasterId, CancellationToken ct = default);
    Task<Result<TwitchChatSettings>> GetChatSettingsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchChatSettings>> UpdateChatSettingsAsync(Guid broadcasterId, UpdateChatSettingsRequest request, CancellationToken ct = default);
    Task<Result<TwitchUserChatColor>> GetUserChatColorAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result> UpdateUserChatColorAsync(Guid broadcasterId, string color, CancellationToken ct = default);
    Task<Result<TwitchPinnedChatMessage>> GetPinnedMessagesAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchPinnedChatMessage>> PinMessageAsync(Guid broadcasterId, string messageId, int? durationSeconds, CancellationToken ct = default);
    Task<Result<TwitchPinnedChatMessage>> UpdatePinnedMessageAsync(Guid broadcasterId, int? durationSeconds, CancellationToken ct = default);
    Task<Result> UnpinMessageAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `SendAnnouncementAsync` | `POST /chat/announcements`, optionally tinted. Requires `moderator:manage:announcements`. |
| `SendShoutoutAsync` | `POST /chat/shoutouts`. Requires `moderator:manage:shoutouts`. Honors Twitch's own shoutout cooldown (surfaces `twitch_error` on 429-cooldown). |
| `GetChatSettingsAsync` / `UpdateChatSettingsAsync` | `GET` / `PATCH /chat/settings` — emote / follower / slow / subscriber / unique-chat configuration; the patch sends only the supplied fields. Read: user token, no scope; update requires `moderator:manage:chat_settings`. |
| `GetUserChatColorAsync` / `UpdateUserChatColorAsync` | `GET` / `PUT /chat/color` — the tenant's own name color (named color or hex). Update requires `user:manage:chat_color`. |
| `GetPinnedMessagesAsync` / `PinMessageAsync` / `UpdatePinnedMessageAsync` / `UnpinMessageAsync` | The pinned-message lifecycle — read the current pin (`not_found` when none), pin for an optional duration, change the remaining duration, unpin. Mutations require `moderator:manage:chat_messages`. |

### 3.3d `ITwitchChatAssetsApi` — chatters, emotes, badges, shared chat

```csharp
public interface ITwitchChatAssetsApi
{
    Task<Result<TwitchPage<TwitchChatter>>> GetChattersAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchChannelEmote>>> GetChannelEmotesAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchGlobalEmote>>> GetGlobalEmotesAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchEmoteSetEmote>>> GetEmoteSetsAsync(IReadOnlyList<string> emoteSetIds, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchUserEmote>>> GetUserEmotesAsync(Guid broadcasterId, string? afterCursor, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchUserEmote>>> GetUserEmotesAsOperatorAsync(Guid operatorUserId, string? broadcasterTwitchId, string? afterCursor, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchChatBadgeSet>>> GetChannelChatBadgesAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchChatBadgeSet>>> GetGlobalChatBadgesAsync(CancellationToken ct = default);
    Task<Result<TwitchSharedChatSession>> GetSharedChatSessionAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `GetChattersAsync` | Read paged `GET /chat/chatters` — the present-viewer list (one page per call, caller follows the cursor); the moderator is the tenant itself (resolved internally). Requires `moderator:read:chatters` — a **progressive** scope (requested only when a chatter-list feature is enabled, e.g. the `{{random.chatter}}` token / chatter-driven actions), not part of the base grant; returns `missing_scope` if not granted. No state change. **As-built note:** no service, controller or pipeline action calls `GetChattersAsync` today (grep, 2026-09-30) and the `random.chatter` template token is not in `TemplateHelperRegistry`; both are unbuilt consumers (`commands-pipelines.md` §6.3 is design intent). A future caller caches the page with a short TTL. |
| `GetChannelEmotesAsync` / `GetGlobalEmotesAsync` / `GetEmoteSetsAsync` | Read — channel custom emotes, Twitch's global emotes, and the emotes in one or more emote sets (repeated `emote_set_id`). App token; no scope. |
| `GetUserEmotesAsync` | Read cursor-paged (no `total`) — the emotes available to the **tenant** across all channels. Requires `user:read:emotes`. |
| `GetUserEmotesAsOperatorAsync` | Same read on the **logged-in operator's own token** (`TwitchHelixAuth.Operator` via `OperatorUserId`) — a moderator sees THEIR personal emotes regardless of whose channel is active (chat-client.md §3.2). Optional `broadcasterTwitchId` (raw Twitch id — the channel may not be a tenant, so it is NEVER resolved from a Guid) guarantees that channel's follower emotes are included. Requires `user:read:emotes` on the operator's grant — enforced by Twitch, never a local tenant-token pre-check; a missing scope surfaces as a typed failure the caller degrades to empty. |
| `GetChannelChatBadgesAsync` / `GetGlobalChatBadgesAsync` | Read — channel / global chat-badge sets. App token; no scope. |
| `GetSharedChatSessionAsync` | Read — the channel's active shared chat session, or `not_found` when not in one (empty `data[]`). App token; no scope. |

### 3.4 `ITwitchSubscriptionsApi` — subscriber list, single-user check, count

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public interface ITwitchSubscriptionsApi
{
    Task<Result<TwitchPage<TwitchBroadcasterSubscription>>> GetBroadcasterSubscriptionsAsync(Guid broadcasterId, IReadOnlyList<string>? filterTwitchUserIds, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchUserSubscription>> CheckUserSubscriptionAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result<int>> GetSubscriberCountAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `GetBroadcasterSubscriptionsAsync` | Read paged `GET /subscriptions`, optionally filtered to specific target users (raw Twitch ids). Returns page + cursor + total. Pure Helix I/O — writes nothing locally (no subscriber mirror table exists; consumers serve the list straight from Twitch). Requires `channel:read:subscriptions`. |
| `CheckUserSubscriptionAsync` | Read `GET /subscriptions/user` — whether a target user (raw Twitch id) subscribes to the channel; an empty response (`not_found`) means "not subscribed". Requires `user:read:subscriptions`. |
| `GetSubscriberCountAsync` | Read `GET /subscriptions?first=1`; returns `total`. Requires `channel:read:subscriptions` (pre-checked — closes the "subscriber count always 0" known issue by returning `missing_scope` instead of a silent 0). No state change. |

### 3.4a Live-ops, engagement & discovery sub-clients (the remaining 16 façade categories)

The early draft's coarse `ITwitchLiveOpsApi` bucket was **split into its constituent category clients** — each a single-responsibility Helix I/O wrapper (deps: transport + identity + token only). `broadcaster-liveops.md` owns the consuming controllers/services/scopes/state; the full endpoint + progressive-scope mapping is in `broadcaster-liveops.md` §8.2. Signatures below are the shipped interfaces verbatim (single-line form); `// scope` comments are the required grant.

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

// ── Polls — channel:read:polls (read) / channel:manage:polls (create/end; status TERMINATED|ARCHIVED) ──
public interface ITwitchPollsApi
{
    Task<Result<TwitchPage<TwitchPoll>>> GetPollsAsync(Guid broadcasterId, IReadOnlyList<string>? pollIds, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchPoll>> CreatePollAsync(Guid broadcasterId, CreatePollRequest request, CancellationToken ct = default);
    Task<Result<TwitchPoll>> EndPollAsync(Guid broadcasterId, string pollId, string status, CancellationToken ct = default);
}

// ── Predictions — channel:read:predictions / channel:manage:predictions
//    (EndPredictionAsync: status RESOLVED|CANCELED|LOCKED; winningOutcomeId required for RESOLVED) ──
public interface ITwitchPredictionsApi
{
    Task<Result<TwitchPage<TwitchPrediction>>> GetPredictionsAsync(Guid broadcasterId, IReadOnlyList<string>? predictionIds, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<TwitchPrediction>> CreatePredictionAsync(Guid broadcasterId, CreatePredictionRequest request, CancellationToken ct = default);
    Task<Result<TwitchPrediction>> EndPredictionAsync(Guid broadcasterId, string predictionId, string status, string? winningOutcomeId, CancellationToken ct = default);
}

// ── Raids — channel:manage:raids (target = raw Twitch broadcaster id) ──
public interface ITwitchRaidsApi
{
    Task<Result<TwitchRaid>> StartRaidAsync(Guid broadcasterId, string toTwitchBroadcasterId, CancellationToken ct = default);
    Task<Result> CancelRaidAsync(Guid broadcasterId, CancellationToken ct = default);
}

// ── Ads — channel:edit:commercial (start) / channel:read:ads (schedule) / channel:manage:ads (snooze) ──
public interface ITwitchAdsApi
{
    Task<Result<TwitchCommercial>> StartCommercialAsync(Guid broadcasterId, int lengthSeconds, CancellationToken ct = default);
    Task<Result<TwitchAdSchedule>> GetAdScheduleAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchAdSnooze>> SnoozeNextAdAsync(Guid broadcasterId, CancellationToken ct = default);
}

// ── Schedule — reads app-token/no-scope; mutations channel:manage:schedule.
//    GetICalendarAsync returns raw RFC 5545 text verbatim (Twitch needs no auth; rides the app-token
//    pipeline for uniform rate limiting). Segment create/update return the updated TwitchSchedule. ──
public interface ITwitchScheduleApi
{
    Task<Result<TwitchSchedule>> GetScheduleAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<string>> GetICalendarAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result> UpdateScheduleSettingsAsync(Guid broadcasterId, bool? isVacationEnabled, DateTimeOffset? vacationStartTime, DateTimeOffset? vacationEndTime, string? timezone, CancellationToken ct = default);
    Task<Result<TwitchSchedule>> CreateSegmentAsync(Guid broadcasterId, CreateScheduleSegmentRequest request, CancellationToken ct = default);
    Task<Result<TwitchSchedule>> UpdateSegmentAsync(Guid broadcasterId, string segmentId, UpdateScheduleSegmentRequest request, CancellationToken ct = default);
    Task<Result> DeleteSegmentAsync(Guid broadcasterId, string segmentId, CancellationToken ct = default);
}

// ── Clips — creates clips:edit (VOD/downloads: editor:manage:clips, broadcaster alternatively
//    channel:manage:clips); reads app-token/no-scope. CreateClipAsync returns id + edit URL (TwitchClipStub). ──
public interface ITwitchClipsApi
{
    Task<Result<TwitchClipStub>> CreateClipAsync(Guid broadcasterId, bool? hasDelay, CancellationToken ct = default);
    Task<Result<TwitchClipStub>> CreateClipFromVodAsync(Guid broadcasterId, CreateClipFromVodRequest request, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchClip>>> GetClipsByBroadcasterAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchClip>>> GetClipsByIdsAsync(IReadOnlyList<string> clipIds, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchClipDownload>>> GetClipDownloadUrlsAsync(Guid broadcasterId, string editorId, IReadOnlyList<string> clipIds, CancellationToken ct = default);
}

// ── Videos — reads app-token/no-scope (type all|archive|highlight|upload; period all|day|week|month;
//    sort time|trending|views); delete channel:manage:videos (max 5, returns the ids Twitch confirmed). ──
public interface ITwitchVideosApi
{
    Task<Result<TwitchPage<TwitchVideo>>> GetVideosByBroadcasterAsync(Guid broadcasterId, string? type, string? period, string? sort, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchVideo>>> GetVideosByIdsAsync(IReadOnlyList<string> videoIds, CancellationToken ct = default);
    Task<Result<IReadOnlyList<string>>> DeleteVideosAsync(Guid broadcasterId, IReadOnlyList<string> videoIds, CancellationToken ct = default);
}

// ── Bits — leaderboard + power-ups bits:read; cheermotes no scope (null broadcasterId = global set only).
//    Leaderboard: broadcaster comes from the user token (no broadcaster_id param); count 1–100 default 10;
//    period day|week|month|year|all. NOTE: the generic list envelope surfaces only data[] — the response's
//    date_range and total are not exposed (transport gap). ──
public interface ITwitchBitsApi
{
    Task<Result<IReadOnlyList<TwitchBitsLeaderboardEntry>>> GetBitsLeaderboardAsync(Guid broadcasterId, int? count, string? period, DateTimeOffset? startedAt, string? userId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchCheermote>>> GetCheermotesAsync(Guid? broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchCustomPowerUp>>> GetCustomPowerUpsAsync(Guid broadcasterId, IReadOnlyList<string>? ids, CancellationToken ct = default);
}

// ── Goals — channel:read:goals ──
public interface ITwitchGoalsApi
{
    Task<Result<IReadOnlyList<TwitchCreatorGoal>>> GetCreatorGoalsAsync(Guid broadcasterId, CancellationToken ct = default);
}

// ── Charity — channel:read:charity (campaign read is not_found when none is active) ──
public interface ITwitchCharityApi
{
    Task<Result<TwitchCharityCampaign>> GetCharityCampaignAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchCharityDonation>>> GetCharityCampaignDonationsAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
}

// ── Hype Train — channel:read:hype_train (not_found when the channel has no Hype Train activity) ──
public interface ITwitchHypeTrainApi
{
    Task<Result<TwitchHypeTrainStatus>> GetHypeTrainStatusAsync(Guid broadcasterId, CancellationToken ct = default);
}

// ── Teams — app token, no scope; GetTeamsAsync is keyed on team name OR id (provide one), no tenant ──
public interface ITwitchTeamsApi
{
    Task<Result<IReadOnlyList<TwitchChannelTeam>>> GetChannelTeamsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchTeam>> GetTeamsAsync(string? name, string? teamId, CancellationToken ct = default);
}

// ── Games — app token, no scope, no tenant (at least one identifier across ids/names/igdbIds required) ──
public interface ITwitchGamesApi
{
    Task<Result<IReadOnlyList<TwitchGame>>> GetGamesAsync(IReadOnlyList<string>? ids, IReadOnlyList<string>? names, IReadOnlyList<string>? igdbIds, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchGame>>> GetTopGamesAsync(TwitchPageRequest page, CancellationToken ct = default);
}

// ── Content Classification Labels — app token, no scope, no tenant (locale defaults to en-US when null) ──
public interface ITwitchContentClassificationApi
{
    Task<Result<IReadOnlyList<TwitchContentClassificationLabel>>> GetContentClassificationLabelsAsync(string? locale, CancellationToken ct = default);
}

// ── Whispers — user:manage:whispers (sender = tenant Guid resolved to from_user_id; recipient = raw id) ──
public interface ITwitchWhispersApi
{
    Task<Result> SendWhisperAsync(Guid fromUserId, string toTwitchUserId, string message, CancellationToken ct = default);
}

// ── Guest Star (BETA) — channel:read:guest_star (reads) / channel:manage:guest_star (mutations).
//    Reads carrying moderator_id and every slot/invite mutation send the single resolved Twitch id for
//    both broadcaster_id and moderator_id — the same convention as the moderation sub-client. ──
public interface ITwitchGuestStarApi
{
    Task<Result<TwitchGuestStarChannelSettings>> GetChannelSettingsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result> UpdateChannelSettingsAsync(Guid broadcasterId, UpdateGuestStarSettingsRequest request, CancellationToken ct = default);
    Task<Result<TwitchGuestStarSession>> GetSessionAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchGuestStarSession>> CreateSessionAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchGuestStarSession>> EndSessionAsync(Guid broadcasterId, string sessionId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchGuestStarInvite>>> GetInvitesAsync(Guid broadcasterId, string sessionId, CancellationToken ct = default);
    Task<Result> SendInviteAsync(Guid broadcasterId, string sessionId, string guestTwitchUserId, CancellationToken ct = default);
    Task<Result> DeleteInviteAsync(Guid broadcasterId, string sessionId, string guestTwitchUserId, CancellationToken ct = default);
    Task<Result> AssignSlotAsync(Guid broadcasterId, string sessionId, string guestTwitchUserId, string slotId, CancellationToken ct = default);
    Task<Result> UpdateSlotAsync(Guid broadcasterId, string sessionId, string sourceSlotId, string? destinationSlotId, CancellationToken ct = default);
    Task<Result> DeleteSlotAsync(Guid broadcasterId, string sessionId, string guestTwitchUserId, string slotId, bool? shouldReinviteGuest, CancellationToken ct = default);
    Task<Result> UpdateSlotSettingsAsync(Guid broadcasterId, string sessionId, string slotId, bool? isAudioEnabled, bool? isVideoEnabled, bool? isLive, int? volume, CancellationToken ct = default);
}
```

### 3.4b `ITwitchEventSubConduitsApi` — EventSub conduits and shards (registered, not on the façade)

The 27th Helix sub-client. It is registered scoped beside the 26 façade clients (`DependencyInjection.cs`), but `ITwitchHelixClient` has no accessor for it: its only consumers are `EventSubConduitShardCoordinator` and the conduit handover (twitch-eventsub.md §10), which resolve it directly. **Every call rides the platform app access token** (`TwitchHelixAuth.BotApp`), so it fails with `no_token` on a self-host without an app secret. Conduit mode itself is opt-in: `EventSub:Conduits:Enabled`, off by default.

```csharp
public interface ITwitchEventSubConduitsApi
{
    Task<Result<IReadOnlyList<TwitchConduit>>> GetConduitsAsync(CancellationToken ct = default);                                // GET    /eventsub/conduits
    Task<Result<TwitchConduit>> CreateConduitAsync(int shardCount, CancellationToken ct = default);                              // POST   /eventsub/conduits
    Task<Result<TwitchConduit>> UpdateConduitAsync(string conduitId, int shardCount, CancellationToken ct = default);            // PATCH  /eventsub/conduits
    Task<Result> DeleteConduitAsync(string conduitId, CancellationToken ct = default);                                          // DELETE /eventsub/conduits?id= (idempotent: already deleted = success)
    Task<Result<IReadOnlyList<TwitchConduitShard>>> GetConduitShardsAsync(string conduitId, string? status = null, CancellationToken ct = default);   // GET /eventsub/conduits/shards (follows the cursor)
    Task<Result<TwitchConduitShardUpdateResult>> UpdateConduitShardsAsync(string conduitId, IReadOnlyList<TwitchConduitShardAssignment> shards, CancellationToken ct = default); // PATCH /eventsub/conduits/shards (binds shards to a WebSocket session or webhook)
}
```

The request/response records (`CreatePollRequest`, `TwitchPoll`, …) are the **hand-written** per-category records in `Dtos/Twitch{Category}Dtos.cs` (§4 — no codegen); `broadcaster-liveops.md` §4 owns the app-facing shapes.

### 3.5 Supporting interfaces (Infrastructure-internal, registered in DI)

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

/// Resolves a usable, decrypted Helix bearer token for a call — the bot token, the platform app token,
/// the broadcaster's user token, or the logged-in operator's own token — and exposes scope state for pre-checks.
public interface ITwitchTokenResolver
{
    // Returns the bot account token (service "twitch_bot", no broadcaster); no_token Failure if absent.
    Task<Result<TwitchAccessContext>> GetBotTokenAsync(CancellationToken ct = default);

    // Returns the platform app access token (client_credentials, via ITwitchAppTokenProvider); no_token when
    // the platform app secret is not configured (a secret-less self-host on the shared public client).
    Task<Result<TwitchAccessContext>> GetAppTokenAsync(CancellationToken ct = default);

    // Returns the broadcaster's user token ("twitch"). With allowBotFallback (default true) it falls back to
    // the bot token when no user token exists; with false it fails with no_token instead (the UserStrict mode).
    Task<Result<TwitchAccessContext>> GetBroadcasterTokenAsync(Guid broadcasterId, bool allowBotFallback = true, CancellationToken ct = default);

    // Returns the logged-in operator's OWN Twitch user token — to act AS the operator in channels they
    // moderate, independent of the active tenant (chat-client.md §3.1). no_token when the user has no
    // Twitch identity or connection.
    Task<Result<TwitchAccessContext>> GetUserTokenAsync(Guid userId, CancellationToken ct = default);

    // Forces a single token refresh for the identity behind the context via the auth layer and returns
    // the refreshed context. Called by the transport on a 401 (refresh-and-retry once). Fails when no
    // refresh is possible (e.g. the app/bot token, or the refresh itself failed).
    Task<Result<TwitchAccessContext>> RefreshAsync(TwitchAccessContext context, CancellationToken ct = default);

    // True if the IntegrationConnection backing the resolved token has been granted the scope. A false result
    // also publishes TwitchHelixReauthRequiredEvent (Reason="missing_scope") — the one chokepoint that feeds §3.6.
    Task<bool> HasScopeAsync(Guid broadcasterId, string scope, CancellationToken ct = default);
}

/// Per-token adaptive rate limiter. One bucket per token identity; proactive throttle from
/// Ratelimit-* headers; queue + exponential backoff on 429; user-triggered calls prioritized.
public interface ITwitchRateLimiter
{
    // Awaits a permit for the bucket; returns the lease to dispose after the request completes.
    Task<ITwitchRateLease> AcquireAsync(string tokenBucketKey, TwitchCallPriority priority, CancellationToken ct = default);

    // Feeds observed response headers back so the limiter adapts the bucket's remaining/reset;
    // wasHardLimited = true (a real 429) hard-blocks the bucket until resetsAt.
    void Observe(string tokenBucketKey, int? limit, int? remaining, DateTimeOffset? resetsAt, bool wasHardLimited = false);
}

public interface ITwitchRateLease : IAsyncDisposable;

public enum TwitchCallPriority { Background = 0, UserInteractive = 1 }

/// One immutable bearer context for a single Helix call.
public sealed record TwitchAccessContext(
    string AccessToken,         // decrypted; never logged
    Guid? BroadcasterId,        // null for app/bot token
    string ServiceName,         // "twitch" | "twitch_bot"
    string TokenBucketKey       // stable hash of token identity for the limiter
);
```

*Behavior notes:* `ITwitchTokenResolver` reads `IntegrationConnections` + `IntegrationTokens` (decrypting via the auth/crypto layer, never the raw vault); on 401 the transport calls `ITwitchTokenResolver.RefreshAsync` exactly once (refresh-and-retry), which delegates the actual refresh to the auth layer. `HasScopeAsync` reads `IntegrationConnections.Scopes ([VC:JSON])`; a missing scope short-circuits the call with `missing_scope` + emits `TwitchHelixReauthRequiredEvent`. `ITwitchRateLimiter` is a singleton and has **one** implementation, `TwitchRateLimiter(TimeProvider)`: per-token buckets in a `ConcurrentDictionary`, remaining/reset taken from observed `Ratelimit-*` headers, a hard 429 blocking the bucket until reset, and a two-band gate so a `UserInteractive` call is served before a queued `Background` poll. **Design intent, unbuilt:** a SaaS `DistributedTwitchRateLimiter` delegating to `IRateLimiter` (`scaling-qos.md` §4.2). `Observe` is called by `TwitchRateLimitHandler` after every response, which also publishes `TwitchHelixRateLimitedEvent`; `AddTwitchResilienceHandler` (retry + breaker + timeout, in `ResiliencePolicies`) publishes `TwitchHelixCircuitOpenedEvent`. `ITwitchAppTokenProvider` (singleton) mints and caches the app token and is invalidated on a 401.

### 3.6 Scope bookkeeping (as-built)

Scope state has four moving parts. None of them is a new table owned by the sub-clients.

1. **Declaration.** Each sub-client method that needs a scope carries `[RequiresTwitchScope(TwitchScopes.X)]` (`Application/Contracts/Twitch/RequiresTwitchScopeAttribute.cs`, repeatable). Scope strings are constants on `TwitchScopes`, never literals. `Infrastructure/Identity/TwitchScopeRegistry` (singleton) reflects the attributes once into `AllDeclaredScopes`, plus a hand-kept `ResidualEventSubScopes` set for scopes that gate only an EventSub topic or a non-Helix claim. `AuthService.RequiredScopes` unions with it, so a new attribute widens the login grant automatically.
2. **Feature gating.** `FeatureScopeMap` maps a feature key to its progressive scopes; `IScopeGrantService` (`RequiredScopesFor`, `EnsureFeatureScopesAsync`, `ReconcileGrantedScopesAsync`) turns "feature enabled" into an incremental authorize URL or a reconciled grant. There is no `TwitchScopeRequirements` class.
3. **Runtime detection.** `TwitchTokenResolver.HasScopeAsync` reads `IntegrationConnection.Scopes` and, when the scope is absent, publishes `TwitchHelixReauthRequiredEvent`. `MissingScopeRecordingHandler` records a `ChannelMissingScope` row (idempotent per `(channel, scope)`) and `IScopeNotificationService` posts one debounced chat notice (`ScopeNotificationDebouncer`). A gap also appears as an action-required item (`TwitchGrantGapSource`).
4. **Surfacing.** The diagnostics routes in §5.


---

## 4. DTOs / contracts

All in `NomNomzBot.Application.Contracts.Twitch` (hand-written, domain-grouped `Dtos/Twitch{Category}Dtos.cs` files). **There is no separate wire-DTO layer and no codegen**: these records deserialize straight from Twitch's `snake_case` JSON via the transport's naming policy (no per-property annotations) — the same record is both the wire shape and the public contract. Raw Twitch ids stay `string`; the owning tenant is always a `Guid` **method argument**, never a record field.

### 4.1 Request records

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public sealed record TwitchPageRequest(string? After = null, int PageSize = 100);

public sealed record TwitchContentClassificationLabelChoice(string Id, bool IsEnabled);

public sealed record ModifyChannelInformationRequest(
    string? Title = null,
    string? GameId = null,
    string? BroadcasterLanguage = null,
    int? Delay = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<TwitchContentClassificationLabelChoice>? ContentClassificationLabels = null,
    bool? IsBrandedContent = null
);

// Body carries only the target status ("FULFILLED" | "CANCELED"); the reward id and redemption ids
// are method arguments on ITwitchChannelPointsApi.UpdateRedemptionStatusAsync, never body fields.
public sealed record UpdateRedemptionStatusRequest(string Status);
```

Ban/timeout carry **no request record** — `ITwitchModerationApi.BanUserAsync`/`TimeoutUserAsync` take inline
parameters (`targetTwitchUserId`, `durationSeconds`, `reason`). (The `BanUserRequest` in
`NomNomzBot.Application.Moderation.Dtos` is the moderation module's app-level DTO, unrelated to this contract.)
The remaining request records ship beside their categories in `Dtos/Twitch{Category}Dtos.cs`:
`CreateCustomRewardRequest`, `UpdateCustomRewardRequest`, `UpdateAutoModSettingsRequest`,
`SuspiciousUserStatusRequest`, `UpdateChatSettingsRequest`, `CreatePollRequest`, `CreatePredictionRequest`,
`CreateScheduleSegmentRequest`, `UpdateScheduleSegmentRequest`, `CreateClipFromVodRequest`,
`CreateStreamMarkerRequest`, `TwitchStreamsFilter`, `UpdateGuestStarSettingsRequest`,
`UpdateUserExtensionsRequest`. (`TwitchPageRequest`/`TwitchPage<T>` live in `TwitchPage.cs`, beside the
contracts, not under `Dtos/`.)

### 4.2 Response records

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public sealed record TwitchPage<T>(IReadOnlyList<T> Items, string? NextCursor, int Total);

public sealed record TwitchUser(
    string Id, string Login, string DisplayName, string Type, string BroadcasterType,
    string Description, string ProfileImageUrl, string OfflineImageUrl, int ViewCount,
    DateTimeOffset CreatedAt);   // email deliberately not modelled (needs user:read:email)

public sealed record TwitchChannelInformation(
    string BroadcasterId, string BroadcasterLogin, string BroadcasterName, string BroadcasterLanguage,
    string GameId, string GameName, string Title, int Delay, IReadOnlyList<string> Tags,
    IReadOnlyList<string> ContentClassificationLabels, bool IsBrandedContent);

public sealed record TwitchStream(
    string Id, string UserId, string UserLogin, string UserName, string GameId, string GameName,
    string Type, string Title, IReadOnlyList<string> Tags, int ViewerCount,
    DateTimeOffset StartedAt, string Language, string ThumbnailUrl, bool IsMature);

public sealed record TwitchChannelFollower(
    string UserId, string UserLogin, string UserName, DateTimeOffset FollowedAt);

public sealed record TwitchBroadcasterSubscription(
    string BroadcasterId, string BroadcasterLogin, string BroadcasterName,
    string GifterId, string GifterLogin, string GifterName, bool IsGift,
    string PlanName, string Tier, string UserId, string UserLogin, string UserName);

public sealed record TwitchUserSubscription(
    string BroadcasterId, string BroadcasterLogin, string BroadcasterName, bool IsGift, string Tier,
    string? GifterId = null, string? GifterLogin = null, string? GifterName = null);

public sealed record TwitchSearchCategory(string Id, string Name, string BoxArtUrl);

public sealed record TwitchBannedUser(
    string UserId, string UserLogin, string UserName, DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt, string Reason, string ModeratorId, string ModeratorLogin,
    string ModeratorName);

public sealed record TwitchModerator(string UserId, string UserLogin, string UserName);

public sealed record TwitchVip(string UserId, string UserName, string UserLogin);

public sealed record TwitchChatter(string UserId, string UserLogin, string UserName);

public sealed record TwitchModeratedChannel(
    string BroadcasterId, string BroadcasterLogin, string BroadcasterName);

public sealed record TwitchBlockedTerm(
    string BroadcasterId, string ModeratorId, string Id, string Text,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt);

public sealed record TwitchCustomReward(
    string BroadcasterId, string BroadcasterLogin, string BroadcasterName,
    string Id, string Title, string Prompt, int Cost,
    TwitchCustomRewardImage? Image, TwitchCustomRewardImage DefaultImage, string BackgroundColor,
    bool IsEnabled, bool IsUserInputRequired,
    TwitchCustomRewardMaxPerStreamSetting MaxPerStreamSetting,
    TwitchCustomRewardMaxPerUserPerStreamSetting MaxPerUserPerStreamSetting,
    TwitchCustomRewardGlobalCooldownSetting GlobalCooldownSetting,
    bool IsPaused, bool IsInStock, bool ShouldRedemptionsSkipRequestQueue,
    int? RedemptionsRedeemedCurrentStream, DateTimeOffset? CooldownExpiresAt);
```

The full response-record set (~one file per category: emotes, badges, polls, predictions, raids, ads,
schedule, clips, videos, bits, charity, goals, hype train, teams, games, CCLs, guest star, extensions, …)
lives in `Dtos/Twitch{Category}Dtos.cs` beside these; the §3 signatures name every record they return.
Timestamps are `DateTimeOffset` throughout (never bare `DateTime`).

> These supersede the loosely-named `TwitchUserInfo`/`TwitchChannelInfo`/`TwitchRewardInfo`/etc. that were inlined in the retired `ITwitchApiService.cs`; the new records carry the missing `IsPaused`/`PlanName`/`Language` fields the schema needs.

---

## 5. Controller endpoints

This subsystem's calls are consumed by **existing** controllers (`DashboardController`, `CommunityController`, `StreamController`, `ModerationController`, `RewardsController`). Its one own controller is `TwitchDiagnosticsController`, which lets the dashboard surface scope/connection health (closes the "subscriber count always 0" and "403 chat" known issues by making the missing-scope state observable).

**Role gate** — the diagnostics route is **management plane**. **Gate-1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). **Gate-2** = the `[RequireAction("<key>")]` attribute, which calls `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` and enforces the per-route floor named in the **Action key** column before the service call (403 `FORBIDDEN` when below). The key is a seeded global `ActionDefinition` (schema B.3, §5.1.1); a broadcaster may raise its floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. All under `/api/v1`.

| Route | Verb | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `api/v{version:apiVersion}/twitch/diagnostics/scopes` | GET | — (tenant from JWT/`ICurrentTenantService`) | `StatusResponseDto<TwitchScopeDiagnosticsDto>` | management / Moderator · `twitch:diagnostics:read` |
| `api/v{version:apiVersion}/twitch/diagnostics/missing-scopes` | GET | — | `StatusResponseDto<MissingScopesDto>` | management / Moderator · `twitch:diagnostics:read` |
| `api/v{version:apiVersion}/twitch/diagnostics/regrant` | POST | — | `StatusResponseDto<ScopeRegrantStartDto>` | management / Moderator · `twitch:diagnostics:read` |

```csharp
public sealed record TwitchScopeDiagnosticsDto(
    string ConnectionStatus,                 // IntegrationConnections.Status
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<TwitchScopeRequirementDto> Requirements
);

public sealed record TwitchScopeRequirementDto(
    string Scope,
    string Feature,            // e.g. "subscriber_count"
    bool Granted,
    bool IsProgressive,        // true = requested lazily when the gating feature is enabled, not at login
    string? GatedByFeature     // gating feature/flag key; null unless IsProgressive
);
```

```csharp
// GET missing-scopes — the deduplicated gaps (proactive FeatureScopeMap diff ∪ reactive ChannelMissingScope rows)
public sealed record MissingScopesDto(string ConnectionStatus, IReadOnlyList<MissingScopeDto> Scopes);
public sealed record MissingScopeDto(string Scope, IReadOnlyList<string> Features, bool DetectedAtRuntime, bool ChatNotified);

// POST regrant — starts a secret-free streamer Device Code Flow requesting (granted ∪ missing), so the
// re-consent never drops an existing grant; the client then polls POST /auth/twitch/device/poll.
public sealed record ScopeRegrantStartDto(string DeviceCode, string UserCode, string VerificationUri, int Interval, int ExpiresIn, IReadOnlyList<string> RequestedScopes);
```
`missing-scopes` returns 404 when the tenant has no Twitch connection; `regrant` returns 404 with no connection and 409 when nothing is missing. In this section `TwitchScopeDiagnosticsDto` and its requirement rows live in `Application/DTOs/Twitch/`.

> Progressive scopes (`IsProgressive = true`) are requested on **feature-enable**, not up front — when the gating feature (`GatedByFeature`) is toggled on; the diagnostics matrix surfaces a missing progressive scope as "feature-gated", not an error.

*Behavior:* the service method first calls `IActionAuthorizationService.AuthorizeActionAsync("twitch:diagnostics:read", ...)` for the current tenant (fails closed → `403` problem-details on deny), then reads `IntegrationConnections.Scopes`/`Status`, diffs against the required-scope map (§9 below references the scope constants), and returns the per-feature granted/missing matrix. No mutation. Returns `404` problem-details if the tenant has no Twitch connection.

#### 5.1.1 Seeded `ActionDefinitions` row

The seed is `M("twitch:diagnostics:read", Mod)` in `ActionDefinitionSeeder` (the seed pass that owns the Domain-B B.3 rows, per `roles-permissions.md`). It is a read-only channel-management diagnostics action (Plane `management`, danger tier `low`, permit-grantable); `DefaultLevel` ships at `FloorLevel` (Moderator). Without this row the resolver fails closed and the diagnostics calls 403.

| `ActionKey` | `Plane` | `DefaultLevel` | `FloorLevel` | `FloorTier` | `IsGrantableViaPermit` | `Description` |
|---|---|---|---|---|---|---|
| `twitch:diagnostics:read` | `management` | Moderator | Moderator | `low` | `true` | View this channel's Twitch scope/connection health diagnostics. |

`Id` is `Guid.CreateVersion7()` at seed time; the seed is idempotent on the `ActionKey` unique index (upsert, no duplicate on re-run). `Plane` uses the `AuthPlane` `[VC:enum]`, `FloorTier` the `DangerTier` `[VC:enum]` (matching B.3). Mirrors the twitch-eventsub §5.1.1 seed pattern exactly.

> **Gate wiring (as built):** the `TwitchDiagnosticsController` is Gate-1 (`[Authorize]` + tenant resolved from the JWT via `ICurrentTenantService` — the routes carry no `channelId`; diagnostics are inherently "my own channel") **plus Gate-2**: every action carries `[RequireAction("twitch:diagnostics:read")]`, enforced by `IActionAuthorizationService` against the seeded `ActionDefinitions` row above; self-host collapses to "owner = full". `ITwitchScopeDiagnosticsService` reads the channel's `IntegrationConnection` (Provider `Twitch`) for `Status`/`Scopes` and flattens the progressive `FeatureScopeMap` into the per-feature matrix; `NOT_FOUND` → 404 when the tenant has no Twitch connection. The Helix enforcement path (`TwitchTokenResolver.HasScopeAsync`) reads the same `IntegrationConnection.Scopes` store, so the diagnostics matrix and the runtime pre-check agree.

> No write endpoints are added here — channel/mod/reward mutations are exposed through the existing `StreamController`/`ModerationController`/`RewardsController`, which now call the sub-clients and translate `Result.Failure(ErrorCode)` into the standard problem-details (`missing_scope`→403, `no_token`→409, `rate_limited`→429, `not_found`→404).

---

## 6. Pipeline actions

**None new.** The existing pipeline actions (`BanAction`, `TimeoutAction`, `DeleteMessageAction`, `ShoutoutAction`, `SendMessageAction`, `SendReplyAction`) are repointed **directly** to the sub-clients: moderation actions → `ITwitchModerationApi`, shoutout → `ITwitchChatApi.SendShoutoutAsync`. No `Type` string, config DTO, or registration changes in this subsystem. Chat send/reply stay on `HelixChatProvider`, which now posts `/chat/messages` straight through `ITwitchHelixTransport` on the bot token (the moderation/chat sub-clients deliberately omit plain Send Chat Message, leaving it to this provider).

---

## 7. DI registration

In `NomNomzBot.Infrastructure.DependencyInjection.AddInfrastructure` (the Twitch block, from about line 1424). Lifetimes: HTTP-bound clients **scoped**, the rate limiter and the app-token provider **singleton** (they hold state across requests).

```csharp
// ── HTTP client: resilience (outermost) → rate limit → auth header (closest to the wire) ──
services.AddTransient<TwitchAuthHeaderHandler>();
services.AddTransient<TwitchRateLimitHandler>();
services.AddHttpClient("twitch-helix")
    .AddTwitchResilienceHandler()                       // Polly retry + breaker + timeout, no 4xx retry
    .AddHttpMessageHandler<TwitchRateLimitHandler>()    // adaptive header-driven limiter
    .AddHttpMessageHandler<TwitchAuthHeaderHandler>();  // Client-Id + Bearer from the request options

// ── Singletons: per-token buckets and the platform app token survive requests ──
services.AddSingleton<ITwitchRateLimiter, TwitchRateLimiter>();          // the only implementation (in-process)
services.AddSingleton<ITwitchAppTokenProvider, TwitchAppTokenProvider>();
services.AddSingleton<Identity.TwitchScopeRegistry>();                   // reflects [RequiresTwitchScope] once

// ── Scoped: token resolver, transport, the sub-clients and the façade ──
services.AddScoped<ITwitchTokenResolver, TwitchTokenResolver>();
services.AddScoped<ITwitchHelixTransport, TwitchHelixTransport>();
services.AddScoped<ITwitchChannelsApi, Platform.Transport.Helix.SubClients.TwitchChannelsApi>();
// … one AddScoped line per category: the 26 façade clients (§3.1) plus ITwitchEventSubConduitsApi (§3.4b),
//   impls in Platform/Transport/Helix/SubClients
services.AddScoped<ITwitchHelixClient, TwitchHelixClient>();             // composes the 26; no conduits accessor
```
(The sub-clients are each registered explicitly and the façade composes them; there is no `ITwitchApiService` shim registration, the interface having been retired. `DistributedTwitchRateLimiter` and a deployment-mode branch for the limiter do not exist.)

**Interface → impl:**

| Interface | Implementation | Lifetime | Notes |
|---|---|---|---|
| `ITwitchHelixClient` | `TwitchHelixClient` | Scoped | Composes the 26 category sub-clients (pure accessor). |
| `ITwitchChannelsApi` | `TwitchChannelsApi` | Scoped | Uses `HttpClient("twitch-helix")`, resolver, limiter. |
| `ITwitchModerationApi` | `TwitchModerationApi` | Scoped | |
| `ITwitchSubscriptionsApi` | `TwitchSubscriptionsApi` | Scoped | |
| `ITwitchTokenResolver` | `TwitchTokenResolver` | Scoped | Reads `IntegrationConnections`/`IntegrationTokens`; refreshes via `ITwitchAuthService`. |
| `ITwitchRateLimiter` | `TwitchRateLimiter` | Singleton | In-memory per-token buckets in every profile. **Design intent, unbuilt:** a SaaS `DistributedTwitchRateLimiter` over `IRateLimiter` (`scaling-qos.md` §4.2) — `helix:app` global bucket (720/60s) + per-channel `helix:ch:{id}` sub-budget. |
| `ITwitchEventSubConduitsApi` | `TwitchEventSubConduitsApi` | Scoped | Registered beside the façade clients but not exposed by `ITwitchHelixClient` (§3.4b). |
| `ITwitchAppTokenProvider` | `TwitchAppTokenProvider` | Singleton | Mints and caches the client-credentials app token; `Invalidate()` after a 401. |
| _(retired)_ `ITwitchApiService` | — | — | Deleted. Every caller targets the granular sub-clients directly; no shim. |

**Deployment-profile adapter variants:** the Helix `HttpClient` is profile-agnostic, and today so is the rate limiter: every profile binds the in-process `TwitchRateLimiter`. **Design intent, unbuilt:** on a multi-node SaaS deployment `ITwitchRateLimiter` would bind a `DistributedTwitchRateLimiter` that delegates to the distributed `IRateLimiter` (`scaling-qos.md` §4.2) — a global `helix:app` bucket (720/60s) and a per-channel `helix:ch:{id}` fair sub-budget — so all nodes share one Helix quota. The contract already allows it; sub-clients are unaware which limiter is bound.

---

## 8. Dependencies

Stack-doc libs used by this subsystem (all 2nd-party / in-box except none 3rd-party — the deliberate "hand-rolled core" decision, twitch-rebuild §"Twitch integration"):

| Lib | Party | Use here |
|---|---|---|
| `System.Net.Http` + `IHttpClientFactory` (in-box .NET 10) | 1st | The Helix `HttpClient`, named `"twitch-helix"`. |
| `Microsoft.Extensions.Http.Resilience` 10.7.0 (+ transitive Polly 8.7.0) | 2nd | Retry / circuit-breaker / timeout pipeline — the existing `AddTwitchResilienceHandler`. **No hand-rolled breaker.** |
| `System.Threading.RateLimiter` (in-box .NET 10 BCL) | 1st | Per-token adaptive buckets in `TwitchRateLimiter`. |
| `Newtonsoft.Json` | (app-JSON convention) | App-side `[VC:JSON]` (`IntegrationConnections.Scopes`, `Channels.Tags`) read/write via the EF `ValueConverter`. **The Helix records deserialize via the transport's `snake_case` naming policy** (no per-property annotations, no codegen). |
| _(none — no codegen)_ | — | The Helix records are **hand-written** per category in `Dtos/Twitch{Category}Dtos.cs` (§4); there is no NSwag/NJsonSchema step and no separate wire-DTO layer. |
| EF Core 10 + provider (Npgsql / SQLite via DI adapter) | 2nd / 3rd | `IntegrationConnections` (scope reads in `TwitchTokenResolver`), `Channels` (tenant → Twitch id resolution) and `ChannelMissingScope` (written by `MissingScopeRecordingHandler`) access. `TwitchSubscribers` / `TwitchFollowers` are unbuilt (§1). |
| `Microsoft.Extensions.Logging` `ILogger` + `[LoggerMessage]` | 2nd | Structured logs; **token/PII scrubbed** (PII discipline, logging decision). |
| `NomNomzBot.Domain.Interfaces.IEventBus` (in-process) | 1st | Emits the §2 domain events. |

**Net new dependency for this subsystem: zero** (resilience handler already referenced). Confirms the stack doc's "Net new deps: one (MS.Http.Resilience)" — already present.

---

## 9. Decisions (resolved)

Two implementation conventions are fixed here so the owner codes first-try:

1. **Required-scope map location.** The per-method scope requirements (e.g. `GetSubscriberCountAsync` → `channel:read:subscriptions`, `GetChattersAsync` → `moderator:read:chatters`) are codified as the static `TwitchScopes` constants class (`Application/Contracts/Twitch/TwitchScopes.cs`) plus the `[RequiresTwitchScope]` attribute on each sub-client method, reflected into `TwitchScopeRegistry` and the `FeatureScopeMap` (§3.6); the §5 diagnostics service consults the same map. Single source of truth; no per-call string literals. (The `TwitchScopeRequirements` lookup class of the original design was not built.) Each entry carries a **progressive** flag: base-grant scopes are requested at connect; progressive scopes (e.g. `moderator:read:chatters`) are requested only when the dependent feature is enabled — the diagnostics matrix surfaces a missing progressive scope as "feature-gated", not an error.
2. **`Guid` ↔ `TwitchChannelId` resolution.** Public methods take `Guid broadcasterId`; the sub-clients resolve to `TwitchChannelId` via `Channels` (cached through `ICacheService`) before building the Helix URL. `GetUsersByIdsAsync`/`GetUsersByLoginsAsync`/`SearchCategoriesAsync`/`GetModeratedChannelsAsync` take raw Twitch ids because their subject is not necessarily a local tenant. This split is intentional and matches the schema's "Twitch ids are indexed attributes, `BroadcasterId` is `Guid`" rule.

---

## 10. Test doubles & fixtures

**Principle: no test hits live Twitch — CI is hermetic.** Helix/EventSub/chat are faked at **two seams**: the Helix sub-client / `ITwitchHelixTransport` / `IChatProvider` interface boundary (for tests of *consumers* of Twitch and of a sub-client's orchestration) and the `HttpMessageHandler` boundary (for tests of the *real client's* envelope parsing and rate-limit handling). This is the Twitch-specific complement to the stack doc's testing decision (`2026-06-16-stack-and-dependencies.md` §"Testing / quality"): `Mvc.Testing` `WebApplicationFactory<Program>` for full-stack security tests, **SQLite + in-memory adapters as the default integration DB**, Testcontainers only for the SaaS RLS/pub-sub subset. All doubles below live in the **test projects** (`server/tests/...`), never in a production assembly. The doubles listed are the ones that exist; names from the original design that were never written are marked.

### 10.1 Seam fakes — for tests of Twitch *consumers* and of sub-client orchestration

There is **no** per-sub-client `Fake<X>Api` family (`FakeTwitchChannelsApi` and its siblings were not built). The shipped doubles (all under `server/tests/NomNomzBot.Infrastructure.Tests/`):

| Double | Path | Seam and use |
|---|---|---|
| `CapturingHelixTransport : ITwitchHelixTransport` | `Platform/Transport/Helix/SubClients/Fakes/SubClientFakes.cs` | Records `LastRequest` and every built `TwitchHelixRequest` (verb, path, auth mode, query, body) and returns pre-seeded canned results (`SingleResult`, `ListResult`, `PageResult`, `SendResult`, a `SendResults` queue for a first-fail-then-succeed flow). Every `Twitch*ApiTests` sub-client test uses it. |
| `StubIdentityResolver`, `StubScopeTokenResolver : ITwitchTokenResolver` | same file | A known tenant → Twitch id map; a token resolver that grants exactly the scopes it was built with, so the per-method scope pre-check and `missing_scope` path are asserted. |
| `FakeTwitchTokenResolver : ITwitchTokenResolver` | `Platform/Transport/Helix/FakeTwitchTokenResolver.cs` | Token resolution for the transport-level tests. |
| `CapturingEventBus : IEventBus` | `Platform/Transport/Helix/CapturingEventBus.cs` | Captures the published `TwitchHelix*Event`s so a test asserts the emitted event, not just the return value. |
| `FakeTwitchConduits : ITwitchEventSubConduitsApi` | `Platform/Eventing/FakeTwitchConduits.cs` | An in-memory conduit and shard store for the shard coordinator and handover tests. |
| `SpyChatProvider`, `RecordingChatProvider : IChatProvider` | test-local classes in the chat and handler tests | Record outbound sends so chat-send actions are verified without a socket. |

The fakes return canned app-facing records (the §4 records, with a `Guid` tenant argument), so a test asserts the consumer's resulting **state change / emitted events / side effects**, not Twitch I/O.

### 10.2 Wire-level tests — for the *real* sub-client and transport internals

`RecordingHelixHandler : HttpMessageHandler` (`Platform/Transport/Helix/RecordingHelixHandler.cs`) sits at the wire seam. It records every outgoing request (so a test asserts the `Client-Id` and `Authorization` actually sent) and replays a scripted queue of `Func<HttpResponseMessage>` responses, so a test drives `401` → refresh-and-retry, `429` → reset, `5xx` → retry, and envelope parsing. It backs `TwitchRateLimiterTests`, `TwitchRateLimitHandlerTests` and `TwitchResilienceTests`; there is no network and no live credential.

- **No committed fixture folder.** The design's `tests/Fixtures/Helix/*.json` and `StubHelixHandler` were not built: each test supplies its response body inline as a JSON string, using public-shape sample data only (no tokens, client ids, secrets or real viewer data — consistent with "no fake/seed community data": these are response-shape samples for client parsing, not seeded application data).
- **Rate-limit replay.** The scripted responses carry `Ratelimit-Limit` / `Ratelimit-Remaining` / `Ratelimit-Reset` headers (and, for the throttle path, a `429` with `Retry-After`), so a test asserts `TwitchRateLimitHandler` → `ITwitchRateLimiter.Observe(...)` adapts the bucket and that a `TwitchHelixRateLimitedEvent` (§2) is published on a hard `429`. The `TwitchAuthHeaderHandler` and the `401` → single refresh-and-retry → `TwitchHelixReauthRequiredEvent` path are covered the same way.

### 10.3 EventSub — dispatcher and transport tests

There is **no** `FakeEventSubSource` class. Event-driven behavior is tested by building an `EventSubNotification` and driving the real `INotificationDispatcher` directly (`NotificationDispatcherTests`, the translator tests under `Platform/Eventing/Translators/`, and the handler tests in `Platform/Eventing/EventHandlers/`), so the real dedupe → journal → `IEventBus` fan-out path runs; the test then asserts the resulting domain events and subsystem state. Transport behavior (`TwitchEventSubSessionLifecycleTests`, `TwitchEventSubReconnectTests`, `WebSocketEventSubTransportTests`) and the conduit handover (`TwitchEventSubConduitHandoverTests`, `EventSubConduitShardCoordinatorTests`, `ConduitModeRegistrationTests`) are covered in the same folder with `FakeTwitchConduits`. Cross-reference **twitch-eventsub.md** §3.1 (`IEventSource` seam), §3.4 (dispatcher), §4.1 (`EventSubNotification`).

### 10.4 Binding summary

| Test target | Seam | Double |
|---|---|---|
| A **sub-client's** orchestration (URL, auth mode, scope pre-check, result mapping) | `ITwitchHelixTransport` | `CapturingHelixTransport` + `StubScopeTokenResolver` / `StubIdentityResolver` (§10.1) |
| Twitch **consumers** (followage, sub count, send-message, pipeline actions) | `IChatProvider` / the consumed service interface | `SpyChatProvider` / `RecordingChatProvider` and test-local stubs (§10.1) |
| **Real client internals** (envelope parsing, `Ratelimit-*` adaptation, `401`/`429`/`5xx` handling) | `HttpMessageHandler` on `HttpClient("twitch-helix")` | `RecordingHelixHandler` with inline bodies (§10.2) |
| **Event-driven** subsystem behavior | `INotificationDispatcher.DispatchAsync` | real dispatcher fed a built `EventSubNotification`; `FakeTwitchConduits` for conduits (§10.3) |

No path in this matrix performs a live Twitch request; the suite is fully deterministic and offline.
