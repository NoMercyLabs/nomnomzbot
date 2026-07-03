# Interface Specification — Sound System (sound clips & playback)

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** Streamer.bot's sound sub-actions (Play Sound / Play Sound From Folder / Stop — the ecosystem reference). Corpus: `tts.md` (the audio-store + overlay-playback pattern — `ITtsAudioStore` deployment-profile abstraction, `IOverlayClient.TtsSpeak`); `widgets-overlays.md` (§7 `IOverlayClient`, `widget_event`, the always-loaded overlay as the audio output); `commands-pipelines.md` (§3.13 `ICommandAction`/`ActionContext`, §6.1 action list); `platform-conventions.md` (`IDeploymentProfileService`, `ICacheService`); `scaling-qos.md` (`IRateLimiter`); `roles-permissions.md` (Gate-2, §5 cell format); locked schema `2026-06-16-database-schema.md` (Domain P — overlay/audio media, beside TTS P.1–P.5 and widgets P.6–P.9).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>` / `PaginatedResponse<T>`; `[ApiVersion("1.0")]`; UUIDv7 `Guid` PKs; `BroadcasterId Guid` tenant scope; soft-delete filter; Newtonsoft.Json.

> **Why.** Audio is currently TTS-only. Every serious bot plays **sound clips** — alert stingers, meme SFX, hype horns — triggered by commands, redemptions, and events. This subsystem adds a per-channel **sound-clip library** (upload/manage curated clips) and a **`play_sound`** pipeline action that plays a clip on the **overlay audio bus** (the same browser-source path TTS already uses, so OBS captures it with zero extra setup). It is streamer-curated (no per-play approval queue — unlike viewer-submitted TTS), bounded by tier-scaled size/count limits.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **`play_sound` plays a library clip on the overlay audio bus.** The pipeline action resolves a clip (by id or name) and pushes a `PlaySound` payload to the always-loaded overlay (the browser source OBS already captures) via `IOverlayClient` — the **same delivery path as TTS** (`tts.md`), so no new OBS setup. Params: clip ref, `Volume` (0–100), `WaitForFinish` (the action awaits playback end before the next action), optional `Handle` (a name for targeted stop). A `stop_sound` action stops a handle or all playback. |
| D2 | **Curated library, no approval queue.** Clips are uploaded and managed by the broadcaster/editor (`SoundClip` library); playing one is gated by who can edit the pipeline that calls it. There is **no per-play moderation queue** — the library is streamer-chosen content (contrast `tts.md`, which queues viewer-submitted *text*). |
| D3 | **Durable clip storage via a deployment-profile store.** `ISoundClipStore` mirrors the `ITtsAudioStore` adapter pattern — disk on self-host, object-store on SaaS — but with **durable** retention (user uploads, not regenerable cache). The overlay fetches a clip by a tokened playback URL (the overlay-asset access pattern). |
| D4 | **Tier-scaled limits, validated formats.** Per-clip max size, per-channel total library size, and clip count are safe-baseline + tier-scaled (the limits rule). Accepted formats: `mp3`/`ogg`/`wav` (validated by content sniff, not just extension); duration is probed and stored. Uploads pass `IRateLimiter`. |
| D5 | **Schema delta P.18 `SoundClip`.** No play-log table — a play is a transient overlay push (`CommandLogEntry`/event journal already record the pipeline run). `IOverlayClient` gains `PlaySound` (parallel to `TtsSpeak`, `widgets-overlays.md` §7); `commands-pipelines.md` gains `play_sound` + `stop_sound` actions. |

---

## 1. Entities

Domain P. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`SoundClip`** | **P.18 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `Name string(50)` (slug used by `play_sound`); `DisplayName string(100)`; `StorageKey string(200)` (key in `ISoundClipStore`); `MimeType string(40)` (`audio/mpeg`\|`audio/ogg`\|`audio/wav`); `DurationMs int`; `SizeBytes long`; `DefaultVolume int` (0–100, default 80); `IsEnabled bool` (default true); `CreatedByUserId Guid` FK→`Users.Id`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, Name)`. |

---

## 2. Domain events

None new. A sound play is a side effect of a pipeline run (already journaled via `CommandLogEntry`/the pipeline-execution record). Library mutations are ordinary CRUD (audited by the standard management-audit path).

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.Sound`. `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/Sound/`.

```csharp
public interface ISoundClipService
{
    Task<Result<PagedList<SoundClipDto>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<SoundClipDto>> GetAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);

    // Validates format/size/limits, probes duration, stores the blob via ISoundClipStore, persists metadata.
    Task<Result<SoundClipDto>> UploadAsync(Guid broadcasterId, Guid actorUserId, UploadSoundClipRequest request, CancellationToken ct = default);
    Task<Result<SoundClipDto>> UpdateAsync(Guid broadcasterId, Guid id, Guid actorUserId, UpdateSoundClipRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid broadcasterId, Guid id, Guid actorUserId, CancellationToken ct = default);

    // Resolves a clip (id or name) → a tokened playback URL + effective volume for the overlay.
    Task<Result<SoundPlaybackDto>> ResolveForPlaybackAsync(Guid broadcasterId, string clipRef, int? volumeOverride, CancellationToken ct = default);

    // Plays a clip on the overlay now (dashboard preview / test).
    Task<Result> PreviewAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);
}

// Deployment-profile blob store for durable user-uploaded clips (mirrors ITtsAudioStore; disk | object-store).
public interface ISoundClipStore
{
    Task<Result<string>> PutAsync(Guid broadcasterId, string fileName, Stream content, string mimeType, CancellationToken ct = default); // returns StorageKey
    Task<Result<Stream>> OpenAsync(string storageKey, CancellationToken ct = default);
    Task<Result> DeleteAsync(string storageKey, CancellationToken ct = default);
    Task<Result<string>> GetPlaybackUrlAsync(string storageKey, CancellationToken ct = default); // tokened, overlay-fetchable
}

public sealed record UploadSoundClipRequest(string Name, string DisplayName, string FileName, string MimeType, Stream Content, int DefaultVolume);
public sealed record UpdateSoundClipRequest(string DisplayName, int DefaultVolume, bool IsEnabled);
public sealed record SoundClipDto(Guid Id, string Name, string DisplayName, string MimeType, int DurationMs, long SizeBytes, int DefaultVolume, bool IsEnabled, DateTime CreatedAt);
public sealed record SoundPlaybackDto(Guid ClipId, string PlaybackUrl, int Volume, int DurationMs);
```

---

## 4. Pipeline actions & overlay

Two `ICommandAction`s (canonical contract, `commands-pipelines.md §3.13). Register in `NomNomzBot.Infrastructure/Sound/PipelineActions/`.

| Action `Type` | Parameters | Behavior |
|---|---|---|
| **`play_sound`** | `{ string Clip (id or name), int? Volume, bool WaitForFinish, string? Handle }` | `ISoundClipService.ResolveForPlaybackAsync` → `IOverlayClient.PlaySound`. If `WaitForFinish`, the action awaits `DurationMs` (capped) before completing. Unknown/disabled clip → typed action failure (no throw). |
| **`stop_sound`** | `{ string? Handle, bool All }` | Pushes a stop to the overlay for the named handle, or all playback when `All`. |

**Overlay (`widgets-overlays.md` §7):** add `IOverlayClient.PlaySound(PlaySoundPayload)` (parallel to `TtsSpeak`). `PlaySoundPayload = (string PlaybackUrl, int Volume, string? Handle)`. The always-loaded overlay holds the `<audio>` element and plays/stops on the pushed payload; plays overlap by default (each is independent), `WaitForFinish` serializes at the **pipeline** level, not the overlay.

---

## 5. REST surface

Controller `SoundClipsController`, `[Route("api/v{version:apiVersion}/sound-clips")]`. `[Authorize]`; Gate-2 keys. Upload is `multipart/form-data`.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/` | — | `PaginatedResponse<SoundClipDto>` | management / Moderator · `sounds:read` |
| GET | `/{id}` | — | `StatusResponseDto<SoundClipDto>` | management / Moderator · `sounds:read` |
| POST | `/` | multipart (`UploadSoundClipRequest`) | `StatusResponseDto<SoundClipDto>` | management / Editor · `sounds:write` |
| PUT | `/{id}` | `UpdateSoundClipRequest` | `StatusResponseDto<SoundClipDto>` | management / Editor · `sounds:write` |
| DELETE | `/{id}` | — | `StatusResponseDto<bool>` | management / Editor · `sounds:write` |
| POST | `/{id}/preview` | — | `StatusResponseDto<bool>` | management / Editor · `sounds:write` |

Seed in `roles-permissions.md`: **`sounds:read`** (`management`, Moderator 10, `Low`), **`sounds:write`** (`management`, Editor 30, `Low`).

---

## 6. DI & testing

`NomNomzBot.Infrastructure/Sound/DependencyInjection.cs` (`AddSound()`): `ISoundClipService`→`SoundClipService` (Scoped); `SoundClipRepository` (Scoped); `ISoundClipStore`→`DiskSoundClipStore` / `ObjectStoreSoundClipStore` selected by `IDeploymentProfileService.Current` (the `ITtsAudioStore` selection pattern); `play_sound` + `stop_sound` actions auto-discovered into the pipeline action registry. `IOverlayClient.PlaySound` is implemented in the overlay client (`widgets-overlays.md`).

**Tests (prove behavior):** uploading a valid `mp3` stores the blob, probes a non-zero `DurationMs`, and persists metadata; an oversized clip or a file whose **content** isn't audio (extension spoofed) is rejected and **nothing is stored**; a library that would exceed the tier total-size cap rejects the upload; `play_sound` by name resolves the clip and pushes exactly one `PlaySound` to the overlay with the effective volume (clip default unless overridden), and an unknown/disabled clip yields a typed failure with **no overlay push**; `WaitForFinish` makes the action's completion follow the (capped) duration so a following `send_message` runs after; `stop_sound` with a handle pushes a targeted stop, `All` stops everything; deleting a clip removes both the row and the stored blob; upload rate-limit denial performs no store write.

---

## 7. Decisions (resolved)

`play_sound`/`stop_sound` over the overlay audio bus, same delivery as TTS (D1); curated library, no per-play approval (D2); durable deployment-profile `ISoundClipStore`, tokened playback URL (D3); tier-scaled size/count limits + content-sniffed format validation (D4); schema delta **P.18 `SoundClip`**, `IOverlayClient.PlaySound` + two pipeline actions, no play-log table (D5).
