# Interface Specification — `music-sr` subsystem

**Status:** Implementable. Code from this directly.
**Area:** ONE interleaved fair song-request queue across Spotify + YouTube (drip-feed/browser-source playback sequencer), track metadata, request provenance, tiered allowances, public SR-page tokens, now-playing widget feed.
**Conventions:** C# namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; Nullable enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI (no MediatR, no Roslyn); responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; Newtonsoft.Json for app JSON; surrogate PKs = `Guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete global filter.

> **Migration note — load-bearing.** The live code (`MusicService`, `IMusicService`, `IMusicProvider`, `IMusicConfigService`, `MusicController`) is **string-keyed** (`string broadcasterId`), keeps the **queue in process memory** (`Dictionary<string, FairQueue<…>>`), and reads provider connectivity from the **old `Service` entity** (`_db.Services`). The locked schema replaces all of that: `BroadcasterId` becomes `Guid`, the queue is **persisted** in `SongRequestQueues` / `SongRequestItems`, and provider config/tokens move to `IntegrationConnections` + the generic `MusicProviderConfig` (one row per provider, keyed by registry `Provider`). This spec defines the **target** surface. Where a type already exists, **EXTEND/widen it in place** (do not create a parallel `ISongRequestService` alongside `IMusicService` — the existing `IMusicService` is rewidened to `Guid` and the new persistence methods are added to it). New types are only introduced where no existing equivalent exists (the SR-page token service, the now-playing feed broadcaster, the persistence-backed config records).

---

## 1. Entities (owned by this subsystem — defined in the LOCKED schema, not redefined here)

All from `docs/design/2026-06-16-database-schema.md`. This subsystem **owns** L.4/L.5/L.6, the song-bump raffle tables L.7/L.8/L.9, and the generic provider config table `MusicProviderConfig` (E.5). It **reads** `IntegrationConnections`/`IntegrationTokens` (Domain E, owned by integrations) and `Channels.OverlayToken` (A.2, owned by identity), and **debits channel points through** the economy `CatalogPurchases` (K.11, owned by economy) for raffle entry — the same `CatalogPurchaseId` link the paid lane uses (§3.8/§3.11).

| Table | Schema ref | Key fields (type) this subsystem touches |
|---|---|---|
| **`SongRequestQueues`** | L.4 | `Id Guid PK`; `BroadcasterId Guid (FK→Channels, **Unique**)`; `IsOpen bool`; `IsPaused bool`; `MaxQueueLength int`; `AllowExplicit bool`; `MinYouTubeTrustScore decimal(8,4)?` (broadcaster-set YouTube auto-approval floor; Spotify never trust-gated; `Vip`/`Moderator`-and-above bypass by role); `SubscriberOnly bool`; `MinStandingToRequest string(20) [VC:enum]` (community-standing floor to submit; default `everyone`); `EnabledProviders text [VC:JSON] List<string>` (subset of `["spotify","youtube"]` — which providers accept requests; one or both); `ProviderPriority text [VC:JSON] List<string>` (preferred provider for AMBIGUOUS requests + the cross-resolve target — §3.1); `CrossResolveForeignLinks bool` (default true); `PendingLimits text [VC:JSON] Dictionary<string,int?>` (per-standing concurrent-pending cap; keys `everyone`/`subscriber_t1`/`subscriber_t2`/`subscriber_t3`/`vip`/`moderator`/`broadcaster`; `null`=unlimited; defaults 2/4/4/4/10/null/null); `PaidPendingLimit int?` (channel-point lane cap, null=off); `PaidExtraSlotEnabled bool` (default false); `QueueJumpEnabled bool` (default false); `PerStreamLimit int?` (lifetime-in-stream per-user, null=off); `MaxDurationFreeSeconds int` (default 360); `MaxDurationPaidSeconds int` (default 600); `StripYouTubeAds bool` (default true); `AutoBumpFirstSong bool` (default false — each requester's FIRST song this stream lands in the auto-bump band, §3.8); `RaffleEnabled bool` (default false); `RaffleEntryCost int` (default 0, channel points); `RaffleTicketsPerUser int` (default 1); `RaffleWinnerCount int` (default 1); `RaffleIntervalMinutes int?` (null = manual-only `!raffle`; value = auto-run cadence); `SpotifyLockedDeviceId string(255)?`; `SpotifyLockedDeviceName string(255)?`; `CreatedAt/UpdatedAt`. One row per channel — the single interleaved fair queue both providers feed. `MaxPerUser` **removed** (superseded by `PendingLimits`). |
| **`SongRequestItems`** `[soft-delete]` | L.5 | `Id Guid PK`; `BroadcasterId Guid (FK→Channels, Index)`; `QueueId Guid (FK→SongRequestQueues, Index)`; `Provider string(20) [VC:enum]` (`spotify`\|`youtube`); `ProviderTrackId string(255) Index`; `Title string(500)?`; `Artist string(500)?`; `DurationSeconds int?`; `ThumbnailUrl string(2048)?`; `RequestedByUserId Guid (FK→Users, Index)`; `RequestedByTwitchUserId string(50) Index` **[PII-hash]**; `RequestedByDisplayNameSnapshot string(255)?` **[PII-scrub]**; `Position int Index`; `Status string(20) [VC:enum] Index` (`queued`\|`playing`\|`waiting`\|`retrying`\|`played`\|`skipped`\|`rejected`); `RejectionReason string(100)?`; `RetryCount int` (default 0); `FailureReason string(100)?`; `NextRetryAt timestamp?`; `PriorityBand string(20) Index [VC:enum]` (`bump`\|`auto_bump`\|`normal`, default `normal`) — three-band ordering, bands stack bump→auto_bump→normal, `Position` orders WITHIN a band (§3.8); `BumpSource string(20)? [VC:enum]` (`raffle`\|`command`\|`redeem`; null unless `PriorityBand=bump`) — provenance for display/analytics; `CatalogPurchaseId bigint? (FK→CatalogPurchases)` (set on an opt-in queue-jump or extra-slot channel-point redeem — §3.8; null on every ordinary request); `RequestedAt timestamp`; `PlayedAt timestamp?`; `CreatedAt/UpdatedAt/DeletedAt`. Single canonical SR item; history = this table filtered by terminal `Status`. `waiting`/`retrying` = the failure taxonomy (§3.x). |
| **`SongRequestTrustScores`** | L.6 | `Id Guid PK`; `BroadcasterId Guid (FK→Channels, Index)`; `RequesterUserId Guid (FK→Users, Index)`; `RequesterTwitchUserId string(50) Index` **[PII-hash]**; `Score decimal(8,4)`; `TotalRequests int`; `PlayedCount int`; `SkippedCount int`; `RejectedCount int`; `IsBlocked bool Index`; `LastRequestAt timestamp?`; `CreatedAt/UpdatedAt`. **Unique** `(BroadcasterId, RequesterUserId)`. Per-requester trust to gate/prioritize. |
| **`SongRequestRaffles`** `[soft-delete]` | L.7 | `Id Guid PK`; `BroadcasterId Guid (FK→Channels, Index)`; `QueueId Guid (FK→SongRequestQueues, Index)`; `Status string(20) [VC:enum] Index` (`open`\|`drawn`\|`cancelled`, default `open`); `EntryCostSnapshot int`; `TicketsPerUserSnapshot int`; `WinnerCountSnapshot int`; `Trigger string(20) [VC:enum]` (`manual`\|`auto`); `OpenedAt timestamp`; `DrawnAt timestamp?`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId) WHERE Status='open'` (one open raffle per channel). One round per channel; config snapshotted at open so an in-flight raffle is unaffected by later L.4 edits. |
| **`SongRequestRaffleEntries`** `[append-only]` | L.8 | `Id bigint PK`; `BroadcasterId Guid (FK→Channels, Index)`; `RaffleId Guid (FK→SongRequestRaffles, Index)`; `EntrantUserId Guid (FK→Users, Index)`; `EntrantTwitchUserId string(50) Index` **[PII-hash]**; `EntrantDisplayNameSnapshot string(255)?` **[PII-scrub]**; `TicketCount int` (default 1, ≤ `TicketsPerUserSnapshot`); `CatalogPurchaseId bigint? (FK→CatalogPurchases)` (channel-point debit when `EntryCostSnapshot>0`; mirrors L.5); `IsWinner bool Index` (default false); `CreatedAt Index`. **Unique** `(RaffleId, EntrantUserId)`; **Index** `(BroadcasterId, EntrantUserId)`. One entry per (raffle, user). |
| **`SongRequestBumpTokens`** | L.9 | `Id Guid PK`; `BroadcasterId Guid (FK→Channels, Index)`; `UserId Guid (FK→Users, Index)`; `UserTwitchUserId string(50) Index` **[PII-hash]**; `TokenCount int` (default 0); `CreatedAt/UpdatedAt`. **Unique** `(BroadcasterId, UserId)`. Bump-token balance granted to a winner with no queued song; consumed on their next request to place it in the bump band. |
| **`MusicProviderConfig`** `[soft-delete]` | E.5 | `Id Guid PK`; `BroadcasterId Guid (FK→Channels, Index)` (ITenantScoped); `Provider string(30) Index` (registry key); `ConnectionId Guid (FK→IntegrationConnections, **Unique**)`; shared `AllowSongRequests bool`; `MaxQueueLength int`; `BlockExplicit bool`; `ProviderSettings text [VC:JSON]` (provider-specific knobs — Spotify: `Market`/`RequirePlaylistContext`/`FallbackPlaylistUri`; YouTube: `Region`/`MaxVideoDurationSeconds`/`BlockAgeRestricted`/`EmbeddableOnly`; other registered providers: their own — shape validated by the provider's registered settings schema); `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, Provider)`. One row per (channel, provider); adding a provider needs no new table/migration. |
| `IntegrationConnections` (read) | E.1 | `Provider string(20)` (registry key, e.g. `spotify`\|`youtube`\|…), `Status string(20)` (`connected`\|…), `BroadcasterId Guid?` — resolves the active provider (replaces the `_db.Services` lookup). |
| `IntegrationTokens` (read, via integrations vault) | E.2 | encrypted provider tokens; never read raw here — go through the integrations token service. |
| `Channels.OverlayToken` (read) | A.2 | `string(36) Unique` — the existing opaque browser-source token the now-playing widget connects with. The **public SR-page token** is a distinct token (see §3 `ISongRequestPageTokenService`). |

**Repository/DbSet naming (EF):** `DbSet<SongRequestQueue> SongRequestQueues`, `DbSet<SongRequestItem> SongRequestItems`, `DbSet<SongRequestTrustScore> SongRequestTrustScores`, `DbSet<MusicProviderConfig> MusicProviderConfigs` on `IApplicationDbContext`. The entity classes are singular (`SongRequestItem`), DbSets plural. Access in services via `IApplicationDbContext`; controllers go through services only.

---

## 2. Domain events

All inherit the canonical `DomainEventBase` (`NomNomzBot.Domain.Events`, platform-conventions §2.0), which supplies `Guid EventId`, `DateTimeOffset OccurredAt`, `Guid BroadcasterId` (events add only payload fields, never redeclaring the base). **Three events already exist and are EXTENDED in place** (do not create duplicates). All key/id fields are widened from raw strings to carry the persistent `SongRequestItemId Guid` so handlers can join to `SongRequestItems`.

| Event | Status | Fields (type) — all `required` unless `?` |
|---|---|---|
| **`SongRequestedEvent`** | EXTEND existing (`Domain/Events/SongRequestedEvent.cs`) | `SongRequestItemId Guid`; `QueueId Guid`; `Provider string`; `ProviderTrackId string`; `TrackUri string`; `TrackName string`; `Artist string?`; `int DurationSeconds`; `RequestedByUserId Guid`; `RequestedByDisplayName string`; `int Position`. (Existing fields `UserId`/`UserDisplayName`/`TrackUri`/`TrackName` are retained as `RequestedByUserId`/`RequestedByDisplayName`/`TrackUri`/`TrackName`; `UserId string` → `RequestedByUserId Guid`.) |
| **`SongSkippedEvent`** | EXTEND existing (`Domain/Events/SongSkippedEvent.cs`) | `SongRequestItemId Guid`; `QueueId Guid`; `SkippedByUserId Guid`; `TrackName string`. (`SkippedByUserId string` → `Guid`.) |
| **`TrackChangedEvent`** | EXTEND existing (`Domain/Events/TrackChangedEvent.cs`) | `SongRequestItemId Guid?` (null if provider-native track not from queue); `TrackName string`; `Artist string`; `TrackUri string`; `AlbumArtUrl string?`; `int DurationSeconds`; `Provider string`; `RequestedByDisplayName string?`. (Existing `DurationMs int` → `DurationSeconds int` for schema parity; keep `AlbumArtUrl`.) This is the event the now-playing feed (§ `INowPlayingFeed`) fans out to overlays. |
| **`SongRequestRejectedEvent`** | NEW (`Domain/Events/SongRequestRejectedEvent.cs`) | `QueueId Guid`; `RequestedByUserId Guid`; `RequestedByDisplayName string`; `Provider string`; `RawQuery string`; `RejectionReason string` (`queue_closed`\|`queue_full`\|`pending_limit`\|`per_stream_limit`\|`min_standing`\|`explicit_blocked`\|`age_restricted`\|`too_long`\|`subscriber_only`\|`trust_too_low`\|`requester_blocked`\|`not_embeddable`\|`no_provider`\|`not_found`\|`not_found_on_target`). |
| **`SongRequestQueueChangedEvent`** | NEW (`Domain/Events/SongRequestQueueChangedEvent.cs`) | `QueueId Guid`; `ChangeKind string` (`item_added`\|`item_removed`\|`item_played`\|`reordered`\|`bumped`\|`cleared`\|`opened`\|`closed`\|`paused`\|`resumed`); `SongRequestItemId Guid?`; `int QueueLength`. Drives live SR-page and dashboard queue refresh over the feed. `bumped` = an item moved into the bump band (raffle win / `!bump` / queue-jump redeem) — a queue-structure reorder. |
| **`SongRequestRaffleEvent`** | NEW (`Domain/Events/SongRequestRaffleEvent.cs`) | `QueueId Guid`; `RaffleId Guid`; `Phase string` (`started`\|`drawn`); `int EntryCost`; `int WinnerCount`; `IReadOnlyList<SongRequestRaffleWinner> Winners` (empty on `started`). `SongRequestRaffleWinner` = `record(Guid UserId, string DisplayName, Guid? BumpedItemId, bool GrantedBumpToken)` — `BumpedItemId` set when the winner had a queued song moved to the bump band, `GrantedBumpToken` true when they had none and got a token instead. Overlays celebrate the start + the draw; the feed mirrors it over SignalR (§3.6). |

---

## 3. Service interfaces (full signatures)

All methods async, return `Task<Result<T>>` (or `Task<Result>` for void outcomes). `Guid broadcasterId` is the tenant key. Namespaces: contracts under `NomNomzBot.Application.Contracts.Music`; config/page-token services under `NomNomzBot.Application.Services` (matching the existing `IMusicConfigService` placement).

### 3.1 `IMusicService` — EXTEND existing (`Application/Contracts/Music/IMusicService.cs`)

Widen `string broadcasterId` → `Guid broadcasterId` on every existing member, convert return types to `Result<T>`, and add the persistence-backed SR members. Final surface:

```csharp
namespace NomNomzBot.Application.Contracts.Music;

public interface IMusicService
{
    // ── Provider passthrough (existing, widened to Guid + Result) ──────────────
    Task<Result<IReadOnlyList<MusicTrack>>> SearchAsync(
        Guid broadcasterId, string query, int maxResults = 5, CancellationToken cancellationToken = default);
    // Searches the active provider. No state change. Returns [] on no match (success), failure only on provider error.

    Task<Result> PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Resumes provider playback. Side effect: provider Play call. No queue mutation. Emits no domain event.

    Task<Result> PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Pauses provider playback. Side effect: provider Pause call.

    Task<Result> SetVolumeAsync(Guid broadcasterId, int volume, CancellationToken cancellationToken = default);
    // Sets provider volume (0–100). Returns Failure("CAPABILITY_UNSUPPORTED") when the active provider's
    // Capabilities lack `Volume` (e.g. YouTube). No provider name checks — gated purely on the capability flag.

    // ── Provider remote / transport (NEW — capability-gated, no queue mutation) ──
    Task<Result> PreviousAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Provider previous-track. Gated on `Previous`; CAPABILITY_UNSUPPORTED if absent, PREMIUM_REQUIRED on non-Premium Spotify.

    Task<Result> SeekAsync(Guid broadcasterId, int positionSeconds, CancellationToken cancellationToken = default);
    // Seeks within the current track (finishes the half-spec'd `Seek` capability). Gated on `Seek`.
    // CAPABILITY_UNSUPPORTED if absent (e.g. YouTube), PREMIUM_REQUIRED on non-Premium Spotify, VALIDATION_FAILED if positionSeconds < 0.

    Task<Result> SetShuffleAsync(Guid broadcasterId, bool enabled, CancellationToken cancellationToken = default);
    // Toggles provider shuffle. Gated on `Shuffle`. CAPABILITY_UNSUPPORTED / PREMIUM_REQUIRED as above.

    Task<Result> SetRepeatAsync(Guid broadcasterId, MusicRepeatMode mode, CancellationToken cancellationToken = default);
    // Sets repeat mode (off|track|context). Gated on `Repeat`. CAPABILITY_UNSUPPORTED / PREMIUM_REQUIRED as above.

    Task<Result<IReadOnlyList<MusicDeviceDto>>> GetDevicesAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Lists the user's available playback devices. Gated on `TransferDevice`. Read-only. CAPABILITY_UNSUPPORTED if absent.

    Task<Result> TransferPlaybackAsync(
        Guid broadcasterId, string deviceId, bool startPlaying, CancellationToken cancellationToken = default);
    // Moves active playback to another of the user's devices. Gated on `TransferDevice`.
    // CAPABILITY_UNSUPPORTED / PREMIUM_REQUIRED as above, NOT_FOUND if deviceId is not one of the user's devices.

    Task<Result<NowPlayingDto>> GetNowPlayingAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Reads current track from active provider + the `playing` SongRequestItem (for RequestedBy). No state change.

    // ── Persistent song-request queue (NEW — schema L.4/L.5) ───────────────────
    Task<Result<MusicQueueDto>> GetQueueAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Reads NowPlaying + ordered queued SongRequestItems (Status=queued, ORDER BY Position). No state change.

    Task<Result<SongRequestItemDto>> RequestAsync(
        Guid broadcasterId, SongRequestInputDto input, CancellationToken cancellationToken = default);
    // Full SR pipeline: resolve requester→Users, select + cross-resolve provider (§3.1 provider model), search→resolve
    // track, recompute trust (§3.9 TrustScoreCalculator → 0–100 score + TrustTier), then run ALL gates (IsOpen,
    // MaxQueueLength, MinStandingToRequest, the per-standing PendingLimits cap (free lane) or PaidPendingLimit (paid
    // lane) + PerStreamLimit when on, AllowExplicit, MaxDurationFreeSeconds/MaxDurationPaidSeconds, the YouTube trust
    // gate (§3.9), SubscriberOnly, provider config BlockExplicit/MaxVideoDuration/BlockAgeRestricted/EmbeddableOnly).
    // PROVIDER MODEL: the queue is ONE interleaved fair queue across both providers (an item carries its own Provider).
    // Provider selection: an explicit `input.Provider` wins; a provider link/id routes to that provider; an AMBIGUOUS
    // request (bare search term, no link) routes to ProviderPriority's head among EnabledProviders. CROSS-RESOLVE: a
    // request whose link is from a provider OTHER than the resolution target (ProviderPriority head) and whose source
    // provider is not enabled is, when CrossResolveForeignLinks (L.4, default true), re-resolved by metadata
    // (title/artist) and searched on the target; no match ⇒ reject(not_found_on_target). A provider not in
    // EnabledProviders is never routed a request.
    // PENDING-LIMIT gate: PendingLimits maps the requester's effective standing (sub-tier aware) → max CONCURRENT
    // unplayed items they own in the fair queue; at/over cap ⇒ reject(pending_limit). null map value = unlimited
    // (Moderator/Broadcaster default). The paid lane (channel-point extra-slot/queue-jump) counts against PaidPendingLimit
    // instead, independent of the free cap. PerStreamLimit (when set) caps lifetime-in-stream items per user ⇒
    // reject(per_stream_limit). MinStandingToRequest below floor ⇒ reject(min_standing).
    // YouTube trust gate (BINDING): Spotify is never trust-gated (inherently safer source — it skips this check
    // entirely); a YouTube request auto-approves only when the requester's effective community standing is
    // `Vip`/`Moderator`-and-above (role bypass) OR the recomputed Score >= the broadcaster's configured
    // MinYouTubeTrustScore (L.4). Below that floor (and not bypassing by role) ⇒ reject(trust_too_low, "requires more
    // trust for YouTube — try Spotify"). MinYouTubeTrustScore null ⇒ no YouTube trust floor (auto-approve all). The
    // §3.9 TrustTier table is informative defaults only; the operative control is this configurable threshold + role bypass.
    // On accept: ENQUEUES the item into the channel's fair queue (§3.8, owner key = RequestedByUserId) and persists
    // SongRequestItem(Status=queued) with Position = fair-snapshot index under the per-tenant lock — **fair-rank order,
    // NOT FIFO tail** — increments SongRequestTrustScore.TotalRequests, hands the item to the sequencer (§3.5; pushed to
    // its provider only when it becomes the playable head), SaveChanges, emits SongRequestedEvent +
    // SongRequestQueueChangedEvent(item_added). On reject: emits SongRequestRejectedEvent, returns Failure with the
    // matching ErrorCode (VALIDATION_FAILED/RATE_LIMITED/FORBIDDEN/SERVICE_UNAVAILABLE).
    // Queue-jump / extra-slot priority placement is NOT applied here by default — both are opt-in channel-point redeem
    // paths (§3.8), off (QueueJumpEnabled/PaidExtraSlotEnabled false) unless the broadcaster enables them.
    // BAND ASSIGNMENT (§3.8 three-band model): the item is enqueued into PriorityBand=normal by default; when
    // AutoBumpFirstSong (L.4) is on AND this is the requester's FIRST request this stream (per-stream count 0 —
    // the same PerStreamLimit tracking, no new counter), it is enqueued into PriorityBand=auto_bump instead. After
    // enqueue (within the same UoW), calls ISongRequestBumpService.TryConsumeBumpTokenAsync (§3.11): a raffle-winner
    // bump token (L.9) promotes this request into PriorityBand=bump (BumpSource=raffle). Position is materialized
    // per-band under the per-tenant lock.

    Task<Result> SkipAsync(Guid broadcasterId, Guid skippedByUserId, CancellationToken cancellationToken = default);
    // Marks the `playing` item Status=skipped (+PlayedAt), asks the sequencer (§3.5) to advance to the next PLAYABLE
    // head, increments SongRequestTrustScore.SkippedCount, SaveChanges, emits SongSkippedEvent + TrackChangedEvent +
    // SongRequestQueueChangedEvent(item_played).

    Task<Result> RemoveAsync(Guid broadcasterId, Guid songRequestItemId, CancellationToken cancellationToken = default);
    // Soft-deletes a queued item (Status=skipped if it was queued), renumbers Position of trailing items under the
    // per-tenant lock, SaveChanges, emits SongRequestQueueChangedEvent(item_removed). Failure NOT_FOUND if absent.

    Task<Result<SongRequestItemDto>> MoveAsync(
        Guid broadcasterId, Guid songRequestItemId, int newPosition, CancellationToken cancellationToken = default);
    // Moves a queued item to newPosition within its band (clamped to band bounds), re-materializes Position under the
    // per-tenant lock, SaveChanges, emits SongRequestQueueChangedEvent(reordered). For mod/broadcaster drag-reorder —
    // a DURABLE manual override needing no pin flag: because the schedule is never globally re-sorted (§3.8), the moved
    // item stays where placed — later inserts splice around it and a head-pop never disturbs it. Failure NOT_FOUND if absent.

    Task<Result> ClearAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Soft-deletes all queued items (Status=skipped), SaveChanges, emits SongRequestQueueChangedEvent(cleared).

    Task<Result> AdvanceAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Track-finished transition (called by the sequencer §3.5 on track-end — Spotify poll / YouTube onStateChange):
    // marks current `playing` item Status=played (+PlayedAt), increments SongRequestTrustScore.PlayedCount, asks the
    // sequencer to promote the highest-ranked PLAYABLE item (playable-head rule) → playing and start it on its engine,
    // SaveChanges, emits TrackChangedEvent + SongRequestQueueChangedEvent(item_played). No-op success if empty; if the
    // only remaining items belong to a down provider the queue idles in `waiting`.
}
```

### 3.2 `ISongRequestQueueStateService` — NEW (`Application/Contracts/Music/ISongRequestQueueStateService.cs`)

Queue lifecycle/config that is **not** per-track. Separated from `IMusicService` (single responsibility: queue state vs. track operations).

```csharp
namespace NomNomzBot.Application.Contracts.Music;

public interface ISongRequestQueueStateService
{
    Task<Result<SongRequestQueueDto>> GetAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Reads (or lazily creates with defaults) the single SongRequestQueues row. No event on lazy-create.

    Task<Result<SongRequestQueueDto>> UpdateSettingsAsync(
        Guid broadcasterId, UpdateSongRequestQueueDto request, CancellationToken cancellationToken = default);
    // Patches IsOpen/IsPaused/MaxQueueLength/AllowExplicit/MinYouTubeTrustScore/SubscriberOnly/MinStandingToRequest/
    // EnabledProviders/ProviderPriority/CrossResolveForeignLinks/PendingLimits/PaidPendingLimit/PaidExtraSlotEnabled/
    // QueueJumpEnabled/PerStreamLimit/MaxDurationFreeSeconds/MaxDurationPaidSeconds/StripYouTubeAds. Validates
    // EnabledProviders/ProviderPriority ⊆ registered providers; PendingLimits keys ⊆ the standing/sub-tier key set.
    // SaveChanges; emits SongRequestQueueChangedEvent(opened|closed|paused|resumed) when those flags flip.

    Task<Result> SetLockedDeviceAsync(
        Guid broadcasterId, string? deviceId, string? deviceName, CancellationToken cancellationToken = default);
    // Persists SpotifyLockedDeviceId/Name (L.4) — the streamer's preferred Spotify device, remembered across sessions
    // for drip-feed playback (§3.5) and the connection nudge (§3.6). Null/null clears the lock. SaveChanges. No event.

    Task<Result> SetOpenAsync(Guid broadcasterId, bool isOpen, CancellationToken cancellationToken = default);
    // Toggle accepting requests. Emits SongRequestQueueChangedEvent(opened|closed).

    Task<Result> SetPausedAsync(Guid broadcasterId, bool isPaused, CancellationToken cancellationToken = default);
    // Toggle playback advance. Emits SongRequestQueueChangedEvent(paused|resumed).
}
```

### 3.3 `ISongRequestTrustService` — NEW (`Application/Contracts/Music/ISongRequestTrustService.cs`)

Owns L.6 read/compute/gate/block and is the home of **Bamo's trust scoring** (full algorithm + DTOs in §3.9). Distinct from the global moderation `UserTrustScores`; this is SR-specific request trust on a **0–100** scale.

```csharp
namespace NomNomzBot.Application.Contracts.Music;

public interface ISongRequestTrustService
{
    Task<Result<SongRequestTrustDto>> GetAsync(
        Guid broadcasterId, Guid requesterUserId, CancellationToken cancellationToken = default);
    // Reads (or returns default-zero) the requester's SongRequestTrustScores row. No state change.

    Task<Result<TrustEvaluationDto>> RecomputeAsync(
        Guid broadcasterId, Guid requesterUserId, TrustEvaluationInput input, CancellationToken cancellationToken = default);
    // Builds TrustContext (§3.9 input table) from the DB + the per-request `input` (provider, track + YouTube channel
    // metadata), runs TrustScoreCalculator.Calculate → 0–100, upserts SongRequestTrustScores.Score, returns the score +
    // resolved TrustTier (+ per-component breakdown). Called by IMusicService.RequestAsync before the gate.

    Task<Result<bool>> IsAllowedAsync(
        Guid broadcasterId, Guid requesterUserId, decimal? minYouTubeTrustScore, bool isYouTubeContent,
        bool bypassByRole, CancellationToken cancellationToken = default);
    // True iff !IsBlocked AND, for YouTube content only, (bypassByRole OR minYouTubeTrustScore is null OR
    // Score >= minYouTubeTrustScore). Spotify content is never trust-gated here (returns true unless IsBlocked).
    // `bypassByRole` = caller-resolved `Vip`/`Moderator`-and-above effective community standing (role bypass).
    // Pure read over the last computed Score — the configurable threshold + role bypass is the operative control,
    // not a fixed §3.9 tier band.

    Task<Result> SetBlockedAsync(
        Guid broadcasterId, Guid requesterUserId, bool isBlocked, CancellationToken cancellationToken = default);
    // Sets IsBlocked (creates row if absent). SaveChanges. No domain event (moderation-local).
}
```

### 3.4 `IMusicConfigService` — EXTEND existing (`Application/Services/IMusicConfigService.cs`)

Already returns `Result<T>`. Widen `string broadcasterId` → `Guid`, and **add** the generic per-provider config accessors backed by the single `MusicProviderConfig` table (today it stores config in a key-value store; the schema makes it relational). One pair of methods serves every provider — the `provider` registry key selects the row; provider-specific knobs ride in `ProviderSettings` and are validated by the registry.

```csharp
namespace NomNomzBot.Application.Services;

public interface IMusicConfigService
{
    Task<Result<MusicConfigDto>> GetConfigAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task<Result<MusicConfigDto>> UpdateConfigAsync(
        Guid broadcasterId, UpdateMusicConfigDto request, CancellationToken cancellationToken = default);

    Task<Result<MusicProviderConfigDto>> GetProviderConfigAsync(
        Guid broadcasterId, string provider, CancellationToken cancellationToken = default);
    // Reads the MusicProviderConfig row for (broadcasterId, provider). Failure NOT_FOUND if the provider
    // is unconnected (no IntegrationConnection). Failure NOT_FOUND if `provider` is not a registered key.

    Task<Result<MusicProviderConfigDto>> UpdateProviderConfigAsync(
        Guid broadcasterId, string provider, UpdateMusicProviderConfigDto request, CancellationToken cancellationToken = default);
    // Upserts the MusicProviderConfig row (1:1 on the connected provider IntegrationConnection). Validates
    // request.ProviderSettings via IMusicProviderRegistry.ValidateSettings(provider, …) before persist
    // (Failure VALIDATION_FAILED on bad knobs). Failure NOT_FOUND if unconnected/unregistered.
}
```
Adding a provider needs **no** new method here — `provider` is the open registry key; the registry validates its settings shape.

### 3.5 `IMusicProvider` — EXTEND existing (`Domain/Interfaces/IMusicProvider.cs`)

Widen `string broadcasterId` → `Guid` on every member; add a `Provider` discriminator, a `Capabilities` flagset (so the SR/now-playing logic gates on **what a provider can do**, not on its name), and a `ResolveTrackAsync` (the SR pipeline needs to turn a raw URI/id into authoritative metadata before persisting). Implementations: `SpotifyMusicProvider`, `YouTubeMusicProvider` (both exist — update signatures, do not rename).

```csharp
namespace NomNomzBot.Domain.Interfaces;

[Flags]
public enum MusicProviderCapabilities
{
    None               = 0,
    Search             = 1 << 0, // can resolve a query → tracks
    Queue              = 1 << 1, // has a provider-side queue to push to
    PlaybackControl    = 1 << 2, // Play/Pause
    Volume             = 1 << 3, // SetVolume
    Skip               = 1 << 4, // Skip current track
    Seek               = 1 << 5, // seek within current track
    NowPlaying         = 1 << 6, // can report the currently-playing track
    AcceptsSongRequests = 1 << 7, // may be routed viewer song requests by the SR pipeline
    Previous           = 1 << 8, // previous-track on the provider's player
    Shuffle            = 1 << 9, // toggle shuffle on the provider's player
    Repeat             = 1 << 10, // set repeat mode (off|track|context)
    TransferDevice     = 1 << 11, // move active playback to another of the user's devices
    Library            = 1 << 12, // save/remove saved tracks, follow/unfollow, ratings
    Playlists          = 1 << 13, // create/read/update playlists + add/remove tracks
    Subscriptions      = 1 << 14, // follow/unfollow channels (YouTube subscriptions)
}

public enum MusicRepeatMode { Off, Track, Context }   // Context = playlist/album (Spotify "context")

public sealed record MusicDeviceInfo(
    string Id, string Name, string Type, bool IsActive, int? VolumePercent);

public interface IMusicProvider
{
    string Provider { get; } // registry key ("spotify" | "youtube" | …) — selection key for ProviderPriority.

    MusicProviderCapabilities Capabilities { get; } // what this provider supports; gates routing instead of name checks.

    Task PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task SkipAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task PreviousAsync(Guid broadcasterId, CancellationToken cancellationToken = default);                          // requires Previous
    Task SetVolumeAsync(Guid broadcasterId, int volumePercent, CancellationToken cancellationToken = default);      // requires Volume (0–100)
    Task SeekAsync(Guid broadcasterId, int positionSeconds, CancellationToken cancellationToken = default);        // requires Seek
    Task SetShuffleAsync(Guid broadcasterId, bool enabled, CancellationToken cancellationToken = default);         // requires Shuffle
    Task SetRepeatAsync(Guid broadcasterId, MusicRepeatMode mode, CancellationToken cancellationToken = default);  // requires Repeat
    Task<IReadOnlyList<MusicDeviceInfo>> GetDevicesAsync(Guid broadcasterId, CancellationToken cancellationToken = default);        // requires TransferDevice
    Task TransferPlaybackAsync(Guid broadcasterId, string deviceId, bool play, CancellationToken cancellationToken = default);      // requires TransferDevice
    Task<TrackInfo?> GetCurrentTrackAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackInfo>> SearchAsync(
        Guid broadcasterId, string query, int maxResults = 5, CancellationToken cancellationToken = default);
    Task<TrackInfo?> ResolveTrackAsync(Guid broadcasterId, string uriOrId, CancellationToken cancellationToken = default);
    // Authoritative single-track metadata lookup (provider track id/uri → TrackInfo). Null if not found/unavailable.
    Task<bool> AddToQueueAsync(Guid broadcasterId, string trackUri, CancellationToken cancellationToken = default);
}
```
`TrackInfo` (existing class) gains `string ProviderTrackId { get; init; }` and `bool IsExplicit { get; init; }` and `bool IsAgeRestricted { get; init; }` and `bool IsEmbeddable { get; init; }` (needed by the gates); `DurationMs` stays (service converts to `DurationSeconds` for persistence).

**Capability set per shipped provider.** `SpotifyMusicProvider` = `Search | Queue | PlaybackControl | Volume | Skip | Seek | NowPlaying | AcceptsSongRequests | Previous | Shuffle | Repeat | TransferDevice | Library | Playlists` (full remote + library/playlist manage; **no `Subscriptions`** — Spotify has no channel-follow analogue here). `YouTubeMusicProvider` = `Search | Queue | NowPlaying | AcceptsSongRequests | Library | Playlists | Subscriptions` (search-fed queue + manage surface) **minus `Volume`/`Seek`/`Previous`/`Shuffle`/`Repeat`/`TransferDevice`** — YouTube Data API has **no playback-transport control** (those ride the embedded player, not the API), so the remote-transport calls below return `CAPABILITY_UNSUPPORTED` on YouTube. `Library` on YouTube = `videos.rate` (like/dislike); `Subscriptions` = `subscriptions.insert`/`delete`. The rest of the subsystem gates purely on the capability flag — **no name checks anywhere**.

**YouTube ad-strip.** Because YouTube plays through our **browser-source player** (§3.5.2), the overlay blocks the YouTube **ad-network requests** (YouTube ads arrive as separate network requests — strippable here, unlike Spotify, which streams ads INLINE in the audio and cannot be stripped). Config `SongRequestQueues.StripYouTubeAds` (L.4, default **true**, per-channel toggle) controls it; rationale + ToS stance in §9.

**Capability-routing rules (stated once, applied everywhere):**
- The **SR pipeline** (`IMusicService.RequestAsync` provider selection) only routes a request to a provider that is in `SongRequestQueues.EnabledProviders` AND whose `Capabilities` include **`AcceptsSongRequests`** (and `Search`/`Queue` to resolve+enqueue). With both providers enabled the queue interleaves them (§3.5.2). `ProviderPriority` is the preferred provider for an **AMBIGUOUS** request (bare search, no link) and the **cross-resolve target** for a foreign-provider link (`CrossResolveForeignLinks`, §3.1) — it is **not** a "first-capable" fallback walk. A provider lacking `AcceptsSongRequests` (or absent from `EnabledProviders`) is never routed a request.
- The **now-playing feed** (`INowPlayingFeed` / `TrackChangedEvent`) accepts **any** provider whose `Capabilities` include **`NowPlaying`** — so a now-playing-only provider feeds `!song`/overlays without ever touching queue logic.
- Per-operation gates check the relevant flag and return `Failure("CAPABILITY_UNSUPPORTED")` if absent (e.g. `SetVolumeAsync` requires `Volume`, `SeekAsync` requires `Seek`, `PreviousAsync` requires `Previous`, `SetShuffleAsync` requires `Shuffle`, `SetRepeatAsync` requires `Repeat`, `TransferPlaybackAsync`/`GetDevicesAsync` require `TransferDevice`, the `IMusicProviderManageApi` library calls require `Library`, playlist calls require `Playlists`, subscription calls require `Subscriptions`).
- **Spotify playback requires Premium.** Transport/remote control on Spotify additionally needs a **Premium** account (Web API restriction). This is surfaced as a runtime **capability**, not a connect error: a remote call on a non-Premium Spotify account returns `Failure("PREMIUM_REQUIRED")` (distinct from `CAPABILITY_UNSUPPORTED`), and `IntegrationStatusDto.Capabilities["spotify.premium"]` (integrations-oauth §3) is `false` so the dashboard can disable the controls rather than letting them fail. Connect still succeeds without Premium (search/library/playlist read-write all work; only transport is gated).

### 3.5.1 `IMusicProviderRegistry` — NEW (`Application/Contracts/Music/IMusicProviderRegistry.cs`)

Single resolution point for providers by their open registry key. Providers are DI-keyed by `Provider`; each self-registers its `Capabilities` and a settings-schema/validator (which validates the `MusicProviderConfig.ProviderSettings` JSON for that provider). Adding a provider = register the `IMusicProvider` + its settings schema here; **zero** new tables, service methods, or migrations.

```csharp
namespace NomNomzBot.Application.Contracts.Music;

public interface IMusicProviderRegistry
{
    IReadOnlyList<string> Providers { get; }
    // All registered provider keys (drives ProviderPriority validation + the SR-page provider list).

    Result<IMusicProvider> Resolve(string provider);
    // The provider for a registry key. Failure NOT_FOUND if unregistered.

    bool Supports(string provider, MusicProviderCapabilities capability);
    // Convenience: does the registered provider declare this capability? (false if unregistered).

    Result ValidateSettings(string provider, string providerSettingsJson);
    // Runs the provider's registered settings-schema validator over a MusicProviderConfig.ProviderSettings blob.
    // Failure VALIDATION_FAILED with the offending field(s); used by UpdateProviderConfigAsync before persist.
}
```

**Provider KINDS the capability model accommodates** (so the extensibility is concrete, not nominal — these are *kinds* the registration seam admits, not providers this subsystem ships):
- **interactive** — `Search | Queue | PlaybackControl | AcceptsSongRequests` (+ optionally `Volume`/`Skip`/`Seek`/`NowPlaying`). The shipped Spotify/YouTube providers. Drive the live SR queue.
- **now-playing / metadata** — `NowPlaying` only. Feeds overlays and `!song`; **never** routed an SR request (no `AcceptsSongRequests`). A DMCA-safe stream-music source (e.g. Pretzel) registers as this kind: it reports the current track but isn't in the request queue.
- **library / source** — a fixed catalog of tracks (`Search` + a queue, royalty-free). A royalty-free library (e.g. StreamBeats) registers here, exposing `Search | Queue | AcceptsSongRequests` over its own catalog.

These names (Pretzel, StreamBeats) are illustrative of the kinds the model fits — they are **not** specified as providers; the point is that each slots in by registering capabilities + a settings schema, with no queue-logic or schema change.

### 3.5.2 Playback sequencing — `ISongRequestSequencer` (NEW, `Application/Contracts/Music/ISongRequestSequencer.cs`)

The fair queue (§3.8) is the **ordering authority**; the **sequencer** is the **playback driver**. It owns ONE now-playing pointer and plays strictly **one item at a time** across both providers — the queue is interleaved (item N may be Spotify, N+1 YouTube), so the sequencer drives whichever engine the playable head belongs to. Impl `SongRequestSequencer` (Infrastructure), run by the existing now-playing poller / `IHostedService` loop.

```csharp
namespace NomNomzBot.Application.Contracts.Music;

public interface ISongRequestSequencer
{
    Task<Result> StartAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Begins driving the channel's queue: resolves the playable head (§playable-head), starts it on its engine
    // (Spotify drip-feed or YouTube browser-source), records it as the `playing` item. No-op if already driving.

    Task<Result> AdvanceAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Track-end transition: stops + VERIFIES the outgoing engine is stopped, then starts the next playable head on its
    // engine (engine may switch Spotify↔YouTube). Backs IMusicService.AdvanceAsync/SkipAsync. Empty/all-down ⇒ waiting.

    Task<Result<PlaybackStateDto>> GetStateAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Current driver state: PlaybackPhase (playing|waiting|paused|idle), the playing item, the active engine,
    // last-known locked-device name, and whether a Spotify device is currently reachable.

    Task<Result> PollAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // One tick of the watchdog (called by the host loop): for a Spotify head, reads /me/player for track-end (→Advance)
    // and runs the autoplay watchdog (§mutual-exclusion rule 3); detects device loss (→waiting) and recovery; for a
    // YouTube head, track-end arrives via the overlay IFrame onStateChange(ENDED) callback, not this poll.
}

public enum PlaybackPhase { Idle, Playing, Paused, Waiting }

public sealed record PlaybackStateDto(
    PlaybackPhase Phase, Guid? PlayingItemId, string? ActiveProvider,
    string? LockedDeviceName, bool SpotifyDeviceReachable);
```

**Spotify = drip-feed remote control (NOT a queue dump).** We hold the authoritative fair queue and **push only the head** to the streamer's active/locked Spotify device (play-track on `/me/player/play`), poll `/me/player` for track-end, then push the next head. We **MUST NOT** dump the queue into Spotify's native queue: the Spotify **"Add to Queue" endpoint is APPEND-ONLY** — no reorder, no remove — so dumping would destroy fair ordering, trust gating, and queue-jump. Drip-feeding keeps our queue the single source of truth. **Spotify Premium is REQUIRED** for every playback write — surfaced as the `PREMIUM_REQUIRED` capability error (§3.5) plus an onboarding note; without Premium the queue cannot drive Spotify.

**YouTube = browser-source player (StreamElements parity).** Our overlay widget (the now-playing/SR widget over `OverlayHub`) plays YouTube items via the IFrame Player API; track-end fires through `onStateChange(ENDED)` (relayed back over the hub → `ISongRequestSequencer.AdvanceAsync`); OBS captures the widget audio. **No device dependency** — a YouTube head never enters `waiting` for a device.

**Mutual exclusion / no Spotify-autoplay bleed.** Spotify's account-level **"Autoplay"** (recommendations after the queue empties) **cannot be disabled via API**, so silence is enforced ACTIVELY by four rules:
1. **Stop-and-verify on every advance.** Pause/stop the OUTGOING engine and verify it stopped (`is_playing=false` for Spotify) BEFORE starting the incoming engine.
2. **Conditional pre-load.** Only pre-load Spotify's native "next" when the upcoming item is ALSO Spotify; if the next item is YouTube, leave Spotify's native queue EMPTY.
3. **Autoplay watchdog.** If Spotify is ever observed playing a track that is NOT our expected current item (autoplay slipped through the ~1–2s end-of-track gap), pause it immediately.
4. **Empty-queue pause.** Empty queue ⇒ pause Spotify (never let it autoplay recommendations).

**Playable-head rule (a down provider never blocks the queue).** The sequencer always plays the **highest-fair-ranked item that is PLAYABLE RIGHT NOW**. Items whose provider is in `waiting`/`retrying` are **SKIPPED but RETAIN their fair position** (not removed, not penalized, no rank loss). When the provider recovers they play next at their rank — **after the current song finishes** (never interrupt). If ALL remaining items belong to the down provider, the queue idles in `waiting`.

**Spotify device handling.** The streamer selects a preferred Spotify device; we persist its id + name (`SpotifyLockedDeviceId`/`SpotifyLockedDeviceName`, L.4) **across sessions** — a fully-closed Spotify reports NO devices, so the remembered name backs the nudge. On each advance/poll: when the active device differs but the locked one is reachable (`GetDevicesAsync`), auto-`TransferPlaybackAsync` to it; when no device is reachable, enter `waiting`. `GetDevicesAsync`/`TransferPlaybackAsync` stay capability-gated (`TransferDevice`) + Premium-gated.

**Connection-state detection + nudge.** On `stream.online` and continuously (the poll tick): if a Spotify item needs playback and no reachable device exists ⇒ state `waiting`. A **chat nudge** (template variable, streamer-rewordable) is posted: default `@{broadcaster} — open Spotify and set {deviceName} as your active device to start the queue.` where `{deviceName}` = `SpotifyLockedDeviceName`, with a generic fallback (`open Spotify on any device`) when none is known. **Throttle:** post ONCE on entering `waiting` (or at stream start), then re-nudge at most every **5 minutes** while still waiting. The same state is ALWAYS mirrored as a dashboard banner via `INowPlayingFeed`/SignalR (§3.6).

**Failure taxonomy (drives `SongRequestItems.Status`, L.5).** Three outcomes, sharply separated:
- **`waiting`** — provider-level / environmental (Spotify not open, no reachable device, provider OUTAGE). **INDEFINITE**, skipped by the playable-head rule, **NEVER auto-removed**, does **NOT** consume retries. A provider outage holds every one of its items in `waiting` until it recovers.
- **`retrying`** — a per-item TRANSIENT error on a **HEALTHY** provider (HTTP 5xx, 429, network blip, our own exception, device dropped mid-push). Exponential backoff via `RetryCount`/`NextRetryAt`, capped at ~3 attempts over a few minutes; on exhaustion ⇒ remove + notify requester (`FailureReason`).
- **removed (immediate)** — a permanent/content error: track/video not found, removed/private/deleted, region-locked, not embeddable (`IsEmbeddable=false`), age-restricted or explicit when the channel blocks it, over max duration. Soft-remove + notify requester with the reason. (No retry — the content will not become valid.)

Explicitly: a provider **OUTAGE** holds items in `waiting` indefinitely; only **per-item** errors on a **healthy** provider use the bounded `retrying` path; content errors are removed immediately.

### 3.6 `INowPlayingFeed` — NEW (`Application/Contracts/Music/INowPlayingFeed.cs`)

The now-playing **widget feed** boundary. Application-layer abstraction; the Infrastructure/Api impl wraps `IHubContext<OverlayHub, IOverlayClient>` and pushes a `WidgetEventDto` to the channel's overlay group. Keeps `IMusicService` free of SignalR.

```csharp
namespace NomNomzBot.Application.Contracts.Music;

public interface INowPlayingFeed
{
    Task PublishNowPlayingAsync(Guid broadcasterId, NowPlayingDto nowPlaying, CancellationToken cancellationToken = default);
    // Fans the current track to all overlay connections for the channel (now-playing widget). Fire-and-forget semantics.

    Task PublishQueueChangedAsync(
        Guid broadcasterId, SongRequestQueueChangedPayload payload, CancellationToken cancellationToken = default);
    // Fans a queue delta to overlay + SR-page subscribers so live views refresh without polling.
}

public sealed record SongRequestQueueChangedPayload(string ChangeKind, Guid? SongRequestItemId, int QueueLength);
```
Impl (`NowPlayingFeed`, Infrastructure or Api) is subscribed to `TrackChangedEvent` + `SongRequestQueueChangedEvent` via the `IEventBus` handler registration and calls `_overlayHub.Clients.Group($"widget-{broadcasterId}-now-playing").WidgetEvent(new WidgetEventDto("now-playing", "track.changed", nowPlaying))` (group naming matches `OverlayHub.JoinWidget`: `widget-{broadcasterId}-{widgetId}`).

### 3.7 `ISongRequestPageTokenService` — NEW (`Application/Services/ISongRequestPageTokenService.cs`)

**Public SR-page tokens.** The public `/(public)/sr/[channel]` page must let viewers submit requests without a JWT. This is a per-channel opaque capability token, **distinct** from `Channels.OverlayToken` (which is for OBS browser sources). It resolves a token → `BroadcasterId` for the public submit endpoint.

```csharp
namespace NomNomzBot.Application.Services;

public interface ISongRequestPageTokenService
{
    Task<Result<SongRequestPageDto>> ResolveAsync(string pageToken, CancellationToken cancellationToken = default);
    // Maps an opaque SR-page token → channel context (BroadcasterId, channel name, queue open/paused, EnabledProviders list).
    // Failure NOT_FOUND on unknown/disabled token. Used by the public (AllowAnonymous) submit/read endpoints.

    Task<Result<string>> GetOrCreateAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Returns the channel's SR-page token, minting one (opaque, 32+ chars, not PII) on first call. Idempotent.

    Task<Result<string>> RotateAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Invalidates the old token and returns a fresh one (revokes public access via the old link).
}
```
> **Storage.** The SR-page token is the LOCKED-schema `Channels.SongRequestPageToken string(64) Null Unique` column (A.2) — a single per-channel opaque, rotatable string mirroring the existing `OverlayToken` pattern. `GetOrCreateAsync` mints it on first call; `RotateAsync` overwrites it. **No PII** (per A.2 `OverlayToken` precedent).

---

### 3.8 Fair-queue ordering — Bamo's algorithm (`IFairQueue<T>`)

Queue order is **not FIFO** — it is **Bamo's rank-based fair scheduler** (`NomNomzBot.Domain.Music.Interfaces.IFairQueue<T>`, impl `NomNomzBot.Infrastructure.Music.FairQueue<T>` — **already in the tree**; reused with the insertion-direction fix in *Code reconciliation* below). One in-memory `IFairQueue<SongRequestItem>` per active channel is the **ordering authority**; `SongRequestItems.Position` (L.5) is its **persisted materialization**.

**Algorithm (faithful — round-robin fair queueing, the Deficit-Round-Robin family).** A song's **rank is positional: the count of that requester's songs at-or-ahead of it in the CURRENT queue** (`rank_of(i) = count of queue[i].owner in queue[0..i]`). Two rules fully determine order: (1) **no song sits behind a higher-rank song**; (2) **within equal rank, earliest-requested stays ahead** (FIFO). Net effect: if N requesters each have one song queued, **all N play before anyone's 2nd** — no one front-loads the queue (which plain FIFO + a flat per-user cap does not prevent). The per-standing `PendingLimits` cap (§3.1) bounds concurrent items; rank orders what's queued. (Reference: the interactive write-up at `https://fair-queueing.netlify.app/`.)

**Rank is queue-STATE, not lifetime history (binding).** Rank counts only songs CURRENTLY queued — already-played songs are forgotten, so after a requester's song plays their next song competes as a fresh low rank. This is intended: a returning requester is judged on their live backlog, not their lifetime play count. The order is **maintained incrementally, never globally re-sorted**:
- **Insert** — compute the new song's rank (`= owner's current queued count + 1`), scan the queue **front-to-back**, insert it **immediately before the first song of higher rank** (the end of its rank tier); append if none higher exists. (Front-to-back is load-bearing — see *Code reconciliation*.)
- **Dequeue / play** — remove the head (index 0) **only**: do NOT re-sort and do NOT re-promote the dequeued owner's remaining songs. Their physical order already encodes the fair schedule and the head-pop preserves it.
- **Remove (self / mod) / Clear** — remove the element(s) only; the surviving order stays a valid schedule.
- **Per band** — run this independently within each band (`bump` → `auto_bump` → `normal`, §three-band) and concatenate in band-stack order.

> **Do NOT "fix" this by globally re-sorting on `(rank, arrival)`.** Re-sorting after a dequeue re-promotes the dequeued requester's next song to the front of its rank tier by arrival time, letting it leapfrog requesters who have not played yet — the exact unfairness the scheduler exists to prevent (e.g. `O1,P1,P2,O2`, play `O1`, then a re-sort yields `P1,O2,P2` instead of the correct `P1,P2,O2`). Order is defined by incremental insertion + head-pop, not a global sort.

```csharp
public interface IFairQueue<T>   // NomNomzBot.Domain.Music.Interfaces (exists)
{
    void Enqueue(string ownerKey, T item);   // rank = owner's queued count + 1; insert before the first HIGHER-rank song (front-to-back scan)
    T?   Dequeue();                           // remove the head ONLY — no re-sort, no re-promote (head-pop preserves the fair order)
    T?   Peek();                              // current head
    int  Count { get; }
    bool IsEmpty { get; }
    void Clear();
    int  RemoveByOwner(string ownerKey);      // remove the owner's items; surviving order stays valid
    bool RemoveAt(int position);              // remove one item by position; surviving order stays valid
    IReadOnlyList<(T Item, int Rank, string OwnerKey)> GetSnapshot();   // items in play order; Rank = live positional rank
}
```

**Persistence mapping.** `IMusicService.RequestAsync` calls `queue.Enqueue(RequestedByUserId, item)`, then writes `SongRequestItems.Position = snapshot index` for every `queued` row from `queue.GetSnapshot()` under the per-tenant lock. `AdvanceAsync` (track finished) `Dequeue()`s the head and re-materializes Position; `RemoveAsync` → `RemoveByOwner`/`RemoveAt` + re-materialize; `ClearAsync` → `Clear`. `GetQueueAsync` (`ORDER BY Position`) and the SR-page render that persisted order.

**Restart-safe.** The in-memory queue is rebuilt on first access from the persisted `Status=queued` rows **in `Position` order — the saved schedule, NOT `RequestedAt`**. Rebuilding from arrival order would re-enqueue songs whose earlier siblings have already played and so re-promote them (the re-sort trap above); loading by `Position` restores the exact pre-restart order, and positional ranks are then recomputed live from the rebuilt list for the next insert.

**Paid lane — queue-jump + extra-slot (opt-in, OFF by default).** The paid lane is separate from the free lane and **default-deny** — the fair queue admits no paid lane unless the broadcaster turns one on, and paid requests count against `PaidPendingLimit` (L.4), not the free `PendingLimits`. Two opt-in **channel-point redeems**, each off by default:
- **`queue-jump`** (`QueueJumpEnabled`) — priority placement: a "queue-jump raffle" redeem (redeemers enter a draw for the next priority slot) and a "one-time bump my song" redeem (moves the redeemer's already-queued item ahead of the fair-ordered remainder, once). Placed **ahead of the fair-ordered items** (priority prefix); fairness is preserved among the non-jumped remainder.
- **`extra-slot`** (`PaidExtraSlotEnabled`) — adds a request at **normal fair position**, bypassing the free `PendingLimits` cap (it buys an additional slot, not priority).

Both link the redemption via `SongRequestItems.CatalogPurchaseId` (`economy.md`). With neither enabled, every request takes fair-rank order under the free cap. Mod `MoveAsync` is a deliberate manual override: it moves the item to the chosen index. Because the schedule is **never globally re-sorted**, the moved item simply stays where it was placed — later inserts splice around it and a head-pop never disturbs it — so the override is durable with no pin flag or extra column.

**Three-band priority model (the bump tier).** The single ordered queue (top plays first) is partitioned into three bands by `SongRequestItems.PriorityBand` (L.5). The sequencer (§3.5.2) always promotes the highest item across the whole queue; the bands stack **bump → auto_bump → normal**, and `Position` orders items WITHIN a band (the fair-rank materialization from §3.8 is computed per-band, not globally). The restart-safe rebuild (above) runs per-band: on first access each band's queue is rebuilt from its `Status=queued` rows **in `Position` order**, partitioned by `PriorityBand`, so a restart never reshuffles.

1. **`bump` band — every explicit bump.** Raffle wins (§3.11), `!bump` (§3.11/§6), and the existing channel-point `queue-jump` redeem (§3.8) ALL land here. Ordered **fairly among bumpers** by the same Bamo rule: a bumper's rank within the band = (number of bumps that user already has pending in the band) + 1, FIFO within equal rank. No single bumper monopolizes the band even with multiple bumps. `BumpSource` records which path placed the item (`raffle`\|`command`\|`redeem`).
2. **`auto_bump` band — each requester's first song of the stream.** When `AutoBumpFirstSong` (L.4, default OFF) is on, a requester's FIRST song this stream is placed here: **above** the regular fair queue, **below** every explicit bump. Fair among the auto-bumped first-songs (same Bamo rule, owner key = `RequestedByUserId`). A user's SUBSEQUENT songs go to the `normal` band. **First-song detection REUSES the per-stream-per-user request tracking the `PerStreamLimit` feature relies on** (§3.1) — first request this stream = that per-stream count is 0; **no second counter is introduced**.
3. **`normal` band — the regular fair queue.** Unchanged Bamo fair order (§3.8 above).

A `!bump`/raffle/redeem on an item already queued sets its `PriorityBand=bump` and re-materializes Position within the bump band under the per-tenant lock, then emits `SongRequestQueueChangedEvent(bumped)`. The `queue-jump` redeem's "priority prefix" (above) is exactly the `bump` band — the two descriptions are the same mechanism.

**Code reconciliation.** One impl is in the tree — `NomNomzBot.Infrastructure.Music.FairQueue<T>` (`server/src/NomNomzBot.Infrastructure/Music/FairQueue.cs`) behind `NomNomzBot.Domain.Music.Interfaces.IFairQueue<T>` — and it is **canonical** (no orphan dupe remains). Its `Dequeue` and play order are **correct** (head-pop preserves the schedule; the in-place rank renumber keeps stored rank equal to live positional rank). The bug is in **`Enqueue`: it scans from the END** for the last item with `rank <= newRank` and inserts after it. That equals the front-scan only while the list is rank-monotonic — but a `Dequeue` legitimately leaves it non-monotonic (the dequeued owner's later song is renumbered to a low rank yet stays physically behind higher-rank items), and scanning from the end then **buries a fresh rank-1 request at the back** instead of placing it in the front rank-1 tier. Worked example: `O1,P1,P2,O2` → `Dequeue O1` → `[P1,P2,O2]` (correct), then enqueue newcomer `Q1` → the impl yields `[P1,P2,O2,Q1]` (Q1 buried); correct is `[P1,Q1,P2,O2]`. **Fix on implementation:** scan **front-to-back** and insert before the first higher-rank item (the `insert`/`rank_of` at `https://fair-queueing.netlify.app/`); keep head-pop as-is and do **not** re-sort. **Regression test (prove behavior):** assert the exact order `[P1,Q1,P2,O2]` for insert-after-dequeue — the existing `FairQueueTests` use order-insensitive `BeEquivalentTo` and only a single-owner dequeue, so they miss it; add explicit-order cases for insert-after-dequeue, mid-queue self-remove, and a 3-owner interleave.

### 3.9 Trust scoring — Bamo's exponential-decay algorithm (`TrustScoreCalculator`)

The `MinYouTubeTrustScore` gate (L.4) and `SongRequestTrustScores.Score` (L.6) are produced by **Bamo's exponential-decay trust algorithm** (`NomNomzBot.Infrastructure.Services.Trust.TrustScoreCalculator` — **already in the tree**). Score is **0–100** per (channel, requester). The **core metric base** — the four metric scores (`requestScore`/`accountScore`/`contentScore`/`popularityScore`), their weights (0.25/0.25/0.30/0.20) and decay constants (0.599/0.499/0.999/0.0003) — is **Bamo's fixed foundation and is NOT configurable**. Each **buff and debuff** layered on top of that base (reputation boost, follow penalty, YouTube channel-quality penalties, skip/timeout/ban penalties) is **individually toggleable + tunable by advanced users** via `TrustScoringConfig` (per-channel, L.4 JSON; rides the `config` PUT under `music:config:write`, §5). Every toggle defaults **ON** and every magnitude defaults to **the constant shown below**, so the **default configuration reproduces today's behavior exactly** — nothing changes unless an advanced user deliberately tunes it. The advanced dashboard surfaces these toggles + magnitudes behind an **"Advanced"** panel. This score is the mechanism that **gates low-quality YouTube requests**: a YouTube request auto-approves only when the requester bypasses by role (`Vip`/`Moderator`-and-above) or scores at/above the broadcaster's configurable `MinYouTubeTrustScore`. Spotify is never trust-gated.

**Inputs — `TrustContext`** (all derived; no extra storage):

| Field | Source |
|---|---|
| `SuccessfulRequestCount` | `SongRequestTrustScores.PlayedCount` (L.6) |
| `AccountAgeMonths` | `Users.CreatedAt` / Twitch account age |
| `ContentAgeMonths` | track/video release date (provider metadata) |
| `ContentViewCount` | track/video play count (provider metadata) |
| `IsFollowing` / `FollowAgeDays` | Twitch follow (Helix `moderator:read:followers`) |
| `IsModerator` / `IsVip` / `IsSubscriber` | `ChannelMemberships` / `ChannelCommunityStandings` |
| `IsYouTubeContent` | `Provider == youtube` |
| `ContentChannel{VideoCount,TotalViews,Subscribers,AgeMonths}` | YouTube Data API `channels.list` (YouTube content only) |
| `SkippedByModCount` | `SongRequestTrustScores.SkippedCount` (L.6) |
| `TimeoutCount` / `BanCount` | `UserModerationHistory` (moderation J.4) |

**Algorithm (faithful — metric base FIXED; each buff/debuff gated on its `cfg` toggle, defaults reproduce today exactly).**
`cfg` = the channel's `TrustScoringConfig` (L.4). The metric base block below is Bamo's fixed foundation — **never configurable**. Each buff/debuff line is **only computed when its toggle is true** (a disabled modifier's line is removed entirely from the computation, not zeroed). Defaults: all toggles true, all magnitudes = the constants shown.

```
// ── METRIC BASE — FIXED (not configurable) ────────────────────────────────────
metric(value, decay) = 100 · (1 − e^(−decay · value))

requestScore     = metric(SuccessfulRequestCount, 0.599)    // ~5 requests → ~95
accountScore     = metric(AccountAgeMonths,        0.499)    // ~6 months   → ~95
contentScore     = metric(ContentAgeMonths,        0.999)    // ~3 months   → ~95
popularityScore  = metric(ContentViewCount,        0.0003)   // ~10K views  → ~95

score = 0.25·requestScore + 0.25·accountScore + 0.30·contentScore + 0.20·popularityScore

// ── BUFFS / DEBUFFS — each gated on its toggle, magnitude from cfg (defaults = constants) ──
if (cfg.FollowPenaltyEnabled && (!IsFollowing || FollowAgeDays < cfg.FollowPenaltyMinDays))   // default 1
                                                             score ×= cfg.FollowPenaltyMultiplier      // default 0.75
if (cfg.ReputationBoostEnabled &&
    (IsModerator || IsVip || IsSubscriber || SuccessfulRequestCount ≥ cfg.ReputationBoostMinRequests)) // default 10
                                                             score += (100 − score)/2                  // reputation boost (gap-halving)
if (cfg.YouTubeQualityPenaltyEnabled && IsYouTubeContent) {                                            // YouTube channel-quality penalties (group toggle)
    if (ChannelVideoCount < cfg.MinChannelVideoCount || ChannelTotalViews < cfg.MinChannelTotalViews)  // defaults 5 / 5000
                                                             score ×= cfg.YouTubeQualityMultiplier     // default 0.75
    if (ChannelSubscribers < cfg.MinChannelSubscribers)      score ×= cfg.YouTubeQualityMultiplier     // default 25 / 0.75
    if (ChannelAgeMonths   < cfg.MinChannelAgeMonths)        score ×= cfg.YouTubeQualityMultiplier     // default 1  / 0.75
}
if (cfg.SkipPenaltyEnabled)    score −= cfg.SkipPenalty    · SkippedByModCount                         // default 5
if (cfg.TimeoutPenaltyEnabled) score −= cfg.TimeoutPenalty · TimeoutCount                              // default 10
if (cfg.BanPenaltyEnabled)     score −= cfg.BanPenalty     · BanCount                                  // default 30
score  = clamp(score, 0, 100)
```

**`TrustScoringConfig` (advanced — per-channel, L.4 JSON).** The metric base above is fixed; the buffs/debuffs are advanced-configurable. The config carries one toggle per modifier (default true) and a tunable magnitude per modifier (default = the constant above). With every value at default, the algorithm is byte-for-byte today's behavior.

| Modifier | Toggle (default) | Tunable value(s) (default) |
|---|---|---|
| Reputation boost (buff, gap-halving) | `ReputationBoostEnabled` (true) | `ReputationBoostMinRequests` (10) |
| Follow penalty (debuff) | `FollowPenaltyEnabled` (true) | `FollowPenaltyMultiplier` (0.75), `FollowPenaltyMinDays` (1) |
| YouTube channel-quality penalties (debuff, group) | `YouTubeQualityPenaltyEnabled` (true) | `YouTubeQualityMultiplier` (0.75); thresholds `MinChannelVideoCount` (5), `MinChannelTotalViews` (5000), `MinChannelSubscribers` (25), `MinChannelAgeMonths` (1) |
| Skip penalty (debuff) | `SkipPenaltyEnabled` (true) | `SkipPenalty` (5) |
| Timeout penalty (debuff) | `TimeoutPenaltyEnabled` (true) | `TimeoutPenalty` (10) |
| Ban penalty (debuff) | `BanPenaltyEnabled` (true) | `BanPenalty` (30) |

**Validation / safety.** Penalties and thresholds validate **≥ 0**; multipliers validate in **(0, 1]**; the final `score` is still `clamp(0, 100)`. A disabled modifier removes its line **entirely** from the computation (it is not the same as zeroing a weight). The metric base (weights/decays) has **no** config surface. The `MinYouTubeTrustScore` gate (L.4) is unchanged and stays on the mod-level `queue/settings` route; the `TrustScoringConfig` is sensitive and rides the `config` PUT (`music:config:write`, Editor/Broadcaster, §5).

**Tiers (`TrustTier`) — informative defaults, NOT the operative gate.** The tier bands below are descriptive labels surfaced to broadcasters/mods (dashboard, `!trust`) and seed the *default* value of `MinYouTubeTrustScore`. They do **not** themselves gate requests. The BINDING control is the broadcaster-configurable `MinYouTubeTrustScore` (L.4) + the `Vip`/`Moderator`-and-above role bypass enforced in `RequestAsync` (§3.1) / `IsAllowedAsync` (§3.3): a YouTube request passes when the requester bypasses by role or scores at/above the configured floor; Spotify is never trust-gated. A broadcaster may set the floor anywhere on 0–100 (or null = no YouTube floor), independent of these bands.

| Tier | Score | Meaning (informative) |
|---|---|---|
| `Untrusted` | 0–25 | new / low-reputation requester |
| `Low` | 26–50 | building reputation |
| `Standard` | 51–75 | established requester; soft cap 3 queued/session |
| `Trusted` | 76–100 | high reputation; soft cap 5 queued/session |

Default `MinYouTubeTrustScore` seeds to the `Standard` floor (51); the broadcaster adjusts it freely. Soft session caps remain tier-derived. Paid/priority queue-jumping is **not** a tier perk — it is the opt-in channel-point redeem path (§3.8), available regardless of tier when the broadcaster enables it.

**DTOs** (§4 — listed here as they back §3.3):

```csharp
namespace NomNomzBot.Application.Contracts.Music;

// Per-request metadata the score needs but the DB lacks (the DB-derived fields are read by the service).
public sealed record TrustEvaluationInput(
    string Provider, double ContentAgeMonths, long ContentViewCount,
    int ContentChannelVideoCount, long ContentChannelTotalViews,
    long ContentChannelSubscribers, double ContentChannelAgeMonths);

public sealed record TrustEvaluationDto(
    decimal Score, TrustTier Tier,
    double RequestScore, double AccountScore, double ContentScore, double PopularityScore);

public enum TrustTier { Untrusted, Low, Standard, Trusted }   // mirrors Infrastructure TrustTier (0..3)
```

**Persistence & wiring.** `ISongRequestTrustService.RecomputeAsync` (§3.3) builds `TrustContext`, calls `TrustScoreCalculator.Calculate`, upserts `SongRequestTrustScores.Score` (`decimal(8,4)`, 0–100 scale) and bumps the L.6 counters on accept/skip/reject. `RequestAsync` calls it before the YouTube trust gate (configurable `MinYouTubeTrustScore` + role bypass); Spotify requests skip the trust gate entirely. YouTube channel stats come from the YouTube provider's `channels.list` read (cached per channel-id).

**Code reconciliation.** `TrustScoreCalculator` (0–100, YouTube penalties, `TrustTier` — **canonical**, used by `MusicService.CheckTrustPermission`) supersedes `Services/General/TrustService.cs` (0.0–1.0, Record-table JSON, **no** YouTube penalties — **orphan**, different scale). Keep the calculator + persist via `SongRequestTrustScores` (L.6); **delete `TrustService.cs` + the 0–1 `ITrustService`**, or rescope `ITrustService` to delegate to the calculator. (The legacy `ITrustService` doc-comment claiming "0.0–1.0" is stale.)

### 3.10 `IMusicProviderManageApi` — NEW (`Application/Contracts/Music/IMusicProviderManageApi.cs`)

The **per-user manage surface** that is **not** the SR queue and **not** transport playback: playlist CRUD, saved-tracks/library, follow/unfollow, and ratings/subscriptions. Separated from `IMusicService` (single responsibility — SR queue vs. the user's own library) and from `IMusicProvider` (which is the playback/queue seam). One generic shape across providers; the active provider's `Capabilities` decide which calls are supported (`Library`/`Playlists`/`Subscriptions`), gated by flag — **no name checks**.

> **AUTH STANCE (binding, stated once).** **YouTube search** (the SR queue source, §3.5) rides the **app-level `YouTube:ApiKey`** — **no per-user OAuth**; a channel queues YouTube tracks without any YouTube connect. **Per-user manage** here (YouTube `videos.rate` / `subscriptions` / playlist writes) uses the **`youtube` OAuth scope** (scope-set `youtube.manage`). **Spotify is always per-user OAuth**; its **transport/remote** additionally needs **Premium** (surfaced as the `spotify.premium` capability, not a connect error — §3.5). **The connect/scope-set flow is owned by `integrations-oauth.md`** (descriptors + seeded scope-sets `spotify.playback`/`spotify.library`/`youtube.manage`/`youtube.readonly`, PKCE, progressive re-auth) — this spec **references it, does not duplicate it**. A manage call on an unconnected/insufficiently-scoped provider returns `Failure("MISSING_SCOPE")` (the connect/re-auth is initiated through integrations-oauth, not here).

```csharp
namespace NomNomzBot.Application.Contracts.Music;

public interface IMusicProviderManageApi
{
    // ── Playlists (capability: Playlists) ──────────────────────────────────────
    Task<Result<IReadOnlyList<MusicPlaylistDto>>> ListPlaylistsAsync(
        Guid broadcasterId, string provider, CancellationToken cancellationToken = default);
    // Lists the user's playlists on the provider. Read. MISSING_SCOPE if unscoped, CAPABILITY_UNSUPPORTED if no Playlists.

    Task<Result<MusicPlaylistDto>> CreatePlaylistAsync(
        Guid broadcasterId, string provider, CreateMusicPlaylistDto request, CancellationToken cancellationToken = default);
    // Creates a playlist (Spotify: POST /me/playlists — the /users/{id}/playlists form is retired, live-verified 2026-07-05; YouTube: playlists.insert). Capability: Playlists.

    Task<Result<MusicPlaylistDto>> UpdatePlaylistAsync(
        Guid broadcasterId, string provider, string playlistId, UpdateMusicPlaylistDto request, CancellationToken cancellationToken = default);
    // Renames / re-describes / sets visibility. NOT_FOUND if the playlist is not the user's. Capability: Playlists.

    Task<Result> DeletePlaylistAsync(
        Guid broadcasterId, string provider, string playlistId, CancellationToken cancellationToken = default);
    // Spotify: unfollow-own-playlist (Spotify has no hard delete); YouTube: playlists.delete. Capability: Playlists.

    Task<Result> AddPlaylistTracksAsync(
        Guid broadcasterId, string provider, string playlistId, IReadOnlyList<string> trackUris, CancellationToken cancellationToken = default);
    // Appends tracks to a playlist. Capability: Playlists.

    Task<Result> RemovePlaylistTracksAsync(
        Guid broadcasterId, string provider, string playlistId, IReadOnlyList<string> trackUris, CancellationToken cancellationToken = default);
    // Removes tracks from a playlist. Capability: Playlists.

    // ── Library / saved tracks + ratings (capability: Library) ─────────────────
    Task<Result> SaveTracksAsync(
        Guid broadcasterId, string provider, IReadOnlyList<string> trackUris, CancellationToken cancellationToken = default);
    // Spotify: PUT /me/library?uris= (Save Items to Library; /me/tracks WRITE is deprecated — live-verified 2026-07-05; max 40 URIs per call, chunk). YouTube: videos.rate(rating="like"). Capability: Library.

    Task<Result> RemoveSavedTracksAsync(
        Guid broadcasterId, string provider, IReadOnlyList<string> trackUris, CancellationToken cancellationToken = default);
    // Spotify: DELETE /me/library?uris= (/me/tracks WRITE is deprecated); YouTube: videos.rate(rating="none"). Capability: Library.

    Task<Result> RateTrackAsync(
        Guid broadcasterId, string provider, string trackUri, MusicRating rating, CancellationToken cancellationToken = default);
    // YouTube videos.rate(like|dislike|none). On Spotify, like/none map to save/remove; dislike → CAPABILITY_UNSUPPORTED. Capability: Library.

    // ── Follow / unfollow (capability: Library for artists/playlists; Subscriptions for channels) ─────────
    Task<Result> FollowAsync(
        Guid broadcasterId, string provider, MusicFollowTarget target, string targetId, CancellationToken cancellationToken = default);
    // Spotify: playlist follows ride PUT /me/library (the /playlists/{id}/followers endpoints are deprecated); artist
    // follows stay on PUT /me/following?type=artist — deprecated in docs but the ONLY artist wire (/me/library accepts
    // no artist URIs; docs contradiction, live-verified 2026-07-05 — graceful degradation). YouTube: subscriptions.insert.
    // Capability resolved by target kind: channel→Subscriptions, artist/playlist→Library.

    Task<Result> UnfollowAsync(
        Guid broadcasterId, string provider, MusicFollowTarget target, string targetId, CancellationToken cancellationToken = default);
    // Inverse of FollowAsync (Spotify: DELETE /me/library for playlists, DELETE /me/following?type=artist for artists;
    // YouTube: subscriptions.delete). Same capability resolution.

    // ── Library reads (capability: Library; channel-follow list: Subscriptions) — added 2026-07-05, the write surface needs its reads ──
    Task<Result<IReadOnlyList<TrackInfo>>> GetSavedTracksAsync(
        Guid broadcasterId, string provider, int limit = 50, int offset = 0, CancellationToken cancellationToken = default);
    // Spotify: GET /me/tracks (read remains live even though its writes are deprecated); YouTube: the liked-videos list. Paged.

    Task<Result<IReadOnlyList<bool>>> AreTracksSavedAsync(
        Guid broadcasterId, string provider, IReadOnlyList<string> trackUris, CancellationToken cancellationToken = default);
    // Positional contains-check. Spotify: the live saved/library contains endpoint (live-verify at build time — the old
    // /me/tracks/contains vs the new library form); YouTube: videos.getRating per id.

    Task<Result<IReadOnlyList<MusicFollowDto>>> GetFollowedAsync(
        Guid broadcasterId, string provider, MusicFollowTarget target, int limit = 50, CancellationToken cancellationToken = default);
    // Spotify: GET /me/following?type=artist (artists; playlist follows are library items → GET /me/library filtered);
    // YouTube: subscriptions.list (channel → Subscriptions capability). MusicFollowDto = (TargetId, Name, ImageUrl?).
}
```

These are management-plane writes against the **broadcaster's own** provider account (never a viewer's) — gated by `music:library:write` (§5). No domain events (provider-side state, mirrored on next read). Token decrypt/refresh and the connect/re-auth that grants the `spotify.library`/`youtube.manage` scope-set are delegated to the integrations vault + `integrations-oauth.md`, never handled here.

### 3.11 `ISongRequestBumpService` — NEW (`Application/Contracts/Music/ISongRequestBumpService.cs`)

Owns the **bump band** writes (L.5 `PriorityBand=bump`) and the **song-bump raffle** (L.7/L.8/L.9). Single responsibility — bump/raffle orchestration, distinct from the queue lifecycle (`ISongRequestQueueStateService`) and per-track ops (`IMusicService`). **No standalone raffle/giveaway primitive existed in the design corpus** (economy's "raffle" is the colloquial name of a `queue-jump` redeem, and economy games are gambling, not random-winner draws), so this is an SR-owned raffle; it **reuses the economy `CatalogPurchases` debit** (K.11) for paid entry — the exact `CatalogPurchaseId` link the paid lane uses (§3.8) — rather than inventing a parallel spend path. Bump/raffle moderation reuses the existing `music:queue:moderate` action key; viewer raffle entry reuses `music:request:submit`.

```csharp
namespace NomNomzBot.Application.Contracts.Music;

public interface ISongRequestBumpService
{
    // ── Bump command (human override) ──────────────────────────────────────────
    Task<Result<SongRequestItemDto>> BumpAsync(
        Guid broadcasterId, Guid songRequestItemId, BumpSource source, CancellationToken cancellationToken = default);
    // Moves a queued item into the bump band at fair rank (rank = the user's existing bumps in the band + 1),
    // sets PriorityBand=bump + BumpSource, re-materializes Position within the band under the per-tenant lock,
    // SaveChanges, emits SongRequestQueueChangedEvent(bumped). Backs !bump <songId> and the raffle/redeem paths.
    // Failure NOT_FOUND if the item is absent/not queued.

    Task<Result<SongRequestItemDto>> BumpUserAsync(
        Guid broadcasterId, Guid targetUserId, BumpSource source, CancellationToken cancellationToken = default);
    // Resolves the target user's earliest queued item (their head) and bumps it (BumpAsync). Backs !bump <user>.
    // Failure NOT_FOUND if the user has no queued item; if they have a bump token (L.9) instead, the caller
    // grants/consumes it via the token path (no auto-token here — explicit !bump targets an existing song).

    // ── Bump tokens (raffle fallback) ──────────────────────────────────────────
    Task<Result> GrantBumpTokenAsync(
        Guid broadcasterId, Guid userId, CancellationToken cancellationToken = default);
    // Increments SongRequestBumpTokens.TokenCount (L.9) for (channel, user); creates the row if absent. SaveChanges.
    // Called on a raffle draw when the winner has NO queued song. No domain event (consumed on next request).

    Task<Result<bool>> TryConsumeBumpTokenAsync(
        Guid broadcasterId, Guid userId, Guid newSongRequestItemId, CancellationToken cancellationToken = default);
    // If the user has a bump token (TokenCount > 0), decrements it and bumps newSongRequestItemId (BumpSource=raffle),
    // returns true; else returns false (no-op). Called by IMusicService.RequestAsync after enqueue, before SaveChanges,
    // so a token holder's next request lands in the bump band automatically. Shares the request's UoW transaction.

    // ── Raffle ─────────────────────────────────────────────────────────────────
    Task<Result<SongRequestRaffleDto>> StartRaffleAsync(
        Guid broadcasterId, RaffleTrigger trigger, CancellationToken cancellationToken = default);
    // Opens a SongRequestRaffles row (Status=open) snapshotting L.4 RaffleEntryCost/RaffleTicketsPerUser/RaffleWinnerCount.
    // Failure VALIDATION_FAILED if RaffleEnabled is false; CONFLICT if an open raffle already exists for the channel.
    // SaveChanges, emits SongRequestRaffleEvent(Phase=started). trigger ∈ manual (!raffle) | auto (RaffleIntervalMinutes).

    Task<Result<SongRequestRaffleEntryDto>> EnterRaffleAsync(
        Guid broadcasterId, Guid entrantUserId, CancellationToken cancellationToken = default);
    // Viewer entry into the open raffle (music:request:submit). When EntryCostSnapshot > 0, debits channel points via the
    // economy module exactly like the queue-jump redeem (creates a CatalogPurchase, links CatalogPurchaseId on the entry —
    // mirrors L.5). Upserts SongRequestRaffleEntries (Unique (RaffleId, EntrantUserId)); TicketCount clamped to
    // TicketsPerUserSnapshot (default 1 — fairness over pay-to-win). All writes wrapped in IUnitOfWork (debit + entry
    // all-or-nothing, rollback on failure). Failure VALIDATION_FAILED if no open raffle / RaffleEnabled false;
    // RATE_LIMITED if already at the per-user ticket cap; FORBIDDEN on insufficient balance (the economy debit fails).

    Task<Result<SongRequestRaffleResultDto>> DrawRaffleAsync(
        Guid broadcasterId, CancellationToken cancellationToken = default);
    // Picks WinnerCountSnapshot random winners (RandomNumberGenerator over the distinct entrants, ticket-weighted),
    // flags SongRequestRaffleEntries.IsWinner. For each winner: if they have a queued song, BumpAsync it
    // (BumpSource=raffle); else GrantBumpTokenAsync. Sets the raffle Status=drawn + DrawnAt. All writes wrapped in
    // IUnitOfWork (all-or-nothing). SaveChanges, emits SongRequestRaffleEvent(Phase=drawn, Winners=…) +
    // SongRequestQueueChangedEvent(bumped) per bumped winner. Failure VALIDATION_FAILED if no open raffle.

    Task<Result> CancelRaffleAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Sets the open raffle Status=cancelled (no winners, no bumps). Channel-point entry costs are refunded via the
    // economy refund path (each entry's CatalogPurchaseId). All-or-nothing in IUnitOfWork. SaveChanges. No bump event.

    Task<Result<SongRequestRaffleDto?>> GetActiveRaffleAsync(
        Guid broadcasterId, CancellationToken cancellationToken = default);
    // Reads the channel's open raffle (or null). Read-only — backs the public/dashboard active-raffle view.
}

public enum BumpSource { Raffle, Command, Redeem }   // mirrors L.5 BumpSource [VC:enum]
public enum RaffleTrigger { Manual, Auto }           // mirrors L.7 Trigger [VC:enum]
```

The interval auto-run (`RaffleIntervalMinutes`, L.4) is driven by the existing `SongRequestSequencerLoop` `IHostedService` tick (§7) — when the cadence elapses it calls `StartRaffleAsync(…, RaffleTrigger.Auto)` then `DrawRaffleAsync` after the entry window; no new hosted service. `IMusicService.RequestAsync` (§3.1) calls `ISongRequestBumpService.TryConsumeBumpTokenAsync` after enqueue (within its UoW) so a token holder's next request auto-bumps; the auto-bump-first-song band (§3.8) is applied by `RequestAsync` setting `PriorityBand=auto_bump` when `AutoBumpFirstSong` is on and the requester's per-stream count is 0 (reusing the `PerStreamLimit` tracking — no new counter).

## 4. DTOs / contracts (records — `NomNomzBot.Application.Contracts.Music` unless noted)

The existing `NowPlayingDto`, `QueueItemDto`, `MusicQueueDto`, `SongRequestDto`, `MusicConfigDto`, `UpdateMusicConfigDto` (`Application/DTOs/Music/`) are **kept and extended**, not duplicated. New records below.

```csharp
// ── Now playing / queue read models ──────────────────────────────────────────
public sealed record NowPlayingDto(           // EXISTS — extend: add SongRequestItemId
    Guid? SongRequestItemId, string? TrackName, string? Artist, string? Album, string? ImageUrl,
    int DurationSeconds, int ProgressSeconds, bool IsPlaying, int Volume,
    string? RequestedBy, string Provider);

public sealed record SongRequestItemDto(
    Guid Id, string Provider, string ProviderTrackId, string? Title, string? Artist,
    int? DurationSeconds, string? ThumbnailUrl, Guid RequestedByUserId, string? RequestedByDisplayName,
    int Position, string PriorityBand, string? BumpSource, string Status, string? RejectionReason,
    int RetryCount, string? FailureReason, DateTime? NextRetryAt, DateTime RequestedAt, DateTime? PlayedAt);
// Status ∈ queued|playing|waiting|retrying|played|skipped|rejected (L.5). RetryCount/FailureReason/NextRetryAt
// back the §3.5.2 failure taxonomy (waiting = provider/environment down; retrying = bounded transient retry).
// PriorityBand ∈ bump|auto_bump|normal (§3.8 three-band ordering); BumpSource ∈ raffle|command|redeem (null unless bumped).

public sealed record MusicQueueDto(NowPlayingDto? NowPlaying, IReadOnlyList<SongRequestItemDto> Queue); // EXISTS — item type widened to SongRequestItemDto

// ── Request input (public + authed) ──────────────────────────────────────────
public sealed record SongRequestInputDto
{
    [Required, MaxLength(500)] public required string Query { get; init; }   // URL, provider id, or free-text search
    public string? Provider { get; init; }                                   // "spotify"|"youtube"; null = ProviderPriority
    public Guid? RequestedByUserId { get; init; }                            // null on public page → resolved from page token + supplied login
    [MaxLength(50)] public string? RequestedByLogin { get; init; }           // public page: viewer-entered Twitch login (validated, resolved to Users)
}

// ── Queue settings ───────────────────────────────────────────────────────────
public sealed record SongRequestQueueDto(
    bool IsOpen, bool IsPaused, int MaxQueueLength, bool AllowExplicit,
    decimal? MinYouTubeTrustScore, bool SubscriberOnly, string MinStandingToRequest,
    IReadOnlyList<string> EnabledProviders, IReadOnlyList<string> ProviderPriority, bool CrossResolveForeignLinks,
    IReadOnlyDictionary<string, int?> PendingLimits, int? PaidPendingLimit, bool PaidExtraSlotEnabled,
    bool QueueJumpEnabled, int? PerStreamLimit, int MaxDurationFreeSeconds, int MaxDurationPaidSeconds,
    bool StripYouTubeAds, bool AutoBumpFirstSong, bool RaffleEnabled, int RaffleEntryCost,
    int RaffleTicketsPerUser, int RaffleWinnerCount, int? RaffleIntervalMinutes,
    string? SpotifyLockedDeviceId, string? SpotifyLockedDeviceName, int CurrentLength);
// PendingLimits keys: everyone|subscriber_t1|subscriber_t2|subscriber_t3|vip|moderator|broadcaster; null value = unlimited.

public sealed record UpdateSongRequestQueueDto
{
    public bool? IsOpen { get; init; }
    public bool? IsPaused { get; init; }
    [Range(1, 1000)] public int? MaxQueueLength { get; init; }
    public bool? AllowExplicit { get; init; }
    [Range(0, 100)] public decimal? MinYouTubeTrustScore { get; init; } // YouTube auto-approval floor; null = no floor; Vip/Moderator+ bypass by role
    public bool? SubscriberOnly { get; init; }
    public string? MinStandingToRequest { get; init; } // community-standing floor: everyone|subscriber|vip|moderator
    public IReadOnlyList<string>? EnabledProviders { get; init; } // subset of {"spotify","youtube"}; which providers accept requests
    public IReadOnlyList<string>? ProviderPriority { get; init; } // subset of {"spotify","youtube"}; preferred-for-ambiguous + cross-resolve target
    public bool? CrossResolveForeignLinks { get; init; }
    public IReadOnlyDictionary<string, int?>? PendingLimits { get; init; } // standing/sub-tier key → concurrent-pending cap; null value = unlimited
    [Range(1, 100)] public int? PaidPendingLimit { get; init; } // channel-point lane cap; omit/null = off
    public bool? PaidExtraSlotEnabled { get; init; }
    public bool? QueueJumpEnabled { get; init; }
    [Range(1, 1000)] public int? PerStreamLimit { get; init; } // lifetime-in-stream per-user; omit/null = off
    [Range(1, 3600)] public int? MaxDurationFreeSeconds { get; init; }
    [Range(1, 3600)] public int? MaxDurationPaidSeconds { get; init; }
    public bool? StripYouTubeAds { get; init; }
    public bool? AutoBumpFirstSong { get; init; } // each requester's first song this stream → auto-bump band (§3.8)
    public bool? RaffleEnabled { get; init; }
    [Range(0, 1000000)] public int? RaffleEntryCost { get; init; } // channel points per ticket; 0 = free entry
    [Range(1, 100)] public int? RaffleTicketsPerUser { get; init; } // fairness cap (default 1)
    [Range(1, 50)] public int? RaffleWinnerCount { get; init; }
    [Range(1, 1440)] public int? RaffleIntervalMinutes { get; init; } // omit/null = manual-only !raffle
}

public sealed record MoveQueueItemDto { [Range(0, 999)] public required int NewPosition { get; init; } }

public sealed record SetLockedDeviceDto { public string? DeviceId { get; init; } public string? DeviceName { get; init; } }
// Persists the preferred Spotify device (SpotifyLockedDeviceId/Name, L.4); null/null clears the lock.

// ── Trust ─────────────────────────────────────────────────────────────────────
public sealed record SongRequestTrustDto(
    Guid RequesterUserId, decimal Score, int TotalRequests, int PlayedCount, int SkippedCount,
    int RejectedCount, bool IsBlocked, DateTime? LastRequestAt);

public sealed record SetTrustBlockedDto { public required bool IsBlocked { get; init; } }

// ── Advanced trust scoring config (§3.9; L.4 TrustScoringConfig JSON) ─────────────
// Metric base (weights/decays) is Bamo's FIXED foundation — no config surface here.
// Each buff/debuff: one toggle (default true) + tunable magnitude (default = the Bamo constant).
// All-defaults == today's behavior exactly. Validation: penalties/thresholds ≥ 0; multipliers in (0,1].
// Rides the config GET/PUT (music:config:write) — wired into MusicConfigDto/UpdateMusicConfigDto below.
public sealed record TrustScoringConfigDto(
    bool ReputationBoostEnabled,        int ReputationBoostMinRequests,
    bool FollowPenaltyEnabled,          decimal FollowPenaltyMultiplier,  int FollowPenaltyMinDays,
    bool YouTubeQualityPenaltyEnabled,  decimal YouTubeQualityMultiplier,
        int MinChannelVideoCount, long MinChannelTotalViews, long MinChannelSubscribers, int MinChannelAgeMonths,
    bool SkipPenaltyEnabled,            decimal SkipPenalty,
    bool TimeoutPenaltyEnabled,         decimal TimeoutPenalty,
    bool BanPenaltyEnabled,             decimal BanPenalty);
// Defaults (reproduce Bamo exactly): all *Enabled = true; ReputationBoostMinRequests = 10;
// FollowPenaltyMultiplier = 0.75, FollowPenaltyMinDays = 1; YouTubeQualityMultiplier = 0.75,
// MinChannelVideoCount = 5, MinChannelTotalViews = 5000, MinChannelSubscribers = 25, MinChannelAgeMonths = 1;
// SkipPenalty = 5; TimeoutPenalty = 10; BanPenalty = 30.

// MusicConfigDto / UpdateMusicConfigDto (Application/DTOs/Music/) are EXTENDED (not duplicated):
//   MusicConfigDto       gains  `TrustScoringConfigDto TrustScoring`        (always populated — defaults when unset).
//   UpdateMusicConfigDto gains  `TrustScoringConfigDto? TrustScoring`       (null = leave unchanged; a supplied value
//     replaces the whole config and is validated: every *Multiplier ∈ (0,1]; every penalty/threshold ≥ 0).
// Both ride the existing `config` GET/PUT route (music:config:write, §5) — no new service method, DTO, or route.

// ── Bump + song-bump raffle (§3.11; L.7/L.8/L.9) ─────────────────────────────────
public sealed record SongRequestRaffleDto(
    Guid Id, string Status, int EntryCost, int TicketsPerUser, int WinnerCount,
    string Trigger, int EntryCount, DateTime OpenedAt, DateTime? DrawnAt);
// Status ∈ open|drawn|cancelled (L.7); Trigger ∈ manual|auto. EntryCount = live SongRequestRaffleEntries count.

public sealed record SongRequestRaffleEntryDto(
    Guid RaffleId, Guid EntrantUserId, string? EntrantDisplayName, int TicketCount, bool IsWinner);

public sealed record SongRequestRaffleResultDto(
    Guid RaffleId, IReadOnlyList<SongRequestRaffleWinnerDto> Winners);
public sealed record SongRequestRaffleWinnerDto(
    Guid UserId, string DisplayName, Guid? BumpedItemId, bool GrantedBumpToken);
// BumpedItemId set when the winner's queued song was moved to the bump band; GrantedBumpToken true when they
// had no queued song and received a bump token (L.9) consumed on their next request.

// ── Provider config (E.5 MusicProviderConfig — generic, one shape for every provider) ─────────
public sealed record MusicProviderConfigDto(
    string Provider, bool AllowSongRequests, int MaxQueueLength, bool BlockExplicit,
    IReadOnlyDictionary<string, object?> ProviderSettings);
// ProviderSettings carries the provider-specific knobs (Spotify: Market/RequirePlaylistContext/FallbackPlaylistUri;
// YouTube: Region/MaxVideoDurationSeconds/BlockAgeRestricted/EmbeddableOnly; other providers: their own). The set of
// keys/types is defined by the provider's registered settings schema, not by this record.

public sealed record UpdateMusicProviderConfigDto
{
    public bool? AllowSongRequests { get; init; }
    [Range(1, 1000)] public int? MaxQueueLength { get; init; }
    public bool? BlockExplicit { get; init; }
    public IReadOnlyDictionary<string, object?>? ProviderSettings { get; init; }
    // Patch of provider-specific knobs; validated by IMusicProviderRegistry.ValidateSettings(provider, …)
    // against the provider's registered schema before persist (no provider-typed record here).
}

// ── Provider remote / transport ─────────────────────────────────────────────────
public sealed record MusicDeviceDto(
    string Id, string Name, string Type, bool IsActive, int? VolumePercent);
public sealed record SetRepeatDto { public required MusicRepeatMode Mode { get; init; } }
public sealed record SeekDto { [Range(0, 86400)] public required int PositionSeconds { get; init; } }
public sealed record TransferPlaybackDto { public required string DeviceId { get; init; } public bool StartPlaying { get; init; } = true; }

// ── Provider manage (library / playlists / follow / ratings) ─────────────────────
public sealed record MusicPlaylistDto(
    string Id, string Name, string? Description, bool IsPublic, int TrackCount, string? ImageUrl, string Provider);

public sealed record CreateMusicPlaylistDto
{
    [Required, MaxLength(150)] public required string Name { get; init; }
    [MaxLength(300)] public string? Description { get; init; }
    public bool IsPublic { get; init; }
}

public sealed record UpdateMusicPlaylistDto
{
    [MaxLength(150)] public string? Name { get; init; }
    [MaxLength(300)] public string? Description { get; init; }
    public bool? IsPublic { get; init; }
}

public sealed record PlaylistTracksDto { public required IReadOnlyList<string> TrackUris { get; init; } }
public sealed record SavedTracksDto { public required IReadOnlyList<string> TrackUris { get; init; } }
public sealed record RateTrackDto { public required string TrackUri { get; init; } public required MusicRating Rating { get; init; } }
public sealed record FollowDto { public required MusicFollowTarget Target { get; init; } public required string TargetId { get; init; } }

public enum MusicRating { None, Like, Dislike }
public enum MusicFollowTarget { Channel, Artist, Playlist }

// ── Public SR-page ─────────────────────────────────────────────────────────────
public sealed record SongRequestPageDto(
    string ChannelName, bool IsOpen, bool IsPaused, IReadOnlyList<string> Providers, int CurrentLength);
// Providers = the channel's EnabledProviders (which providers the public page may submit to), not all registered providers.
```

---

## 5. Controller endpoints

Two controllers. **Authed** queue/control/config under the existing `MusicController` (extend in place); **public** SR-page submit/read in a new `PublicSongRequestController`.

**Role gate.** Each `MusicController` route names a plane, a role floor, and (where one exists) a Gate-2 action key:
- **Gate 1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's — this is what lets Everyone-floored actions like `music:request:submit` actually reach viewers).
- **Gate 2** = `IActionAuthorizationService.AuthorizeActionAsync(userId, channelId, actionKey)` enforces the per-route floor named in the action-key column before the service call (`FORBIDDEN`/403 when below). The action key is the only contract; the effective caller level is `IRoleResolver.ResolveEffectiveLevelAsync` = MAX(community standing, ManagementRole membership, active `!permit` grant), compared against the action's required level.
- The keys (`music:request:submit`, `music:queue:moderate`, `music:config:write`, `music:token:read`, `music:token:rotate`, `music:remote:control`, `music:library:write`) are seeded global `ActionDefinitions` (Domain B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. `music:remote:control` gates provider transport/remote (previous/seek/shuffle/repeat/transfer-device) at the **`Moderator`** floor (live-show control, same as queue moderation). `music:library:write` gates the broadcaster-account manage surface (playlist CRUD, saved-tracks, follow/ratings) at the **`Editor`/`Broadcaster`** floor (it writes the broadcaster's own provider account, same posture as token read/rotate).

Floors as seeded: read = community plane, `Everyone`; submit a request = community plane, authenticated viewer (dynamically gated by queue config — `MinStandingToRequest`, `SubscriberOnly`, the YouTube `MinYouTubeTrustScore` floor + role bypass, the per-standing `PendingLimits` / `PaidPendingLimit` / `PerStreamLimit` caps, duration limits — in `IMusicService.RequestAsync`); mutating queue state, removing/reordering others' items, clearing, config, trust-block = management plane, `Moderator` (`SuperMod`/`Broadcaster` inherit); provider config + token read/rotate = management plane, `Broadcaster`/`Editor` floor. The **advanced `TrustScoringConfig`** (§3.9 per-modifier buff/debuff toggles + magnitudes, L.4) is sensitive and rides the existing **`config` GET/PUT** route via `MusicConfigDto`/`UpdateMusicConfigDto` (`music:config:write`, **Editor/Broadcaster** floor) — **not** the mod-level `queue/settings` route; **no new route or action key**. The simple `MinYouTubeTrustScore` floor stays on `queue/settings` (`music:queue:moderate`) as before. All the new allowance / duration / provider-enablement / cross-resolve / ad-strip / **auto-bump-first-song + raffle** config (`AutoBumpFirstSong`/`RaffleEnabled`/`RaffleEntryCost`/`RaffleTicketsPerUser`/`RaffleWinnerCount`/`RaffleIntervalMinutes`) lands on the existing `PUT queue/settings` route via the extended `UpdateSongRequestQueueDto` (`music:queue:moderate`) — **no new config routes**; the Spotify locked device + sequencer playback-state ride `music:remote:control` (live-show control). Bump + raffle **moderation** (bump-a-song, raffle start/draw/cancel) reuses `music:queue:moderate` (Moderator floor — matches StreamElements' song-control-commands-default-Moderator+ precedent); **viewer raffle entry** reuses `music:request:submit` (community plane, dynamically gated by `RaffleEnabled`); reading the active raffle is community-plane `Everyone`. **No new action keys are introduced.**

### 5.1 `MusicController` — `[Route("api/v{version:apiVersion}/channels/{channelId:guid}/music")]` `[Authorize]`

> `channelId` widened `string` → `:guid`. Existing playback endpoints (`config GET/PUT`, `queue GET`, `skip`, `pause`, `resume`, `now-playing`) are kept; routes below are the full target set.

| Verb | Route | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `queue` | — | `StatusResponseDto<MusicQueueDto>` | community / Everyone |
| POST | `queue` | `SongRequestInputDto` | `StatusResponseDto<SongRequestItemDto>` | community / Everyone · `music:request:submit` |
| DELETE | `queue/{itemId:guid}` | — | `StatusResponseDto<object>` (204) | management / Moderator · `music:queue:moderate` (own item: Everyone) |
| PATCH | `queue/{itemId:guid}/move` | `MoveQueueItemDto` | `StatusResponseDto<SongRequestItemDto>` | management / Moderator · `music:queue:moderate` |
| POST | `queue/clear` | — | `StatusResponseDto<object>` | management / Moderator · `music:queue:moderate` |
| GET | `queue/settings` | — | `StatusResponseDto<SongRequestQueueDto>` | community / Everyone |
| PUT | `queue/settings` | `UpdateSongRequestQueueDto` | `StatusResponseDto<SongRequestQueueDto>` | management / Moderator · `music:queue:moderate` |
| POST | `queue/open` `queue/close` `queue/pause` `queue/resume` | — | `StatusResponseDto<object>` | management / Moderator · `music:queue:moderate` |
| POST | `skip` | — | `StatusResponseDto<object>` | management / Moderator · `music:queue:moderate` |
| POST | `pause` / `resume` | — | `StatusResponseDto<object>` | management / Moderator · `music:queue:moderate` |
| POST | `previous` | — | `StatusResponseDto<object>` | management / Moderator · `music:remote:control` |
| POST | `seek` | `SeekDto` | `StatusResponseDto<object>` | management / Moderator · `music:remote:control` |
| POST | `shuffle` | `{ "enabled": bool }` | `StatusResponseDto<object>` | management / Moderator · `music:remote:control` |
| POST | `repeat` | `SetRepeatDto` | `StatusResponseDto<object>` | management / Moderator · `music:remote:control` |
| GET | `devices` | — | `StatusResponseDto<List<MusicDeviceDto>>` | management / Moderator · `music:remote:control` |
| POST | `transfer` | `TransferPlaybackDto` | `StatusResponseDto<object>` | management / Moderator · `music:remote:control` |
| PUT | `devices/locked` | `SetLockedDeviceDto` | `StatusResponseDto<object>` | management / Moderator · `music:remote:control` |
| GET | `playback-state` | — | `StatusResponseDto<PlaybackStateDto>` | management / Moderator · `music:remote:control` |
| GET | `now-playing` | — | `StatusResponseDto<NowPlayingDto>` | community / Everyone |
| GET/PUT | `config` | `UpdateMusicConfigDto` | `StatusResponseDto<MusicConfigDto>` | management / Moderator · `music:config:write` (PUT) |
| GET/PUT | `config/providers/{provider}` | `UpdateMusicProviderConfigDto` | `StatusResponseDto<MusicProviderConfigDto>` | management / Moderator · `music:config:write` (PUT) |
| GET | `trust/{userId:guid}` | — | `StatusResponseDto<SongRequestTrustDto>` | management / Moderator · `music:queue:moderate` |
| PUT | `trust/{userId:guid}/blocked` | `SetTrustBlockedDto` | `StatusResponseDto<object>` | management / Moderator · `music:queue:moderate` |
| POST | `queue/{itemId:guid}/bump` | — | `StatusResponseDto<SongRequestItemDto>` | management / Moderator · `music:queue:moderate` |
| GET | `raffle` | — | `StatusResponseDto<SongRequestRaffleDto>` | community / Everyone |
| POST | `raffle/start` | — | `StatusResponseDto<SongRequestRaffleDto>` | management / Moderator · `music:queue:moderate` |
| POST | `raffle/draw` | — | `StatusResponseDto<SongRequestRaffleResultDto>` | management / Moderator · `music:queue:moderate` |
| POST | `raffle/cancel` | — | `StatusResponseDto<object>` | management / Moderator · `music:queue:moderate` |
| POST | `raffle/enter` | — | `StatusResponseDto<SongRequestRaffleEntryDto>` | community / Everyone · `music:request:submit` (gated by `RaffleEnabled`) |
| GET | `providers/{provider}/playlists` | — | `StatusResponseDto<List<MusicPlaylistDto>>` | management / Editor · `music:library:write` (read of own account) |
| POST | `providers/{provider}/playlists` | `CreateMusicPlaylistDto` | `StatusResponseDto<MusicPlaylistDto>` | management / Editor · `music:library:write` |
| PUT | `providers/{provider}/playlists/{playlistId}` | `UpdateMusicPlaylistDto` | `StatusResponseDto<MusicPlaylistDto>` | management / Editor · `music:library:write` |
| DELETE | `providers/{provider}/playlists/{playlistId}` | — | `StatusResponseDto<object>` | management / Editor · `music:library:write` |
| POST | `providers/{provider}/playlists/{playlistId}/tracks` | `PlaylistTracksDto` | `StatusResponseDto<object>` | management / Editor · `music:library:write` |
| DELETE | `providers/{provider}/playlists/{playlistId}/tracks` | `PlaylistTracksDto` | `StatusResponseDto<object>` | management / Editor · `music:library:write` |
| POST | `providers/{provider}/library/save` | `SavedTracksDto` | `StatusResponseDto<object>` | management / Editor · `music:library:write` |
| POST | `providers/{provider}/library/remove` | `SavedTracksDto` | `StatusResponseDto<object>` | management / Editor · `music:library:write` |
| POST | `providers/{provider}/library/rate` | `RateTrackDto` | `StatusResponseDto<object>` | management / Editor · `music:library:write` |
| POST | `providers/{provider}/follow` | `FollowDto` | `StatusResponseDto<object>` | management / Editor · `music:library:write` |
| POST | `providers/{provider}/unfollow` | `FollowDto` | `StatusResponseDto<object>` | management / Editor · `music:library:write` |
| GET | `sr-page-token` | — | `StatusResponseDto<string>` | management / Editor · `music:token:read` (`Broadcaster`/`Editor` floor) |
| POST | `sr-page-token/rotate` | — | `StatusResponseDto<string>` | management / Editor · `music:token:rotate` (`Broadcaster`/`Editor` floor) |

### 5.2 `PublicSongRequestController` — `[Route("api/v{version:apiVersion}/public/sr/{pageToken}")]` `[AllowAnonymous]`

Token-gated, no JWT. Rate-limited (`[EnableRateLimiting("public-sr")]`). Backs the `/(public)/sr/[channel]` page.

| Verb | Route | Request DTO | Response DTO | Auth |
|---|---|---|---|---|
| GET | _(root)_ | — | `StatusResponseDto<SongRequestPageDto>` | SR-page token (`ISongRequestPageTokenService.ResolveAsync`) |
| GET | `queue` | — | `StatusResponseDto<MusicQueueDto>` | SR-page token |
| POST | `request` | `SongRequestInputDto` | `StatusResponseDto<SongRequestItemDto>` | SR-page token; viewer login validated → `Users`; gates in `RequestAsync` |
| GET | `raffle` | — | `StatusResponseDto<SongRequestRaffleDto>` | SR-page token (active-raffle state for the public page; null body when none open) |

---

## 6. Pipeline actions

Five chat-command actions already exist in `Infrastructure/Pipeline/Actions/MusicActions.cs` implementing `ICommandAction` — **EXTEND in place** to route through the persistent `IMusicService` (`RequestAsync`/`SkipAsync`/`GetQueueAsync`/`GetNowPlayingAsync`) and widen `ctx.BroadcasterId` to `Guid`. The existing five keep their `ActionType` strings (stable contract). **Two new transport actions** (`song_previous`, `song_seek`) are added for chat-driven remote control (the library/playlist/transfer-device manage surface stays dashboard-only — it writes the broadcaster's own account and has no natural chat-command shape, so no pipeline action for it). Both route through `IMusicService` and surface `CAPABILITY_UNSUPPORTED`/`PREMIUM_REQUIRED` as a chat reply. **Four bump/raffle actions** (`song_bump`, `song_raffle`, `song_raffle_enter`) route through `ISongRequestBumpService` (§3.11): `!bump`/`!raffle` are Moderator+ via command role config (floor `music:queue:moderate`, matching StreamElements' song-control-default-Moderator+ precedent) and viewer raffle entry rides `music:request:submit` (gated by `RaffleEnabled`). The viewer-paid bump path stays the existing channel-point `queue-jump` redeem — there is **no free viewer bump command and no paid command alias**.

| `ActionType` (string) | Config DTO (action params) | Behavior |
|---|---|---|
| `song_request` | `{ "query": string }` (supports `{var}` substitution) | Resolves query → calls `RequestAsync(broadcasterId, new SongRequestInputDto{Query, RequestedByUserId=ctx.TriggeredByUserId})`; replies in chat with queued track or rejection reason. Emits `SongRequestedEvent`/`SongRequestRejectedEvent` via the service. |
| `song_skip` | `{}` | Calls `SkipAsync(broadcasterId, ctx.TriggeredByUserId)`; chat-confirms. Gated by command's own role config. |
| `song_current` | `{}` | Calls `GetNowPlayingAsync`; posts now-playing line to chat. |
| `song_queue` | `{ "max": int = 5 }` | Calls `GetQueueAsync`; posts the first `max` queued items to chat. |
| `song_volume` | `{ "volume": int 0–100 }` (supports `{var}`) | Calls `SetVolumeAsync`; chat-confirms. Fails `CAPABILITY_UNSUPPORTED` when the active provider lacks the `Volume` capability (e.g. YouTube). |
| `song_previous` | `{}` | **NEW.** Calls `PreviousAsync(broadcasterId)`; chat-confirms. Fails `CAPABILITY_UNSUPPORTED` (e.g. YouTube) / `PREMIUM_REQUIRED` (non-Premium Spotify) as a chat reply. Gated by command's own role config. |
| `song_seek` | `{ "position": int seconds }` (supports `{var}`) | **NEW.** Calls `SeekAsync(broadcasterId, position)`; chat-confirms. Same capability/Premium failure surfacing. |
| `song_bump` | `{ "target": string }` (`<user>` or `<songId>`, supports `{var}`) | **NEW.** `!bump` — Moderator+ (command role config, floor `music:queue:moderate`). Resolves `target` → `ISongRequestBumpService.BumpUserAsync`/`BumpAsync(…, BumpSource.Command)`; chat-confirms the bumped track. Human override — pushes the target's song into the bump band at fair rank. Failure NOT_FOUND replied in chat. |
| `song_raffle` | `{ "action": string = "start" }` (`start`\|`draw`\|`cancel`) | **NEW.** `!raffle` — Moderator+ (command role config, floor `music:queue:moderate`). Routes to `ISongRequestBumpService.StartRaffleAsync`/`DrawRaffleAsync`/`CancelRaffleAsync` (default `!raffle` = start, then `!raffle draw` to draw); chat-announces start/winners. Fails (chat reply) when `RaffleEnabled` is false. |
| `song_raffle_enter` | `{}` | **NEW.** Viewer raffle entry (`music:request:submit`, gated by `RaffleEnabled`). Calls `EnterRaffleAsync(broadcasterId, ctx.TriggeredByUserId)`; chat-confirms entry (or replies the channel-point debit failure / per-user ticket cap). The channel-point spend rides the economy `CatalogPurchases` debit (§3.11). |

---

## 7. DI registration

`Infrastructure/DependencyInjection.cs` (extend the existing music block; existing lines noted). All tenant-scoped services **Scoped** (they touch `IApplicationDbContext`); the overlay feed adapter is **Scoped** too (resolves `IHubContext`, itself singleton). Profile-adapter variants chosen by `App__DeploymentMode` (lite SQLite / SaaS Postgres) — the SR services are profile-agnostic (they use `IApplicationDbContext`, which is the adapter); only `INowPlayingFeed` has no profile variance (SignalR is single-process lite / Redis-backplane SaaS, but the interface is identical — backplane is wired at the SignalR registration, not here).

```csharp
// Music providers (EXISTING — keep) — registered THROUGH the registry by their Capabilities
services.AddHttpClient("spotify").AddSpotifyResilienceHandler();
services.AddScoped<SpotifyMusicProvider>();
services.AddScoped<YouTubeMusicProvider>();
services.AddScoped<IMusicProvider, SpotifyMusicProvider>();   // ADD: expose via interface for the registry's IEnumerable<IMusicProvider>
services.AddScoped<IMusicProvider, YouTubeMusicProvider>();   // ADD
services.AddSingleton<IMusicProviderRegistry, MusicProviderRegistry>();  // NEW: keyed by Provider; aggregates Capabilities + settings schemas

// SR services
services.AddScoped<IMusicService, MusicService>();                                   // EXISTING (rewritten to persistence)
services.AddScoped<IMusicConfigService, MusicConfigService>();                       // EXISTING (extended)
services.AddScoped<ISongRequestQueueStateService, SongRequestQueueStateService>();   // NEW
services.AddScoped<ISongRequestTrustService, SongRequestTrustService>();             // NEW
services.AddScoped<ISongRequestPageTokenService, SongRequestPageTokenService>();     // NEW
services.AddScoped<IMusicProviderManageApi, MusicProviderManageApi>();               // NEW (library/playlist/follow/ratings; delegates to the active IMusicProvider)
services.AddScoped<INowPlayingFeed, NowPlayingFeed>();                               // NEW (wraps IHubContext<OverlayHub,IOverlayClient>)
services.AddScoped<ISongRequestSequencer, SongRequestSequencer>();                   // NEW (drip-feed/browser-source playback driver §3.5.2; one now-playing pointer, playable-head + autoplay watchdog)
services.AddScoped<ISongRequestBumpService, SongRequestBumpService>();               // NEW (bump band + song-bump raffle §3.11; reuses economy CatalogPurchases for paid entry)
services.AddHostedService<SongRequestSequencerLoop>();                               // NEW (IHostedService poll tick → ISongRequestSequencer.PollAsync per active channel; also fires RaffleIntervalMinutes auto-raffle via ISongRequestBumpService; resolves the Scoped services per tick)

// Pipeline actions (EXISTING five + two transport + three bump/raffle actions)
services.AddTransient<ICommandAction, SongRequestAction>();
services.AddTransient<ICommandAction, SongSkipAction>();
services.AddTransient<ICommandAction, SongCurrentAction>();
services.AddTransient<ICommandAction, SongQueueAction>();
services.AddTransient<ICommandAction, SongVolumeAction>();
services.AddTransient<ICommandAction, SongPreviousAction>();   // NEW
services.AddTransient<ICommandAction, SongSeekAction>();       // NEW
services.AddTransient<ICommandAction, SongBumpAction>();        // NEW (!bump)
services.AddTransient<ICommandAction, SongRaffleAction>();      // NEW (!raffle start|draw|cancel)
services.AddTransient<ICommandAction, SongRaffleEnterAction>(); // NEW (viewer raffle entry)

// Event→feed handlers (NEW — subscribe NowPlayingFeed to TrackChangedEvent + SongRequestQueueChangedEvent)
services.AddScoped<IEventHandler<TrackChangedEvent>, NowPlayingFeedHandler>();
services.AddScoped<IEventHandler<SongRequestQueueChangedEvent>, NowPlayingFeedHandler>();
```
> Remove the obsolete singleton `MusicService` registration tied to the in-memory `Dictionary` queue if present — the persistence-backed service is **Scoped**. Drop the `_db.Services` provider lookup; `MusicService` resolves providers through `IMusicProviderRegistry` over `IntegrationConnections` (`Provider`/`Status=connected`), routing requests only to providers in `SongRequestQueues.EnabledProviders` whose `Capabilities` include `AcceptsSongRequests` (a now-playing-only provider is never picked), and using `ProviderPriority` solely for ambiguous-request preference + cross-resolve (§3.1). Playback itself is owned by `ISongRequestSequencer` (§3.5.2) — it drip-feeds the head to Spotify (never a queue dump) or drives the YouTube browser-source player, one item at a time, never both at once.

**Per-tenant ordering lock.** `RequestAsync`/`RemoveAsync`/`MoveAsync`/`ClearAsync`/`AdvanceAsync` and the bump/raffle writes (`ISongRequestBumpService.BumpAsync`/`BumpUserAsync`/`TryConsumeBumpTokenAsync`/`DrawRaffleAsync`) assign/renumber `Position` **within the affected `PriorityBand`** under the per-tenant lock defined by schema §1.4 (`TenantSequences` row `(BroadcasterId, "sr_position")` read-incremented in the same transaction; `SELECT … FOR UPDATE` on Postgres, `BEGIN IMMEDIATE` on SQLite). Use the existing `IUnitOfWork` transaction (`BeginTransactionAsync`/`CommitTransactionAsync`) — the raffle entry/draw and cancel-refund are multi-write and wrapped all-or-nothing (rollback on failure), per the UoW transaction-boundary rule.

---

## 8. Dependencies (from the stack doc)

| Dependency | Party | Use here |
|---|---|---|
| `Microsoft.EntityFrameworkCore` (+ `.Sqlite` / `Npgsql.EntityFrameworkCore.PostgreSQL` via profile adapter) | 2nd / 3rd-accepted | Persist L.4/L.5/L.6 + E.5 `MusicProviderConfig`; `[VC:JSON]` `ProviderPriority` and `MusicProviderConfig.ProviderSettings` via hand-rolled `ValueConverter` + `ValueComparer` (NOT `jsonb`/`ToJson`). |
| `Microsoft.AspNetCore.SignalR` (+ `.StackExchangeRedis` SaaS backplane, `.Protocols.MessagePack`) | 2nd | `INowPlayingFeed` over `IHubContext<OverlayHub, IOverlayClient>` → now-playing widget feed + SR-page live updates. |
| `Microsoft.Extensions.Http.Resilience` (Polly v8) | 2nd | Spotify/YouTube Helix/Web-API `HttpClient` retry/breaker (existing `AddSpotifyResilienceHandler`). |
| `System.Security.Cryptography` (RNG) | 1st in-box | Mint opaque SR-page token (`RandomNumberGenerator` → base64url, same posture as `OverlayToken`); unbiased random winner selection for the song-bump raffle draw (§3.11). |
| Economy `ICatalogService` / `CatalogPurchases` (K.11) | in-repo seam | Channel-point debit for raffle entry — the same `CatalogPurchaseId` link the paid lane uses (§3.8); no parallel spend path, no new economy primitive (none existed). Refund on raffle cancel via the economy refund path. |
| `Newtonsoft.Json` | per project rule | App-level JSON serialization for `[VC:JSON]` columns / config blobs (project mandates Newtonsoft for app JSON). |
| In-box `Result<T>`, `IEventBus`, `IApplicationDbContext`, `IUnitOfWork`, `ICommandAction` | 1st | Existing app primitives — reused, not re-created. |

No new third-party dependency is introduced by this subsystem. Provider token decrypt/refresh is delegated to the integrations token vault (Domain E), not handled here.

---

## 9. Decisions (resolved)

1. **SR-page token storage shape (§3.7).** The LOCKED schema carries `Channels.SongRequestPageToken string(64) Null Unique` (A.2), mirroring `OverlayToken`. The token lives in that column; there is no separate `SongRequestPageTokens` table.
2. **`TrackInfo` explicit/age/embeddable flags (§3.5).** `TrackInfo` gains `ProviderTrackId`, `IsExplicit`, `IsAgeRestricted`, and `IsEmbeddable` as init-only properties with safe `false`/empty defaults. They are required to enforce the `MusicProviderConfig` gates (`BlockExplicit`, and the YouTube `BlockAgeRestricted`/`EmbeddableOnly` knobs in `ProviderSettings`). The safe defaults keep every existing construction site of this shared Domain type valid without change.
3. **`MusicProviderConfig.ProviderSettings` validation source of truth.** Each `IMusicProvider` impl supplies a **declarative settings schema** (key → type/range/regex) that it registers with `IMusicProviderRegistry`; `IMusicProviderRegistry.ValidateSettings(provider, json)` interprets those schemas generically. This keeps validation in-box, lets a new provider self-describe its knobs without touching `ValidateSettings`, and lets the dashboard render a generic settings form from the schema without provider-specific UI code. The table and interface shapes are unchanged — `ValidateSettings` is the single contract regardless.
4. **YouTube trust gate is a configurable floor + role bypass, not fixed tier bands (§3.1/§3.3/§3.9).** YouTube requests gate on the broadcaster-configurable `SongRequestQueues.MinYouTubeTrustScore` (L.4, renamed from `MinTrustScore`); a request auto-approves when the requester's effective community standing is `Vip`/`Moderator`-and-above (role bypass) or the recomputed Bamo score is at/above the floor, else `reject(trust_too_low)`. `MinYouTubeTrustScore = null` means no YouTube floor. **Spotify is never trust-gated** (inherently safer source). Bamo's trust-score tuning (§3.9 weights/decays/follow penalty/YouTube quality penalties/violation penalties) is **kept exactly as-is**; the `TrustTier` table is **informative defaults** (labels + the seeded default floor `Standard`/51), not the operative gate.
5. **Queue-jump / priority placement is opt-in, OFF by default (§3.1/§3.8).** The fair rank-based queue admits no paid lane unless the broadcaster enables one. When enabled, jumping is via **channel-point redeems** — a "queue-jump raffle" redeem and a "one-time bump my song" redeem — not an always-on paid tier and not a trust-tier perk. A redeemed jump sets `SongRequestItems.CatalogPurchaseId` and is placed ahead of the fair-ordered remainder; ordinary requests carry null and take fair-rank order.
6. **Provider remote/transport is capability-gated, not name-checked (§3.1/§3.5).** Previous, seek (finishes the previously half-spec'd `Seek` capability), shuffle, repeat, and transfer-device are added to `IMusicProvider` + `IMusicService`, each gated on its `MusicProviderCapabilities` flag (`Previous`/`Seek`/`Shuffle`/`Repeat`/`TransferDevice`). Spotify supports all (Premium-gated); YouTube has **no Web-API transport**, so those return `CAPABILITY_UNSUPPORTED`. The §5 action key is **`music:remote:control`** (Moderator floor — live-show control). **Spotify Premium** is surfaced as the `spotify.premium` capability (integrations-oauth §3); a transport call on a non-Premium account returns `PREMIUM_REQUIRED`, never a connect error.
7. **Per-user manage surface (library/playlists/follow/ratings/subscriptions) is a separate interface (§3.10).** `IMusicProviderManageApi` owns the broadcaster's own-account writes — distinct from the SR queue (`IMusicService`) and the playback seam (`IMusicProvider`). Generic across providers, capability-gated (`Library`/`Playlists`/`Subscriptions`). §5 action key **`music:library:write`** (Editor/Broadcaster floor — it writes the broadcaster's own provider account).
8. **AUTH STANCE (decided, §3.10).** **YouTube search** (SR queue source) rides the **app-level `YouTube:ApiKey`** — no per-user OAuth. **YouTube per-user manage** (`videos.rate`/subscriptions/playlist writes) uses the **`youtube` OAuth scope** (scope-set `youtube.manage`). **Spotify is always per-user OAuth**; transport additionally needs **Premium** (a capability, not a connect error). **The connect/scope-set flow is owned by `integrations-oauth.md`** (descriptors, seeded scope-sets `spotify.playback`/`spotify.library`/`youtube.manage`/`youtube.readonly`, PKCE, progressive re-auth) — this spec references it and never duplicates it; a manage call lacking the scope returns `MISSING_SCOPE` and the re-auth is initiated through integrations-oauth.
9. **Drip-feed remote control, NOT a queue dump (§3.5.2).** We hold the authoritative fair queue and push **only the current head** to Spotify (play-track on the active/locked device), poll for track-end, then push the next. We do **not** dump the queue into Spotify's native queue because Spotify's **"Add to Queue" endpoint is APPEND-ONLY** (no reorder, no remove) — dumping would destroy fair ordering, trust gating, and queue-jump. YouTube plays through our **browser-source player** (StreamElements parity; track-end via IFrame `onStateChange(ENDED)`). The two providers interleave in ONE fair queue, driven by `ISongRequestSequencer` one item at a time.
10. **Mutual exclusion / no Spotify-autoplay bleed (§3.5.2).** Spotify's account-level "Autoplay" (recommendations after the queue empties) **cannot be disabled via API**, so silence is enforced actively by four rules: (1) stop-and-verify the outgoing engine (`is_playing=false`) before starting the incoming one on every advance; (2) pre-load Spotify's native "next" only when the upcoming item is also Spotify, else leave it empty; (3) an autoplay watchdog pauses Spotify the instant it is seen playing a track that is not our expected current item (slipped through the ~1–2s gap); (4) empty queue ⇒ pause Spotify. Never both engines at once.
11. **Playable-head rule — a down provider never blocks the queue (§3.5.2).** The sequencer always plays the highest-fair-ranked item that is **playable right now**; items on a `waiting`/`retrying` provider are **skipped but keep their fair position** (no removal, no rank loss, no penalty) and play at their rank once the provider recovers — after the current song finishes, never interrupting. If all remaining items are on the down provider, the queue idles in `waiting`.
12. **Failure taxonomy — waiting vs retrying vs removed (§3.5.2, L.5).** `waiting` = provider/environmental outage (Spotify not open / no device / provider down): **indefinite**, skipped by the playable-head rule, never auto-removed, consumes no retries. `retrying` = a per-item **transient** error on a **healthy** provider (5xx/429/network blip/our exception/device dropped mid-push): bounded exponential backoff (`RetryCount`/`NextRetryAt`, ~3 attempts) then remove + notify. **removed (immediate)** = permanent/content error (not found, private/deleted, region-locked, `IsEmbeddable=false`, blocked age-restricted/explicit, over max duration): no retry, notify with the reason. A provider OUTAGE never burns the retry budget — only per-item errors on a healthy provider do.
13. **YouTube ad-strip — deliberate, ToS-gray, reversible (§3.5).** Our browser-source YouTube player blocks the YouTube ad-network requests (ads arrive as separate requests — strippable, unlike Spotify which streams ads inline). `SongRequestQueues.StripYouTubeAds` defaults **true**, per-channel toggle. This is a **deliberately accepted ToS-gray area**: it is long-standing industry practice (StreamElements has stripped YouTube ads in its SR player for years) and is **reversible** — the toggle can default off / be removed if YouTube objects.
14. **Tiered allowances + a separate paid lane (§3.1/§3.8, L.4).** The flat `MaxPerUser` is **removed**; the free lane is a per-standing CONCURRENT-PENDING cap (`PendingLimits`: count of unplayed items the requester owns) — defaults Everyone 2, Subscriber T1/T2/T3 4/4/4 (per-tier overridable), VIP 10, Moderator/Broadcaster unlimited (null sentinel). The **paid lane is separate and OFF by default**: channel-point requests count against `PaidPendingLimit` (independent of the free cap) via two opt-in redeems — `extra-slot` (adds a request at normal fair position, bypassing the free cap, `PaidExtraSlotEnabled`) and `queue-jump` (priority placement, `QueueJumpEnabled`); both link the redemption via `CatalogPurchaseId`. A `PerStreamLimit` (lifetime-in-stream per user, off by default) and separate free/paid `MaxDuration*Seconds` (defaults 360s / 600s) round out the allowances; a `MinStandingToRequest` floor and the existing `SubscriberOnly` / `AllowExplicit` / provider `BlockExplicit` are retained. All streamer-overridable.
15. **Cross-resolve foreign links (§3.1, L.4).** A request whose link is from a provider other than the resolution target (`ProviderPriority` head, redefined as preferred-for-ambiguous + cross-resolve target) and whose source provider is not enabled is resolved by metadata (title/artist) and re-searched on the target provider. `CrossResolveForeignLinks` defaults **true**; no match ⇒ `reject(not_found_on_target)`. `ProviderPriority` is no longer a "first-capable" fallback walk — `EnabledProviders` (one or both) decides which providers accept requests, and the queue interleaves them.
16. **Unified bump tier — fair-within-tier ordering (§3.8, L.5 `PriorityBand`).** The single ordered queue is a three-band model — `bump` → `auto_bump` → `normal` — with `Position` ordering items WITHIN a band (the Bamo fair rank is computed per-band, not globally). The `bump` band holds **every** explicit bump (raffle win, `!bump`, the existing channel-point `queue-jump` redeem — the redeem's "priority prefix" IS this band) and is **fair among bumpers** (a bumper's rank = their existing pending bumps + 1), so no single bumper monopolizes it. The restart-safe Position rebuild runs per-band. This also reconciles the prior queue-jump "priority prefix" description with the band model — they are one mechanism.
17. **Song-bump raffle — SR-owned, no existing primitive to reuse (§3.11, L.7/L.8/L.9).** The design corpus had **no** raffle/giveaway/draw primitive (economy's "raffle" is the colloquial name of a `queue-jump` redeem; economy games are gambling, not random-winner draws), so the raffle is **SR-owned** (`ISongRequestBumpService` + L.7 `SongRequestRaffles`/L.8 `SongRequestRaffleEntries`/L.9 `SongRequestBumpTokens`). It does **not** invent a parallel spend path: paid entry **reuses the economy `CatalogPurchases` debit** (K.11) via `CatalogPurchaseId`, exactly like the `queue-jump` redeem. Config (`RaffleEnabled`/`RaffleEntryCost`/`RaffleTicketsPerUser`/`RaffleWinnerCount`/`RaffleIntervalMinutes`, L.4) is **all OFF/default** (raffle off, cost 0, 1 ticket per user — **fairness over pay-to-win**, 1 winner, manual-only). Draw picks `RaffleWinnerCount` random winners (in-box `RandomNumberGenerator`, ticket-weighted); a winner's queued song moves to the `bump` band at fair rank, and a winner with **no** queued song gets a persisted **bump token** (L.9) auto-consumed on their next request. `!raffle` (mod/broadcaster) or the `RaffleIntervalMinutes` cadence triggers it; entry/draw/cancel are multi-write and wrapped in `IUnitOfWork` (all-or-nothing, cancel refunds via the economy refund path).
18. **Auto-bump first song — each user's first song of the stream, off by default (§3.8, L.4 `AutoBumpFirstSong`).** When enabled, a requester's **first** song this stream lands in the `auto_bump` band — **above** the regular fair queue, **below** every explicit bump — fair among the auto-bumped first-songs; their subsequent songs go to the `normal` band. Default **OFF**. "First song this stream" detection **reuses the per-stream-per-user request tracking the `PerStreamLimit` feature relies on** (per-stream count 0 = first request); **no second counter** is introduced.
19. **Bump command actor model — Moderator+ default, viewer-paid via redeem only (§3.11/§6).** `!bump <user|songId>` is **Moderator + Broadcaster** (`music:queue:moderate` floor, command role config) — matching StreamElements (its song-control commands default Moderator+) plus the existing queue-moderation floor. It is a human override placing a song in the `bump` band at fair rank. Viewer-paid bumping stays the existing channel-point `queue-jump` redeem — there is **no free viewer bump command and no paid command alias**. Raffle moderation (`!raffle` start/draw/cancel) reuses the same `music:queue:moderate`; viewer raffle entry reuses `music:request:submit` (gated by `RaffleEnabled`). **No new action keys** were introduced.
20. **`SongRequestQueueChangedEvent.ChangeKind += bumped` + a raffle event (§2).** A bump IS a queue-structure reorder, so `ChangeKind` gains `bumped` (this resolves the previously-flagged gap). A new `SongRequestRaffleEvent` (Phase `started`\|`drawn` with winners) lets overlays celebrate the raffle and the feed mirror it over SignalR. `SongRequestItems.BumpSource` (`raffle`\|`command`\|`redeem`) records bump provenance for display/analytics.
21. **Trust scoring is advanced-configurable per modifier; metric base stays fixed (§3.9, L.4 `TrustScoringConfig`).** The **core metric base** — the four metric scores (`requestScore`/`accountScore`/`contentScore`/`popularityScore`), their weights (0.25/0.25/0.30/0.20) and decay constants (0.599/0.499/0.999/0.0003) — is **intentionally NOT configurable**; it is Bamo's fixed foundation. Each **buff/debuff** on top of it (reputation boost, follow penalty, the YouTube channel-quality penalty group, skip/timeout/ban penalties) is **individually toggleable + tunable** via the per-channel `TrustScoringConfig` (L.4 `[VC:JSON]` column). **Every toggle defaults ON and every magnitude defaults to Bamo's current constant, so behavior is unchanged by default** — only a deliberate advanced edit alters it; a disabled modifier drops its line from the computation entirely (not a zeroed weight); validation keeps multipliers in (0,1], penalties/thresholds ≥ 0, and the final score `clamp(0,100)`. Because the config is sensitive, it rides the existing `config` GET/PUT (`music:config:write`, **Editor/Broadcaster** floor) via `MusicConfigDto`/`UpdateMusicConfigDto` — **no new route, DTO route, or action key**; the simple `MinYouTubeTrustScore` floor stays on the mod-level `queue/settings` route as before.
