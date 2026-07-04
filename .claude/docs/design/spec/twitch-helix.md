# Interface Specification — `twitch-helix` subsystem

**Status:** Directly implementable. Owner codes from this first-try.
**Subsystem area:** Helix API client (`IHttpClientFactory` + resilience), channel info, followers, subs, bans, scopes, rate-limit handling.
**Source of truth:** locked DB schema `2026-06-16-database-schema.md`, design `2026-06-16-twitch-rebuild.md`, stack `2026-06-16-stack-and-dependencies.md`, decisions `2026-06-16-decisions-pending-confirmation.md`.

**Binding conventions** (apply throughout, stated once): namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way (never `.Result`/`.Wait`); `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; **Newtonsoft.Json** for app JSON / `[VC:JSON]` columns; surrogate PKs `guid` via `Guid.CreateVersion7()`; Twitch ids are indexed attribute columns; tenant key `BroadcasterId` is `Guid`; soft-delete (`IsDeleted`+`DeletedAt`) global filter.

> **Scope boundary.** This subsystem is the **Helix request/response client and its supporting concerns**: HTTP pipeline, per-token adaptive rate limiting, scope pre-checks, typed error mapping, and the read/write Helix calls for channel info / followers / subs / bans / moderators / VIPs / rewards / categories. **Out of scope** (owned by sibling subsystems, referenced but not (re)defined here): EventSub transport (`ITwitchEventSubService`), OAuth token issuance/refresh/vault (`ITwitchAuthService`, `IntegrationTokens`, `CryptoKey`), IRC retirement, pipeline action wiring. This spec **replaced** the legacy `ITwitchApiService`/`TwitchApiService` pair — retired entirely, no shim (§3, §8); the built surface is `ITwitchHelixClient` (`server/src/NomNomzBot.Application/Contracts/Twitch/ITwitchHelixClient.cs`) + the granular sub-clients — migrating `Task<bool>`/`Task<T?>` returns to `Task<Result<T>>` and the inline header-poll rate limiting to a dedicated handler.

---

## 1. Entities

This subsystem **reads from and writes to** locked-schema tables; it **owns none exclusively** (Helix is a projection/mutation layer over Twitch state mirrored into these tables by EventSub + sync jobs). It is the canonical **writer** of the rate-limit/scope bookkeeping rows below. All field definitions are authoritative in `2026-06-16-database-schema.md`; referenced here by name + the fields this subsystem touches.

| Table | Schema ref | Role for this subsystem | Key fields touched |
|---|---|---|---|
| `Channels` | A.2 | Read tenant root; resolve `TwitchChannelId` ↔ `BroadcasterId (Guid)`; write `GameId`, `GameName`, `Title`, `Tags`, `Language`, `IsLive` on channel-info sync | `Id (guid PK)`, `TwitchChannelId (string50)`, `Title`, `GameId`, `GameName`, `Tags ([VC:JSON] List<string>)`, `Language`, `IsLive (bool)` |
| `Streams` | F.1 | Read `IsLive`/current session for stream-info calls; written by EventSub (not here) | `Id`, `BroadcasterId`, `TwitchStreamId`, `Title`, `GameId`, `GameName`, `ViewerCountPeak`, `StartedAt`, `EndedAt` |
| `TwitchSubscribers` | F.2 | Upserted from Helix `GET /subscriptions` sync; sub-count source | `Id`, `BroadcasterId`, `SubscriberUserId (guid)`, `SubscriberTwitchUserId`, `Tier`, `IsGift`, `IsActive` |
| `TwitchFollowers` | F.3 | Upserted from Helix `GET /channels/followers` sync; follower-count source | `Id`, `BroadcasterId`, `FollowerUserId (guid)`, `FollowerTwitchUserId`, `FollowedAt` |
| `Rewards` | F.5 | Read/mirror channel-point rewards from Helix `GET /channel_points/custom_rewards` | `Id`, `BroadcasterId`, `TwitchRewardId`, `Title`, `Cost`, `IsEnabled`, `IsPaused` |
| `IntegrationConnections` | E.1 | **Read** granted `Scopes ([VC:JSON])`, `Status`, `ProviderAccountId` for scope pre-check; **write** `LastErrorAt`, `LastRefreshedAt`, `ConsecutiveFailureCount`, `Status` on Helix call outcomes | `Id`, `BroadcasterId (guid, Null=global)`, `Provider (=twitch)`, `ProviderAccountId`, `Status`, `Scopes ([VC:JSON] List<string>)`, `LastRefreshedAt`, `LastErrorAt`, `ConsecutiveFailureCount` |
| `IntegrationTokens` | E.2 | **Read-only** — decrypted access token obtained via `ITwitchAuthService` (this subsystem never reads `CipherText` directly) | (via auth service) |
| `IdempotencyKey` | (Domain Q, `Id bigint PK`; `Scope`,`Key`,`BroadcasterId`,`ExpiresAt`) | **Write** at-most-once guard for mutating Helix calls (ban/timeout/shoutout/redemption-update/channel-update) so retries don't double-apply | `Scope (string100)`, `Key (string255)`, `BroadcasterId (guid Null)`, `ExpiresAt`, `ResultHash` |

> **No new tables introduced.** Self-host rate-limit buckets are **in-memory** (`System.Threading.RateLimiter`, per-token, per-process — not persisted; self-host is single-node). On SaaS, `ITwitchRateLimiter` delegates to the distributed `IRateLimiter` (`scaling-qos.md` §4.2) — a global `helix:app` bucket (720/60s) plus a per-channel `helix:ch:{id}` fair sub-budget — so neither variant adds to this subsystem's data model. Scope state is read from `IntegrationConnections.Scopes`, never duplicated.

---

## 2. Domain events

Emitted on the in-process `IEventBus` (`NomNomzBot.Domain.Interfaces.IEventBus`). All inherit the canonical `DomainEventBase` (`platform-conventions.md` §2.0 — provides `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`; events do **not** redeclare these). Records live in `NomNomzBot.Domain.Events.Twitch`. The publisher sets the inherited `BroadcasterId` to the owning channel — `Guid.Empty` for app/bot-token, non-tenant events (rate-limit / circuit-breaker). Twitch ids ride alongside as strings; no PII free-text beyond display-name snapshots already permitted by schema.

```csharp
namespace NomNomzBot.Domain.Events.Twitch;

/// Raised when a Helix call returns 401 and the single refresh-and-retry also fails,
/// or when a required scope is absent. Drives IntegrationConnections.Status = needs_reauth.
public sealed record TwitchHelixReauthRequiredEvent(
    string Provider,            // "twitch"
    string ServiceName,         // "twitch" | "twitch_bot"
    string Reason,              // "unauthorized" | "missing_scope" | "token_revoked"
    string? MissingScope        // e.g. "channel:read:subscriptions"; null unless Reason="missing_scope"
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

/// Raised after a successful channel-info read syncs new title/game/tags into Channels.
public sealed record TwitchChannelInfoSyncedEvent(
    string TwitchChannelId,
    string Title,
    string GameId,
    string GameName,
    IReadOnlyList<string> Tags,
    string Language
) : DomainEventBase;
```

> `TwitchSubscriber*` / `TwitchFollower*` add/remove events are **owned by the EventSub subsystem** (driven by `channel.subscribe` / `channel.follow`), not emitted here. This subsystem's sub/follower calls are **reconciliation reads**; they upsert rows but do not re-emit per-row events to avoid double-counting.

---

## 3. Service interface(s)

All in `NomNomzBot.Application.Contracts.Twitch`. `ITwitchHelixClient` is the top-level façade exposing the **category sub-clients** by name (§3.1; the built surface is the 26 granular clients, not four coarse buckets). The legacy `ITwitchApiService` has been **retired entirely** — there is no compatibility shim. Every caller (`DashboardController`, `StreamController`, `CommunityController`, `ChannelsController`, `ModerationService`, `RewardService`, the pipeline actions, the event handlers, `HelixChatProvider`) now targets the sub-clients directly (no-backwards-compat: the codebase has no external consumers yet, so the interface was deleted rather than shimmed). Every method returns `Task<Result<T>>` (or `Task<Result>` for void mutations). `Result.Failure` carries `ErrorCode` ∈ `{ "no_token", "missing_scope", "unauthorized", "rate_limited", "not_found", "twitch_error", "transport" }`.

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

### 3.2 `ITwitchChannelsApi` — channel info, followers, categories, stream, channel updates

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public interface ITwitchChannelsApi
{
    // ─ Reads ─
    Task<Result<TwitchUserDto>> GetUserAsync(string twitchUserId, CancellationToken ct = default);
    Task<Result<TwitchChannelInfoDto>> GetChannelInformationAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchStreamInfoDto>> GetStreamAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<int>> GetFollowerCountAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchFollowerDto>>> GetFollowersAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchCategoryDto>>> SearchCategoriesAsync(string query, int first = 10, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchVipDto>>> GetVipsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchChatterDto>>> GetChattersAsync(Guid broadcasterId, CancellationToken ct = default);

    // ─ Writes ─
    Task<Result> ModifyChannelInformationAsync(Guid broadcasterId, ModifyChannelInformationRequest request, CancellationToken ct = default);
}
```

| Method | Behavior (state change / events / side effects) |
|---|---|
| `GetUserAsync` | Read-only `GET /users?id=`. Uses app/bot token. No state change. Returns `not_found` if user array empty. |
| `GetChannelInformationAsync` | Read-only `GET /channels`. On success **upserts** `Channels.Title/GameId/GameName/Tags/Language` and emits `TwitchChannelInfoSyncedEvent`. Resolves `Guid broadcasterId`→`TwitchChannelId` first; `not_found` if channel unknown locally. |
| `GetStreamAsync` | Read-only `GET /streams`. Empty array ⇒ returns a `TwitchStreamInfoDto` with `IsLive=false` (not a failure). Does **not** write `Streams` (EventSub owns lifecycle); may update `Channels.IsLive` for cheap UI freshness. |
| `GetFollowerCountAsync` | Read-only `GET /channels/followers?first=1`; returns `total`. Requires `moderator:read:followers` scope (pre-checked). No state change. |
| `GetFollowersAsync` | Read-only paged `GET /channels/followers`. Returns one page + cursor + total. **Upserts** `TwitchFollowers` rows for the page (reconciliation; no per-row event). Requires `moderator:read:followers`. |
| `SearchCategoriesAsync` | Read-only `GET /search/categories`. App/bot token. No state change. |
| `GetVipsAsync` | Read-only `GET /channels/vips?first=100`. Requires `channel:read:vips`. No state change. |
| `GetChattersAsync` | Read-only paged `GET /chat/chatters?first=1000` (auto-follows cursor, all pages) — the present-viewer list. Uses the **bot/moderator** identity (the bot is a channel mod), matching the existing Helix auth pattern. Requires `moderator:read:chatters` — a **progressive** scope (requested only when a feature that needs the chatter list is enabled, e.g. the `{{random.chatter}}` token / chatter-driven actions), not part of the base grant. No state change; callers cache short-TTL (the `{{random.chatter}}` token is rendered from this cached set, per `commands-pipelines.md` §6.3). Returns `missing_scope` if not granted. |
| `ModifyChannelInformationAsync` | `PATCH /channels` (title/game/tags). **Idempotency-guarded** (`IdempotencyKey` scope `helix:channel:update`). On success writes the new values to `Channels` and emits `TwitchChannelInfoSyncedEvent`. Requires `channel:manage:broadcast`. |

### 3.3 `ITwitchModerationApi` — bans, timeouts, unbans, moderators, message deletion, shoutouts, rewards

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public interface ITwitchModerationApi
{
    // ─ Reads ─
    Task<Result<IReadOnlyList<TwitchBannedUserDto>>> GetBannedUsersAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchModeratorDto>>> GetModeratorsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchModeratedChannelDto>>> GetModeratedChannelsAsync(string twitchUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchRewardDto>>> GetCustomRewardsAsync(Guid broadcasterId, CancellationToken ct = default);

    // ─ Writes ─
    Task<Result> BanUserAsync(Guid broadcasterId, BanUserRequest request, CancellationToken ct = default);
    Task<Result> TimeoutUserAsync(Guid broadcasterId, TimeoutUserRequest request, CancellationToken ct = default);
    Task<Result> UnbanUserAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result> AddModeratorAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result> RemoveModeratorAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result> AddVipAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result> RemoveVipAsync(Guid broadcasterId, string targetTwitchUserId, CancellationToken ct = default);
    Task<Result> DeleteChatMessageAsync(Guid broadcasterId, string messageId, CancellationToken ct = default);
    Task<Result> WarnUserAsync(Guid broadcasterId, string targetTwitchUserId, string reason, CancellationToken ct = default);
    Task<Result> ResolveAutoModMessageAsync(Guid broadcasterId, string messageId, bool approve, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TwitchBlockedTermDto>>> GetBlockedTermsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchBlockedTermDto>> AddBlockedTermAsync(Guid broadcasterId, string text, CancellationToken ct = default);
    Task<Result> RemoveBlockedTermAsync(Guid broadcasterId, string blockedTermId, CancellationToken ct = default);
    Task<Result> ShoutoutAsync(Guid broadcasterId, string toTwitchUserId, string moderatorTwitchUserId, CancellationToken ct = default);
    Task<Result> UpdateRedemptionStatusAsync(Guid broadcasterId, UpdateRedemptionStatusRequest request, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `GetBannedUsersAsync` | Read `GET /moderation/banned?first=100`. Requires `moderator:read:banned_users` (or manage). No state change. |
| `GetModeratorsAsync` | Read `GET /moderation/moderators?first=100`. Requires `moderation:read`. No state change. |
| `GetModeratedChannelsAsync` | Read paged `GET /moderation/channels` (auto-follows cursor, all pages). Requires `user:read:moderated_channels`. No state change. |
| `GetCustomRewardsAsync` | Read `GET /channel_points/custom_rewards?only_manageable_rewards=true`. Requires `channel:read:redemptions`. Mirrors into `Rewards` (upsert by `TwitchRewardId`). |
| `BanUserAsync` | `POST /moderation/bans` (permanent). Idempotency-guarded (`helix:mod:ban`). Requires `moderator:manage:banned_users`. **State:** Twitch ban applied; the local `ModerationActions`/`TwitchSubscribers` rows are written by the moderation subsystem reacting to the resulting EventSub `channel.ban`, not here. |
| `TimeoutUserAsync` | `POST /moderation/bans` with `duration`. Idempotency-guarded. Same scope/side-effect note as ban. |
| `UnbanUserAsync` | `DELETE /moderation/bans`. Idempotency-guarded. Requires `moderator:manage:banned_users`. |
| `AddModeratorAsync` | `POST /moderation/moderators`. Requires `channel:manage:moderators`. |
| `RemoveModeratorAsync` | `DELETE /moderation/moderators`. Requires `channel:manage:moderators`. |
| `AddVipAsync` | `POST /channels/vips`. Requires `channel:manage:vips`. Surfaces `twitch_error` on Twitch's VIP-slot-limit 422. |
| `RemoveVipAsync` | `DELETE /channels/vips`. Requires `channel:manage:vips`. |
| `DeleteChatMessageAsync` | `DELETE /moderation/chat`. Requires `moderator:manage:chat_messages`. |
| `WarnUserAsync` | `POST /moderation/warnings`. Requires `moderator:manage:warnings`. Twitch-native warn — the warned user must acknowledge before chatting again. |
| `ResolveAutoModMessageAsync` | `POST /moderation/automod/message` with `action`=`ALLOW`/`DENY`. Requires `moderator:manage:automod`. Releases or drops a held AutoMod message. |
| `GetBlockedTermsAsync` | Read paged `GET /moderation/blocked_terms?first=100` (auto-follows cursor). Requires `moderator:read:blocked_terms` (or manage). No state change. |
| `AddBlockedTermAsync` | `POST /moderation/blocked_terms`. Requires `moderator:manage:blocked_terms`. Returns the created term (Twitch assigns its id). |
| `RemoveBlockedTermAsync` | `DELETE /moderation/blocked_terms`. Requires `moderator:manage:blocked_terms`. |
| `ShoutoutAsync` | `POST /chat/shoutouts`. Requires `moderator:manage:shoutouts`. Honors Twitch's own shoutout cooldown (surfaces `twitch_error` on 429-cooldown). |
| `UpdateRedemptionStatusAsync` | `PATCH /channel_points/custom_rewards/redemptions` to `FULFILLED`/`CANCELED`. Idempotency-guarded. Requires `channel:manage:redemptions`. Local `RewardRedemptions.Status` is updated by the rewards subsystem via EventSub, not here. |

### 3.4 `ITwitchSubscriptionsApi` — subscriber list/count

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public interface ITwitchSubscriptionsApi
{
    Task<Result<int>> GetSubscriberCountAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchPage<TwitchSubscriberDto>>> GetSubscribersAsync(Guid broadcasterId, TwitchPageRequest page, CancellationToken ct = default);
}
```

| Method | Behavior |
|---|---|
| `GetSubscriberCountAsync` | Read `GET /subscriptions?first=1`; returns `total`. Requires `channel:read:subscriptions` (pre-checked — closes the "subscriber count always 0" known issue by returning `missing_scope` instead of a silent 0). No state change. |
| `GetSubscribersAsync` | Read paged `GET /subscriptions`. Returns page + cursor + total. **Upserts** `TwitchSubscribers` (reconciliation; no per-row event). Requires `channel:read:subscriptions`. |

### 3.4a `ITwitchLiveOpsApi` — broadcaster live-ops writes (polls, predictions, raids, ads, schedule, markers, clips)

Consumed by `broadcaster-liveops.md` (which owns the controllers/services/scopes/state). This sub-client is the thin Helix transport for those writes; full endpoint + progressive-scope mapping is in `broadcaster-liveops.md` §8.2. All `Task<Result<T>>`, idempotency-guarded for the mutating calls.

```csharp
public interface ITwitchLiveOpsApi
{
    // Polls (channel:manage:polls)
    Task<Result<TwitchPollDto>> CreatePollAsync(Guid broadcasterId, CreateTwitchPollRequest req, CancellationToken ct = default);
    Task<Result<TwitchPollDto>> EndPollAsync(Guid broadcasterId, string pollId, string status, CancellationToken ct = default);
    // Predictions (channel:manage:predictions)
    Task<Result<TwitchPredictionDto>> CreatePredictionAsync(Guid broadcasterId, CreateTwitchPredictionRequest req, CancellationToken ct = default);
    Task<Result<TwitchPredictionDto>> UpdatePredictionAsync(Guid broadcasterId, UpdateTwitchPredictionRequest req, CancellationToken ct = default);
    // Raids (channel:manage:raids)
    Task<Result<TwitchRaidDto>> StartRaidAsync(Guid broadcasterId, string toBroadcasterId, CancellationToken ct = default);
    Task<Result> CancelRaidAsync(Guid broadcasterId, CancellationToken ct = default);
    // Ads (channel:edit:commercial + channel:read:ads)
    Task<Result<TwitchCommercialDto>> StartCommercialAsync(Guid broadcasterId, int lengthSeconds, CancellationToken ct = default);
    Task<Result<TwitchAdScheduleDto>> SnoozeNextAdAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchAdScheduleDto>> GetAdScheduleAsync(Guid broadcasterId, CancellationToken ct = default);
    // Stream schedule (channel:manage:schedule)
    Task<Result<TwitchScheduleDto>> GetScheduleAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<TwitchScheduleSegmentDto>> CreateScheduleSegmentAsync(Guid broadcasterId, CreateScheduleSegmentRequest req, CancellationToken ct = default);
    Task<Result<TwitchScheduleSegmentDto>> UpdateScheduleSegmentAsync(Guid broadcasterId, string segmentId, UpdateScheduleSegmentRequest req, CancellationToken ct = default);
    Task<Result> DeleteScheduleSegmentAsync(Guid broadcasterId, string segmentId, CancellationToken ct = default);
    Task<Result> UpdateScheduleSettingsAsync(Guid broadcasterId, UpdateScheduleSettingsRequest req, CancellationToken ct = default);
    // Markers (channel:manage:broadcast) + Clips (clips:edit)
    Task<Result<TwitchStreamMarkerDto>> CreateStreamMarkerAsync(Guid broadcasterId, string? description, CancellationToken ct = default);
    Task<Result<TwitchClipDto>> CreateClipAsync(Guid broadcasterId, bool hasDelay, CancellationToken ct = default);
}
```

The request/response DTOs (`CreateTwitchPollRequest`, `TwitchPollDto`, …) are the codegen'd Helix wire models mapped to app DTOs per §4; `broadcaster-liveops.md` §4 owns the app-facing shapes.

### 3.5 Supporting interfaces (Infrastructure-internal, registered in DI)

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

/// Resolves a usable, decrypted Helix bearer token for a call, choosing the bot app token
/// or the broadcaster's user token, and exposes scope state for pre-checks.
public interface ITwitchTokenResolver
{
    // Returns the bot/app token (Service/Connection "twitch_bot"); null-result Failure if absent.
    Task<Result<TwitchAccessContext>> GetBotTokenAsync(CancellationToken ct = default);

    // Returns the broadcaster's user token ("twitch"); falls back to bot token only for read scopes.
    Task<Result<TwitchAccessContext>> GetBroadcasterTokenAsync(Guid broadcasterId, CancellationToken ct = default);

    // True if the connection backing the resolved token has been granted the scope.
    Task<bool> HasScopeAsync(Guid broadcasterId, string scope, CancellationToken ct = default);
}

/// Per-token adaptive rate limiter. One bucket per token identity; proactive throttle from
/// Ratelimit-* headers; queue + exponential backoff on 429; user-triggered calls prioritized.
public interface ITwitchRateLimiter
{
    // Awaits a permit for the bucket; returns the lease to dispose after the request completes.
    Task<ITwitchRateLease> AcquireAsync(string tokenBucketKey, TwitchCallPriority priority, CancellationToken ct = default);

    // Feeds observed response headers back so the limiter adapts the bucket's remaining/reset.
    void Observe(string tokenBucketKey, int? limit, int? remaining, DateTimeOffset? resetsAt);
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

*Behavior notes:* `ITwitchTokenResolver` reads `IntegrationConnections` + `IntegrationTokens` (decrypting via the auth/crypto layer, never the raw vault); on 401 it triggers `ITwitchAuthService.RefreshTokenAsync` exactly once. `HasScopeAsync` reads `IntegrationConnections.Scopes ([VC:JSON])`; a missing scope short-circuits the call with `missing_scope` + emits `TwitchHelixReauthRequiredEvent`. `ITwitchRateLimiter` is a singleton; on self-host it wraps `System.Threading.RateLimiter` per bucket, on SaaS it delegates to the distributed `IRateLimiter` (`scaling-qos.md` §4.2). `Observe` is called by the rate-limit `DelegatingHandler` after every response.

---

## 4. DTOs / contracts

All in `NomNomzBot.Application.Contracts.Twitch`. **App-facing DTOs use `Guid BroadcasterId`**; raw Twitch ids stay `string`. **Wire DTOs** (the codegen'd Helix request/response models with `snake_case` JSON) live separately under `NomNomzBot.Infrastructure.Twitch.Helix.{Domain}.Dtos` (NSwag-generated, committed, domain-foldered per twitch-rebuild §Codegen) and are mapped to these app DTOs inside the Infrastructure sub-clients — they are **not** part of this public contract.

### 4.1 Request records

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public sealed record TwitchPageRequest(string? After = null, int PageSize = 100);

public sealed record ModifyChannelInformationRequest(
    string? Title = null,
    string? GameId = null,
    IReadOnlyList<string>? Tags = null,
    string? Language = null,
    IReadOnlyList<string>? ContentLabels = null,
    bool? IsBrandedContent = null
);

public sealed record BanUserRequest(string TargetTwitchUserId, string? Reason = null);

public sealed record TimeoutUserRequest(string TargetTwitchUserId, int DurationSeconds, string? Reason = null);

public sealed record UpdateRedemptionStatusRequest(
    string TwitchRewardId,
    string TwitchRedemptionId,
    string Status            // "FULFILLED" | "CANCELED"
);
```

### 4.2 Response records

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public sealed record TwitchPage<T>(IReadOnlyList<T> Items, string? NextCursor, int Total);

public sealed record TwitchUserDto(
    string Id, string Login, string DisplayName, string? ProfileImageUrl, string BroadcasterType);

public sealed record TwitchChannelInfoDto(
    string TwitchChannelId, string Title, string GameId, string GameName,
    IReadOnlyList<string> Tags, string Language);

public sealed record TwitchStreamInfoDto(
    string StreamId, string TwitchUserId, string? GameId, string? GameName,
    string? Title, bool IsLive, int ViewerCount, DateTime? StartedAt);

public sealed record TwitchFollowerDto(
    string UserId, string UserLogin, string UserName, DateTime FollowedAt);

public sealed record TwitchSubscriberDto(
    string UserId, string UserLogin, string UserName, string Tier, bool IsGift,
    string? GifterUserId, string PlanName);

public sealed record TwitchCategoryDto(string Id, string Name, string? BoxArtUrl);

public sealed record TwitchBannedUserDto(
    string UserId, string UserLogin, string UserName, string Reason, DateTime? ExpiresAt);

public sealed record TwitchModeratorDto(string UserId, string UserLogin, string UserName);

public sealed record TwitchVipDto(string UserId, string UserLogin, string UserName);

public sealed record TwitchChatterDto(string UserId, string UserLogin, string UserName);

public sealed record TwitchModeratedChannelDto(
    string BroadcasterId, string BroadcasterLogin, string BroadcasterName);

public sealed record TwitchBlockedTermDto(string Id, string Text, DateTime CreatedAt, DateTime? ExpiresAt);

public sealed record TwitchRewardDto(
    string Id, string Title, int Cost, bool IsEnabled, bool IsPaused,
    string? Prompt, bool UserInputRequired);
```

> These supersede the loosely-named `TwitchUserInfo`/`TwitchChannelInfo`/`TwitchRewardInfo`/etc. that were inlined in the retired `ITwitchApiService.cs`; the new records carry the missing `IsPaused`/`PlanName`/`Language` fields the schema needs.

---

## 5. Controller endpoints

This subsystem's calls are consumed by **existing** controllers (`DashboardController`, `CommunityController`, `StreamController`, `ModerationController`, `RewardsController`) — it does **not** introduce a new public controller. One **new diagnostics endpoint** is added so the dashboard can surface scope/connection health (closes the "subscriber count always 0" and "403 chat" known issues by making the missing-scope state observable).

**Role gate** — the diagnostics route is **management plane**. **Gate 1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's). **Gate 2** = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the **Action key** column before the service call (403 `FORBIDDEN` when below). The key is a seeded global `ActionDefinition` (schema B.3, §5.1.1); a broadcaster may raise its floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. All under `/api/v1`.

| Route | Verb | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `api/v{version:apiVersion}/twitch/diagnostics/scopes` | GET | — (tenant from JWT/`ICurrentTenantService`) | `StatusResponseDto<TwitchScopeDiagnosticsDto>` | management / Moderator · `twitch:diagnostics:read` |

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

> Progressive scopes (`IsProgressive = true`) are requested on **feature-enable**, not up front — when the gating feature (`GatedByFeature`) is toggled on; the diagnostics matrix surfaces a missing progressive scope as "feature-gated", not an error.

*Behavior:* the service method first calls `IActionAuthorizationService.AuthorizeActionAsync("twitch:diagnostics:read", ...)` for the current tenant (fails closed → `403` problem-details on deny), then reads `IntegrationConnections.Scopes`/`Status`, diffs against the required-scope map (§9 below references the scope constants), and returns the per-feature granted/missing matrix. No mutation. Returns `404` problem-details if the tenant has no Twitch connection.

#### 5.1.1 Seeded `ActionDefinitions` row (this subsystem owns this seed entry)

The action key the diagnostics service-body gate resolves is **not** defined elsewhere — this subsystem adds it to the `[GLOBAL, seed]` `ActionDefinitions` seed set in the existing `DataSeeder` (the same seed pass that owns Domain-B B.3 rows, per `roles-permissions.md`). It is a read-only channel-management diagnostics action (Plane `management`, danger tier `low`, permit-grantable); `DefaultLevel` ships at `FloorLevel` (read ≥ Moderator 10). Without this row the resolver fails closed and the diagnostics call 403s.

| `ActionKey` | `Plane` | `DefaultLevel` | `FloorLevel` | `FloorTier` | `IsGrantableViaPermit` | `Description` |
|---|---|---|---|---|---|---|
| `twitch:diagnostics:read` | `management` | 10 (Moderator) | 10 (Moderator) | `low` | `true` | View this channel's Twitch scope/connection health diagnostics. |

`Id` is `Guid.CreateVersion7()` at seed time; the seed is idempotent on the `ActionKey` unique index (upsert, no duplicate on re-run). `Plane` uses the `AuthPlane` `[VC:enum]`, `FloorTier` the `DangerTier` `[VC:enum]` (matching B.3). Mirrors the twitch-eventsub §5.1.1 seed pattern exactly.

> **Implementation status (deferred Gate-2):** the `TwitchDiagnosticsController` ships **Gate-1 only** — `[Authorize]` + tenant resolved from the JWT via `ICurrentTenantService` (the route carries no `channelId`; diagnostics are inherently "my own channel"). The Gate-2 floor (`twitch:diagnostics:read`) and the seeded `ActionDefinitions` row above are **deferred to the roles-permissions subsystem** (`IActionAuthorizationService`), which is not built yet — no controller in this codebase wires Gate-2 today, so self-host collapses to "owner = full". The service reads the channel's `IntegrationConnection` (Provider `Twitch`) for `Status`/`Scopes` and flattens the progressive `FeatureScopeMap` into the per-feature matrix; `NOT_FOUND` → 404 when the tenant has no Twitch connection. **Note:** the live Helix *enforcement* path (`TwitchTokenResolver.HasScopeAsync`) still reads the legacy `Service.Scopes` store while login writes `IntegrationConnection.Scopes` — diagnostics read the login-truthful `IntegrationConnection`; reconciling the token resolver onto the same store is tracked separately.

> No write endpoints are added here — channel/mod/reward mutations are exposed through the existing `StreamController`/`ModerationController`/`RewardsController`, which now call the sub-clients and translate `Result.Failure(ErrorCode)` into the standard problem-details (`missing_scope`→403, `no_token`→409, `rate_limited`→429, `not_found`→404).

---

## 6. Pipeline actions

**None new.** The existing pipeline actions (`BanAction`, `TimeoutAction`, `DeleteMessageAction`, `ShoutoutAction`, `SendMessageAction`, `SendReplyAction`) are repointed **directly** to the sub-clients: moderation actions → `ITwitchModerationApi`, shoutout → `ITwitchChatApi.SendShoutoutAsync`. No `Type` string, config DTO, or registration changes in this subsystem. Chat send/reply stay on `HelixChatProvider`, which now posts `/chat/messages` straight through `ITwitchHelixTransport` on the bot token (the moderation/chat sub-clients deliberately omit plain Send Chat Message, leaving it to this provider).

---

## 7. DI registration

In `NomNomzBot.Infrastructure.DependencyInjection.AddInfrastructure` (extends the existing Twitch block, lines ~244–262). Lifetimes follow the existing pattern: HTTP-bound clients **scoped**, the rate limiter **singleton** (holds per-token buckets across requests).

```csharp
// ── HTTP clients (existing) ──
services.Configure<TwitchOptions>(configuration.GetSection(TwitchOptions.SectionName));
services.AddHttpClient("twitch-helix")
    .AddTwitchResilienceHandler()                       // existing Polly retry+breaker+timeout
    .AddHttpMessageHandler<TwitchRateLimitHandler>()    // NEW: adaptive header-driven limiter
    .AddHttpMessageHandler<TwitchAuthHeaderHandler>();  // NEW: injects Bearer + Client-Id, scrubs from logs

// ── Rate limiter: singleton, per-token buckets, survives requests ──
// Adapter selected by App__DeploymentMode: in-process for self-host, distributed for SaaS.
if (deploymentMode == DeploymentMode.SaaS)
    services.AddSingleton<ITwitchRateLimiter, DistributedTwitchRateLimiter>();  // delegates to IRateLimiter (scaling-qos §4.2)
else
    services.AddSingleton<ITwitchRateLimiter, TwitchRateLimiter>();             // in-process System.Threading.RateLimiter
services.AddTransient<TwitchRateLimitHandler>();        // DelegatingHandler, resolves the singleton limiter
services.AddTransient<TwitchAuthHeaderHandler>();

// ── Token resolver: scoped (reads IntegrationConnections via scoped DbContext) ──
services.AddScoped<ITwitchTokenResolver, TwitchTokenResolver>();

// ── Sub-clients: scoped ──
services.AddScoped<ITwitchChannelsApi, TwitchChannelsApi>();
services.AddScoped<ITwitchModerationApi, TwitchModerationApi>();
services.AddScoped<ITwitchSubscriptionsApi, TwitchSubscriptionsApi>();
services.AddScoped<ITwitchLiveOpsApi, TwitchLiveOpsApi>();

// ── Façade: scoped (composes the sub-clients) ──
services.AddScoped<ITwitchHelixClient, TwitchHelixClient>();
```
(The granular per-category sub-clients are each registered explicitly — 26 of them — and the façade composes them; there is no `ITwitchApiService` shim registration, the interface having been retired.)

**Interface → impl:**

| Interface | Implementation | Lifetime | Notes |
|---|---|---|---|
| `ITwitchHelixClient` | `TwitchHelixClient` | Scoped | Composes the 26 category sub-clients (pure accessor). |
| `ITwitchChannelsApi` | `TwitchChannelsApi` | Scoped | Uses `HttpClient("twitch-helix")`, resolver, limiter. |
| `ITwitchModerationApi` | `TwitchModerationApi` | Scoped | |
| `ITwitchSubscriptionsApi` | `TwitchSubscriptionsApi` | Scoped | |
| `ITwitchTokenResolver` | `TwitchTokenResolver` | Scoped | Reads `IntegrationConnections`/`IntegrationTokens`; refreshes via `ITwitchAuthService`. |
| `ITwitchRateLimiter` | `TwitchRateLimiter` (self-host) / `DistributedTwitchRateLimiter` (SaaS) | Singleton | Selected by `App__DeploymentMode`. Self-host: in-memory per-token buckets (`System.Threading.RateLimiter`). SaaS: delegates to distributed `IRateLimiter` (`scaling-qos.md` §4.2) — `helix:app` global bucket (720/60s) + per-channel `helix:ch:{id}` sub-budget. |
| _(retired)_ `ITwitchApiService` | — | — | Deleted. Every caller targets the granular sub-clients directly; no shim. |

**Deployment-profile adapter variants:** the Helix `HttpClient` itself is profile-agnostic (one `HttpClient` on both self-host and SaaS). The single profile divergence is the rate-limiter *coordination*. `ITwitchRateLimiter` has two registrations selected by `App__DeploymentMode`: self-host binds the in-process `TwitchRateLimiter` (per-process `System.Threading.RateLimiter` buckets); SaaS binds `DistributedTwitchRateLimiter`, which delegates to the distributed `IRateLimiter` (`scaling-qos.md` §4.2) — a global `helix:app` bucket (720/60s) and a per-channel `helix:ch:{id}` fair sub-budget — so multi-node nodes share one Helix quota. Both implement the same `ITwitchRateLimiter` contract; sub-clients are unaware which is bound. This adapter selection depends on the `IRateLimiter` abstraction from `scaling-qos.md` §4.2.

---

## 8. Dependencies

Stack-doc libs used by this subsystem (all 2nd-party / in-box except none 3rd-party — the deliberate "hand-rolled core" decision, twitch-rebuild §"Twitch integration"):

| Lib | Party | Use here |
|---|---|---|
| `System.Net.Http` + `IHttpClientFactory` (in-box .NET 10) | 1st | The Helix `HttpClient`, named `"twitch-helix"`. |
| `Microsoft.Extensions.Http.Resilience` 10.7.0 (+ transitive Polly 8.7.0) | 2nd | Retry / circuit-breaker / timeout pipeline — the existing `AddTwitchResilienceHandler`. **No hand-rolled breaker.** |
| `System.Threading.RateLimiter` (in-box .NET 10 BCL) | 1st | Per-token adaptive buckets in `TwitchRateLimiter`. |
| `Newtonsoft.Json` | (app-JSON convention) | App-side `[VC:JSON]` (`IntegrationConnections.Scopes`, `Channels.Tags`) read/write via the EF `ValueConverter`. **Wire DTOs use `System.Text.Json`** to match Twitch `snake_case` + the codegen'd models. |
| NSwag / NJsonSchema | 3rd, **dev-time only** | Generate the committed Helix wire DTOs from the Twitch OpenAPI spec; not a runtime dep, not Roslyn. |
| EF Core 10 + provider (Npgsql / SQLite via DI adapter) | 2nd / 3rd | `IntegrationConnections`, `Channels`, `TwitchSubscribers`, `TwitchFollowers`, `Rewards`, `IdempotencyKey` access through `IUnitOfWork` + repositories. |
| `Microsoft.Extensions.Logging` `ILogger` + `[LoggerMessage]` | 2nd | Structured logs; **token/PII scrubbed** (PII discipline, logging decision). |
| `NomNomzBot.Domain.Interfaces.IEventBus` (in-process) | 1st | Emits the §2 domain events. |

**Net new dependency for this subsystem: zero** (resilience handler already referenced). Confirms the stack doc's "Net new deps: one (MS.Http.Resilience)" — already present.

---

## 9. Decisions (resolved)

Two implementation conventions are fixed here so the owner codes first-try:

1. **Required-scope map location.** The per-method scope requirements (e.g. `GetSubscriberCountAsync` → `channel:read:subscriptions`, `GetChattersAsync` → `moderator:read:chatters`) are codified as a static `TwitchScopes` constants class + a `TwitchScopeRequirements` lookup in `NomNomzBot.Infrastructure.Twitch`, consulted by both `HasScopeAsync` and the §5 diagnostics endpoint. Single source of truth; no per-call string literals. Each entry carries a **progressive** flag: base-grant scopes are requested at connect; progressive scopes (e.g. `moderator:read:chatters`) are requested only when the dependent feature is enabled — the diagnostics matrix surfaces a missing progressive scope as "feature-gated", not an error.
2. **`Guid` ↔ `TwitchChannelId` resolution.** Public methods take `Guid broadcasterId`; the sub-clients resolve to `TwitchChannelId` via `Channels` (cached through `ICacheService`) before building the Helix URL. `GetUserAsync`/`SearchCategoriesAsync`/`GetModeratedChannelsAsync` take raw Twitch ids because their subject is not necessarily a local tenant. This split is intentional and matches the schema's "Twitch ids are indexed attributes, `BroadcasterId` is `Guid`" rule.

---

## 10. Test doubles & fixtures

**Principle: no test hits live Twitch — CI is hermetic.** Helix/EventSub/chat are faked at **two seams**: the Helix sub-client (`ITwitchChannelsApi`, …) / `IChatProvider` interface boundary (for tests of *consumers* of Twitch) and the `HttpMessageHandler` boundary (for tests of the *real client's* deserialization + rate-limit handling). This is the Twitch-specific complement to the stack doc's testing decision (`2026-06-16-stack-and-dependencies.md` §"Testing / quality"): `Mvc.Testing` `WebApplicationFactory<Program>` for full-stack security tests, **SQLite + in-memory adapters as the default integration DB**, Testcontainers only for the SaaS RLS/pub-sub subset. All doubles below live in the **test projects** (`tests/...`), never in a production assembly.

### 10.1 Seam fakes — for unit/integration tests of Twitch *consumers*

A consumer test (followage, subscriber count, chatters, send-message, a pipeline action) substitutes a hand-written fake at the public interface so it runs with **zero network**. The fakes return canned app-facing domain objects (the §4 DTOs, with `Guid BroadcasterId`), letting a test assert the consumer's resulting **state change / emitted events / side effects** rather than Twitch I/O.

- Per-sub-client fakes (`FakeTwitchChannelsApi : ITwitchChannelsApi`, etc. — one per consumed sub-client, §3) — each method returns a pre-seeded `Result<T>` (success canned-DTO or a specific `Result.Failure(ErrorCode)` from `{ "no_token", "missing_scope", "unauthorized", "rate_limited", "not_found", "twitch_error", "transport" }`) so consumers can be tested against both happy-path and every failure mode.
- A fake `IChatProvider` (`HelixChatProvider` seam, `scaling-qos.md` §6 / `commands-pipelines.md` §6) — records outbound sends and yields canned inbound messages, so chat-send actions and `{{random.chatter}}`-style reads are verified without a socket.

```csharp
namespace NomNomzBot.Application.Tests.Fakes;   // also Infrastructure.Tests.Fakes per the consuming layer

public sealed class FakeTwitchChannelsApi : ITwitchChannelsApi   // pattern repeats per consumed sub-client
{
    // Tests pre-seed canned Result<T> per method (success DTO or a specific Result.Failure(ErrorCode)).
    // No HttpClient, no token resolver, no network. Captures call args for behavioral assertions.
}
```

These never reference `HttpClient` or `ITwitchRateLimiter`; they replace the whole subsystem at its seam.

### 10.2 Recorded Helix fixtures — for testing the *real* `TwitchHelixClient` sub-client internals

To exercise the **actual** sub-client deserialization (wire DTO → §4 app DTO mapping) **and** the adaptive `ITwitchRateLimiter` reading `Ratelimit-*` headers, committed sample Helix JSON responses are replayed through a stub `HttpMessageHandler` / `DelegatingHandler` injected into `HttpClient("twitch-helix")`. No live call, fully deterministic.

- **Fixture location:** `tests/Fixtures/Helix/` — one JSON file per Helix shape, named after the endpoint (e.g. `get-users.json`, `get-channels.json`, `get-subscriptions.json`, `get-channels-followers.json`, `get-chatters.json`).
- **Provenance:** captured from the **Twitch Helix OpenAPI examples** (the same spec NSwag generates the wire DTOs from, §4/§8) or from real responses, with **all tokens, client-ids, and secrets scrubbed** before commit — fixtures carry only public-shape sample data, no live credentials and no real-viewer PII (consistent with "no fake/seed community data" — these are response-shape fixtures for client parsing, not seeded application data).
- **Rate-limit replay:** the stub handler attaches synthetic `Ratelimit-Limit` / `Ratelimit-Remaining` / `Ratelimit-Reset` response headers (and, for the throttle path, a `429` with `Retry-After`) so a test can assert `TwitchRateLimitHandler` → `ITwitchRateLimiter.Observe(...)` adapts the bucket and that a `TwitchHelixRateLimitedEvent` (§2) is emitted on a hard `429`. Pairs with the `TwitchAuthHeaderHandler` (§7) for the `401` → single refresh-and-retry → `TwitchHelixReauthRequiredEvent` path.

```csharp
namespace NomNomzBot.Infrastructure.Tests.Twitch;

// Replays a committed fixture body + canned Ratelimit-* headers for one request, in-process.
public sealed class StubHelixHandler(string fixtureBody, IReadOnlyDictionary<string, string> responseHeaders)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct);
}
```

### 10.3 EventSub — `FakeEventSubSource` harness

Event-driven subsystem behavior (a handler reacting to `channel.chat.message`, `stream.online`, `channel.follow`, …) is tested by injecting notification frames **straight into the dispatcher** — no live WebSocket/conduit. The harness builds a transport-agnostic `EventSubNotification` (twitch-eventsub.md §4.1) and calls `INotificationDispatcher.DispatchAsync(...)` (twitch-eventsub.md §3.4), driving the real dedupe → journal → `IEventBus` fan-out path; the test then asserts the resulting domain event(s) and subsystem state. Cross-reference **twitch-eventsub.md** §3.1 (`IEventSource` seam), §3.4 (dispatcher), §4.1 (`EventSubNotification`).

```csharp
namespace NomNomzBot.Infrastructure.Tests.Twitch;

// Feeds canned EventSub notification frames into the real INotificationDispatcher — no WS/conduit.
public sealed class FakeEventSubSource(INotificationDispatcher dispatcher)
{
    public Task<Result<NotificationDispatchResult>> InjectAsync(EventSubNotification notification, CancellationToken ct = default);
}
```

### 10.4 Binding summary

| Test target | Seam | Double |
|---|---|---|
| Twitch **consumers** (followage, sub count, chatters, send-message, pipeline actions) | Helix sub-client / `IChatProvider` interface | per-sub-client fakes (`FakeTwitchChannelsApi`, …) + chat fake (§10.1) |
| **Real client internals** (wire→app DTO mapping, `Ratelimit-*` adaptation, `401`/`429` handling) | `HttpMessageHandler` on `HttpClient("twitch-helix")` | `StubHelixHandler` + recorded fixtures `tests/Fixtures/Helix/` (§10.2) |
| **Event-driven** subsystem behavior | `INotificationDispatcher.DispatchAsync` | `FakeEventSubSource` (§10.3, twitch-eventsub.md §3.4) |

No path in this matrix performs a live Twitch request; the suite is fully deterministic and offline.
