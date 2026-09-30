# Interface Specification — Media Share Subsystem (viewer clip/video queue)

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` (Domain L — media/economy-adjacent; music-sr owns L.4–L.9); music `music-sr.md` (queue/eligibility/cost patterns — mirrored, not shared); Twitch `twitch-helix.md` (`ITwitchClipsApi` clip metadata); integrations `integrations-oauth.md` / `music-sr.md` (YouTube Data API for video metadata); economy `economy.md` (`ICurrencyAccountService` entry cost); widgets `widgets-overlays.md` (`IWidgetNotifier`, overlay); pipeline `commands-pipelines.md`; roles `roles-permissions.md`.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`/`PaginatedResponse<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json; UUIDv7 `Guid` PKs; `BroadcasterId` `Guid`; soft-delete filter; AGPL header on every source file.

> **Why.** "Submit a clip to play on stream" (StreamElements media-share, Lumia) is a popular interactive segment and a corpus gap. It is **distinct from music song-requests** (`music-sr.md` queues audio tracks; this queues short **video** clips that play on an overlay). Viewer-submitted video is moderation-sensitive, so the design is **safe-by-default**: pre-play mod approval on, a closed set of embeddable sources (Twitch clips + YouTube), and a hard duration cap.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Safe-by-default approval.** `RequireApproval` defaults **on** — submissions enter `pending` and a mod approves before they can play. A streamer may toggle it off (auto-`approved`) per channel. |
| D2 | **Closed source set.** `SourceType ∈ {twitch_clip, youtube}` only — parsed/validated from the URL; **no arbitrary URLs** (NSFW/embedding risk). Metadata (title, duration, thumbnail) is fetched server-side via `ITwitchClipsApi` (Twitch) / the YouTube Data API (`music-sr.md`'s provider). |
| D3 | **Hard duration cap** (`MaxDurationSeconds`, default 180, tier-scaled per the safety-baseline rule) — over-length submissions are rejected at submit time. |
| D4 | **Optional cost + eligibility** (opt-in): `EntryCost` loyalty points (debited via economy), and `EligibilityJson` (sub-only / min-standing / min-account-age). Free + everyone by default. |
| D5 | **FIFO queue with mod control.** Approved items play in submission order; mods can approve/reject/skip/reorder. The overlay pulls the next approved item and reports playback completion. |
| D6 | **Submission paths:** a built-in `!media <url>` command and a `submit_media` pipeline action (so a channel-point redemption can be "redeem to submit a clip"). |
| D7 | **Schema additions (Domain L):** **L.10 `MediaShareConfig`**, **L.11 `MediaShareRequest`**. |

---

## 1. Entities

Domain L. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`MediaShareConfig`** | **L.10 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` **Unique** (one per channel); `IsEnabled bool`; `RequireApproval bool` (default true, D1); `AllowTwitchClips bool` (default true); `AllowYouTube bool` (default true); `MaxDurationSeconds int` (default 180, D3); `EntryCost long?` (points; null/0 = free); `EligibilityJson text?` **[VC:JSON]**; `MaxQueueLength int` (default 20); `PerUserCooldownSeconds int` (default 60); `ConfigSchemaVersion int`; `CreatedAt/UpdatedAt/DeletedAt`. |
| **`MediaShareRequest`** | **L.11 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK Index; `RequesterUserId Guid` FK→`Users.Id`; `RequesterTwitchUserId string(50)` **[PII-hash]**; `SourceType string(20)` **[VC:enum]** (`twitch_clip`\|`youtube`); `SourceUrl string(2048)`; `MediaRef string(255)` (clip slug / YouTube id); `Title string(300)?`; `DurationSeconds int`; `ThumbnailUrl string(2048)?`; `Status string(20)` **[VC:enum]** (`pending`\|`approved`\|`rejected`\|`playing`\|`played`\|`skipped`); `QueuePosition int?`; `EntryCostLedgerEntryId long?`; `RequestedAt DateTime`; `DecidedAt DateTime?`; `DecidedByUserId Guid?`; `CreatedAt/UpdatedAt/DeletedAt`. **Index** `(BroadcasterId, Status, QueuePosition)`. |

---

## 2. Domain events

Inherit `DomainEventBase` (platform-conventions §2.0). Published via `IEventBus`.

```csharp
namespace NomNomzBot.Domain.MediaShare.Events;

public sealed class MediaShareSubmittedEvent : DomainEventBase
{
    public required Guid RequestId { get; init; }
    public required Guid RequesterUserId { get; init; }
    public required string SourceType { get; init; }
    public required bool AutoApproved { get; init; }
}

public sealed class MediaSharePlaybackChangedEvent : DomainEventBase   // → dashboard (MediaSharePlaybackBroadcastHandler pushes `media_share_playback_changed` {requestId, status})
{
    public required Guid RequestId { get; init; }
    public required string Status { get; init; }   // approved | playing | played | skipped
}
```

---

## 3. Service interface

Namespace `NomNomzBot.Application.MediaShare.Services` (DTOs in `NomNomzBot.Application.MediaShare.Dtos`). Returns `Task<Result<T>>`. Impl in `NomNomzBot.Infrastructure/MediaShare/`.

```csharp
public interface IMediaShareService
{
    // Validates the URL → SourceType (D2), fetches metadata (ITwitchClipsApi / YouTube), enforces duration cap (D3),
    // eligibility + per-user cooldown + queue length (D4/D5), debits EntryCost (spend_media) when set; enqueues
    // pending (or approved if RequireApproval off); publishes MediaShareSubmittedEvent. Fails closed with a stable code.
    Task<Result<MediaShareRequestDto>> SubmitAsync(Guid broadcasterId, Guid requesterUserId, SubmitMediaRequest request, CancellationToken ct = default);

    Task<Result<MediaShareRequestDto>> ApproveAsync(Guid broadcasterId, Guid requestId, Guid moderatorUserId, CancellationToken ct = default);  // → approved + appends to play order
    Task<Result<MediaShareRequestDto>> RejectAsync(Guid broadcasterId, Guid requestId, Guid moderatorUserId, CancellationToken ct = default);   // → rejected (refunds EntryCost if charged); returns the updated request
    Task<Result<MediaShareRequestDto>> SkipAsync(Guid broadcasterId, Guid requestId, CancellationToken ct = default);                           // playing/approved → skipped (refunds EntryCost if charged); returns the updated request
    Task<Result<MediaShareRequestDto>> ReorderAsync(Guid broadcasterId, Guid requestId, int newPosition, CancellationToken ct = default);      // approved item → 1-based play position; returns the moved request

    Task<Result<PagedList<MediaShareRequestDto>>> GetQueueAsync(Guid broadcasterId, MediaShareFilter filter, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<MediaShareRequestDto?>> GetNextAsync(Guid broadcasterId, CancellationToken ct = default);          // overlay pulls the next approved item → playing
    Task<Result<MediaShareRequestDto>> MarkPlayedAsync(Guid broadcasterId, Guid requestId, CancellationToken ct = default);   // overlay reports completion → played, advances; returns the updated request

    Task<Result<MediaShareConfigDto>> GetConfigAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<MediaShareConfigDto>> UpdateConfigAsync(Guid broadcasterId, UpdateMediaShareConfigRequest request, CancellationToken ct = default);
}

public sealed record SubmitMediaRequest(string Url);
public sealed record ReorderMediaRequest(int Position);
public sealed record MediaShareRequestDto(Guid Id, Guid RequesterUserId, string RequesterDisplayName, string? RequesterAvatarUrl, string SourceType, string SourceUrl, string MediaRef, string? Title, int DurationSeconds, string? ThumbnailUrl, string Status, int? QueuePosition, DateTime RequestedAt);
public sealed record MediaShareFilter(string? Status);
public sealed record MediaShareConfigDto(bool IsEnabled, bool RequireApproval, bool AllowTwitchClips, bool AllowYouTube, int MaxDurationSeconds, long? EntryCost, int MaxQueueLength, int PerUserCooldownSeconds);
public sealed record UpdateMediaShareConfigRequest(bool IsEnabled, bool RequireApproval, bool AllowTwitchClips, bool AllowYouTube, int MaxDurationSeconds, long? EntryCost, int MaxQueueLength, int PerUserCooldownSeconds);
```

`RequesterDisplayName` / `RequesterAvatarUrl` are joined live from `Users` at read time (never stored on the request row), so the mod queue always shows the current Twitch name and avatar.

URL parsing + metadata sit behind `IMediaSourceResolver.ResolveAsync(url, allowTwitchClips, allowYouTube, ct)` → `Result<ResolvedMedia(SourceType, MediaRef, Title, DurationSeconds, ThumbnailUrl)>` (`MediaSourceResolver`, registered explicitly in `AddInfrastructure`; it is not an `I<X>Service`).

**Economy delta (owner `economy.md`):** `EntryType` gains `SpendMedia` (`spend_media`, entry cost) and `RefundMedia` (`refund_media`, rejected/skipped refund).

---

## 4. Built-in command, pipeline action, overlay

- **Built-in `!media`** (`IBuiltinCommand`, `BuiltinKey="media"`): `!media <url>` → `SubmitAsync` for the caller; replies with queued/needs-approval status. Default min-permission `Everyone` (per-channel overridable); honors eligibility + cooldown.
- **Pipeline action `submit_media`** (`ICommandAction`): config `url` (template) → `SubmitAsync` for the triggering viewer — so a channel-point redemption can require a clip URL and submit it.
- **Overlay:** the first-party **`media_share`** widget is **NOT BUILT** (tracked as S104 in `SHORTCOMINGS-EXECUTION-PLAN.md`); there is no `media_share` entry in the widget catalogue and no `media.play|next|done` widget events. The backend half is complete: the queue is consumed through `GET /media-share/next` (pulls the next approved item, flips it to `playing`) and `POST /media-share/{id}/played` (reports completion, advances the queue), and `MediaSharePlaybackChangedEvent` reaches the dashboard as the `media_share_playback_changed` push. The widget, when built, plays the current approved clip (Twitch clip embed / YouTube iframe), shows the upcoming queue, and drives itself from those two endpoints.

---

## 5. REST surface

Controller `MediaShareController`, `[Route("api/v{version:apiVersion}/media-share")]`. `[Authorize]`; Gate-2 keys.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/queue` | `MediaShareFilter`+`PageRequestDto` | `PaginatedResponse<MediaShareRequestDto>` | management / Moderator · `media:read` |
| GET | `/next` | — | `StatusResponseDto<MediaShareRequestDto>` | management / Moderator · `media:read` |
| POST | `/{id}/approve` | — | `StatusResponseDto<MediaShareRequestDto>` | management / Moderator · `media:moderate` |
| POST | `/{id}/reject` | — | `StatusResponseDto<MediaShareRequestDto>` | management / Moderator · `media:moderate` |
| POST | `/{id}/skip` | — | `StatusResponseDto<MediaShareRequestDto>` | management / Moderator · `media:moderate` |
| POST | `/{id}/reorder` | `ReorderMediaRequest(Position)` (1-based) | `StatusResponseDto<MediaShareRequestDto>` | management / Moderator · `media:moderate` |
| POST | `/{id}/played` | — | `StatusResponseDto<MediaShareRequestDto>` (the overlay reports completion → `played`, the queue advances) | management / Moderator · `media:moderate` |
| GET | `/config` | — | `StatusResponseDto<MediaShareConfigDto>` | management / Moderator · `media:read` |
| PUT | `/config` | `UpdateMediaShareConfigRequest` | `StatusResponseDto<MediaShareConfigDto>` | management / Moderator · `media:write` |

`{id}` is a `Guid` route constraint; the tenant comes from `ICurrentTenantService`. `GET /next` returns the item already `playing` if there is one (it never advances past it), else flips the next approved item (lowest `QueuePosition`, then oldest) to `playing`; an empty queue returns success with null data. Seeded in `ActionDefinitionSeeder`: `media:read`, `media:moderate` **and `media:write`** all default to Moderator (S-MOD-PERMS — the config is reversible bot-internal tooling); a channel may raise any of them per its own floors.

---

## 6. DI & testing

Registered in `NomNomzBot.Infrastructure/DependencyInjection.cs` (there is no `MediaShare/DependencyInjection.cs` and no `AddMediaShare()`): `IMediaShareService` → `MediaShareService` (Scoped, bound by the `I<X>Service` convention scan); `IMediaSourceResolver` → `MediaSourceResolver` (Scoped, explicit); `MediaBuiltin` (`IBuiltinCommand`, Scoped, explicit) and `SubmitMediaAction` (`ICommandAction`, auto-discovered). No repositories: the service uses `IApplicationDbContext`. Metadata via the existing `ITwitchClipsApi` + YouTube Data provider; no new external client.

**Tests (prove behavior):** a valid Twitch-clip URL resolves `SourceType=twitch_clip` + real `DurationSeconds`/`Title` and enqueues `pending` (or `approved` when `RequireApproval` off); an **over-cap** clip is rejected (`DURATION_EXCEEDED`); a **non-allowlisted** URL is rejected (`SOURCE_NOT_ALLOWED`); `EntryCost` is debited on submit and **refunded** on reject/skip (`refund_media`); per-user cooldown + `MaxQueueLength` enforced; `GetNextAsync` returns approved items in FIFO/`QueuePosition` order and flips the item to `playing`, `MarkPlayedAsync` advances; eligibility gate rejects an ineligible viewer.

---

## 7. Decisions (resolved)

Safe-by-default approval (D1); closed source set Twitch-clip + YouTube, server-fetched metadata (D2); hard duration cap, tier-scaled (D3); optional cost + eligibility (D4); FIFO + mod control (D5); `!media` + `submit_media` submission (D6); schema deltas L.10 `MediaShareConfig` + L.11 `MediaShareRequest`, economy `EntryType` `spend_media`/`refund_media` (D7).
