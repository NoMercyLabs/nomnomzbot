# Interface Specification — Music Automation Controls (silent transport/library actions + read surface)

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** `music-sr.md` §3.5 (`IMusicProvider`, `MusicProviderCapabilities`), §3.10 (`IMusicProviderManageApi`) — both entirely reused, zero new provider methods; `commands-pipelines.md` §3.13 (`ICommandAction` canonical contract); `automation-api.md` (§3 `IAutomationCommandService`, `AutomationPrincipal`, scopes `invoke`/`read`/`events`, `IAutomationEventDescriptor`/`IAutomationEventRegistry` D6 auto-discovery); `stream-deck.md` (the pairing handshake this rides — unchanged, reused as-is); `widget-sdk.md` §9 (`song.changed` position-anchor payload shape, reused verbatim for the automation event); `roles-permissions.md` (Gate-2, `DangerTier`).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json.

> **Why.** The existing `song_*` pipeline actions (`music-sr.md` §6: `song_request`, `song_skip`, `song_current`, `song_volume`, `song_previous`, `song_seek`) are viewer-chat-command flavored — every one posts a chat reply. A Stream Deck key (or any silent automation client) firing `PlayPause` or polling favorite-state every few seconds cannot use those without spamming chat. This spec adds a **second, silent action family** over the same already-built `IMusicProvider`/`IMusicProviderManageApi` seams — no new provider capability, no new schema, purely a fire-and-forget pipeline-action wrapper plus a typed read/event surface on the Automation API so a key can show live state (elapsed time, shuffle/repeat, saved) without polling. Built to satisfy full first-party parity with third-party Spotify control surfaces (BarRaider's Stream Deck plugin) and beyond — every `IMusicProvider`/`IMusicProviderManageApi` member gets an action, not a curated subset.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Silent action family, separate from `song_*`.** New file `Infrastructure/Pipeline/Actions/MusicControlActions.cs`, `Category = "Music Control"`, each implementing the canonical `ICommandAction`. None post a chat reply — result surfaces only via the caller (pipeline execution result / `AutomationInvokeResult`). Existing `song_*` actions are untouched. |
| D2 | **Both discrete and toggle/cycle actions ship — no curation.** Every `IMusicProvider`/`IMusicProviderManageApi` member that makes sense as a single fire gets a discrete action (`music_play`, `music_pause`, `music_save_track`, …) **and**, where the member has an observable on/off/mode state, a convenience toggle/cycle wrapper (`music_play_pause`, `music_toggle_shuffle`, `music_cycle_repeat`, `music_toggle_saved`) that reads current state via `GetCurrentTrackAsync`/`AreTracksSavedAsync` then flips it. A pipeline (or a Stream Deck key) picks whichever fits — this is the full hook surface, not a subset. |
| D3 | **Read surface added to `IAutomationCommandService`, scope `read`.** `GetNowPlayingAsync`, `GetDevicesAsync`, `GetPlaylistsAsync` — thin wrappers over `IMusicProvider.GetCurrentTrackAsync`/`GetDevicesAsync` and `IMusicProviderManageApi.ListPlaylistsAsync`. Needed for: (a) a key's *initial* paint before the first event arrives, (b) a device/playlist picker in a client's property-inspector-equivalent UI. Same `CAPABILITY_UNSUPPORTED` failure shape as every other music surface. |
| D4 | **`song.changed` becomes a public automation event.** Register `SongChangedAutomationEventDescriptor : IAutomationEventDescriptor` (auto-discovered, `automation-api.md` D6) wrapping the domain event that already drives the overlay now-playing widget (`widget-sdk.md` §9). Payload is the same position-anchor shape (`title, artist, durationMs, positionMs, isPlaying, serverTime`) **plus `isSaved: bool`** (new field — the favorite-toggle key needs it and nothing upstream emits it today) and `shuffleEnabled`/`repeatMode` (already on `TrackInfo`, just not on the wire payload). Automation clients subscribe once (scope `events`) and extrapolate position locally exactly like overlay widgets do — no per-second polling from any client, Stream Deck included. |
| D5 | **Gate-2:** new action key `music:control:write` — one seeded floor, **Broadcaster** (every action here operates the broadcaster's own connected Spotify/YouTube account); a broadcaster delegates it per channel via `ChannelActionOverride` (lowering to Moderator or granting a named user), never a second seeded floor. Governs which `AllowedPipelineIds`/scopes a management-plane token mint can grant; the existing `automation:tokens:write` still governs minting the token itself. Read methods ride existing scope `read` — no new key needed for reads. |
| D6 | **Schema: none.** Every action/read is a wrapper over already-schema'd surfaces (`music-sr.md`, `automation-api.md` P.17). No new tables, no new domain events beyond the existing `song.changed` source event gaining two extra projected fields on its **public** (automation) payload — the internal domain event itself is unchanged, only `SongChangedAutomationEventDescriptor.ProjectPayload` adds `isSaved`/`shuffleEnabled`/`repeatMode`. |

---

## 1. Entities

**None.** Zero new tables — see D6.

---

## 2. Domain events

**None new.** `SongChangedAutomationEventDescriptor` (D4) is an `IAutomationEventDescriptor` over the existing now-playing domain event (`widget-sdk.md` §9's source), not a new `DomainEventBase` subtype. Its `ProjectPayload` calls `IMusicProviderManageApi.AreTracksSavedAsync` (single-track) to resolve `isSaved` at projection time — cheap, capability-gated (`Library` absent ⇒ `isSaved: null`, never a failed projection).

---

## 3. Service interfaces

### 3.1 Pipeline actions — `NomNomzBot.Infrastructure.Pipeline.Actions.MusicControlActions`

Each `: ICommandAction` (`commands-pipelines.md` §3.13: `string Type`, `string Category`, `string Description`, `Task<ActionResult> ExecuteAsync(ActionContext context, JsonElement parameters)`). `Category = "Music Control"`. All resolve the broadcaster's active `IMusicProvider` the same way `song_*` does; all fail `CAPABILITY_UNSUPPORTED` when the required `MusicProviderCapabilities` flag is absent, `PREMIUM_REQUIRED` on the same Spotify-transport 403 path `song_*` already surfaces, `MISSING_SCOPE` when the provider connection lacks the needed OAuth scope. None post to chat.

| `Type` | Params | Capability required | Calls |
|---|---|---|---|
| `music_play` | `{}` | `PlaybackControl` | `PlayAsync` |
| `music_pause` | `{}` | `PlaybackControl` | `PauseAsync` |
| `music_play_pause` | `{}` | `PlaybackControl`, `NowPlaying` | `GetCurrentTrackAsync().IsPlaying` → `PauseAsync`/`PlayAsync` |
| `music_next` | `{}` | `Skip` | `SkipAsync` |
| `music_previous` | `{}` | `Previous` | `PreviousAsync` |
| `music_set_volume` | `{ volume: int 0-100 }` (supports `{var}`) | `Volume` | `SetVolumeAsync` |
| `music_seek` | `{ positionSeconds: int }` (supports `{var}`) | `Seek` | `SeekAsync` |
| `music_set_shuffle` | `{ enabled: bool }` | `Shuffle` | `SetShuffleAsync` |
| `music_toggle_shuffle` | `{}` | `Shuffle`, `NowPlaying` | reads `TrackInfo.ShuffleEnabled` → `SetShuffleAsync(!current)` |
| `music_set_repeat` | `{ mode: "off"\|"track"\|"context" }` | `Repeat` | `SetRepeatAsync` |
| `music_cycle_repeat` | `{}` | `Repeat`, `NowPlaying` | reads `TrackInfo.RepeatMode` → advances `Off→Track→Context→Off` → `SetRepeatAsync` |
| `music_transfer_device` | `{ deviceId: string }` (supports `{var}`) | `TransferDevice` | `TransferPlaybackAsync(deviceId, play: true)` |
| `music_save_track` | `{}` | `Library`, `NowPlaying` | current track's `TrackUri` → `SaveTracksAsync([uri])` |
| `music_unsave_track` | `{}` | `Library`, `NowPlaying` | current track's `TrackUri` → `RemoveSavedTracksAsync([uri])` |
| `music_toggle_saved` | `{}` | `Library`, `NowPlaying` | `AreTracksSavedAsync([uri])[0]` → `RemoveSavedTracksAsync`/`SaveTracksAsync` |
| `music_add_to_playlist` | `{ playlistId: string }` (supports `{var}`) | `Playlists`, `NowPlaying` | current track's `TrackUri` → `AddPlaylistTracksAsync(playlistId, [uri])` |
| `music_remove_from_playlist` | `{ playlistId: string }` (supports `{var}`) | `Playlists`, `NowPlaying` | current track's `TrackUri` → `RemovePlaylistTracksAsync(playlistId, [uri])` |
| `music_follow_artist` | `{}` | `Library`, `NowPlaying` | current track's primary artist id → `FollowAsync(target: Artist, artistId)` |
| `music_unfollow_artist` | `{}` | `Library`, `NowPlaying` | current track's primary artist id → `UnfollowAsync(target: Artist, artistId)` |

`music_*` actions needing "current track's artist id" resolve it via `TrackInfo` — if `IMusicProvider.GetCurrentTrackAsync`'s `TrackInfo` does not carry a provider artist id today, add `string? ArtistId` to `TrackInfo` (one nullable field, additive, no migration — `TrackInfo` is a plain record, not an entity) rather than a second provider round-trip.

### 3.2 Read surface — extend `IAutomationCommandService` (`automation-api.md` §3)

```csharp
public interface IAutomationCommandService
{
    // ...existing InvokePipelineAsync / ListPipelinesAsync / ListCommandsAsync / GetInfoAsync / SendChatAsync...

    // Scope `read`. Capability-gated identically to the pipeline actions above.
    Task<Result<AutomationNowPlayingDto>> GetNowPlayingAsync(AutomationPrincipal principal, CancellationToken ct = default);
    Task<Result<IReadOnlyList<AutomationDeviceDto>>> GetDevicesAsync(AutomationPrincipal principal, CancellationToken ct = default);
    Task<Result<IReadOnlyList<AutomationPlaylistDto>>> GetPlaylistsAsync(AutomationPrincipal principal, int limit = 20, int offset = 0, CancellationToken ct = default);
}

public sealed record AutomationNowPlayingDto(
    string? Title, string? Artist, int DurationMs, int PositionMs,
    bool IsPlaying, bool ShuffleEnabled, string RepeatMode, bool? IsSaved,
    DateTimeOffset ServerTime
);
public sealed record AutomationDeviceDto(string Id, string Name, string Type, bool IsActive, int? VolumePercent);
public sealed record AutomationPlaylistDto(string Id, string Name, string Uri, int TrackCount, string? ImageUrl);
```

`GetNowPlayingAsync` maps `IMusicProvider.GetCurrentTrackAsync` + one `AreTracksSavedAsync` call (same projection `SongChangedAutomationEventDescriptor` uses — factor the mapping into one shared internal helper, `MusicAutomationProjection`, so the REST read and the event payload can never drift). `GetDevicesAsync`/`GetPlaylistsAsync` map `IMusicProvider.GetDevicesAsync`/`IMusicProviderManageApi.ListPlaylistsAsync` 1:1.

### 3.3 Event descriptor

```csharp
namespace NomNomzBot.Infrastructure.AutomationApi.Events;

public sealed class SongChangedAutomationEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "song.changed";
    public Type DomainEventType => typeof(NowPlayingChangedEvent); // the existing widget-sdk.md §9 source event
    public object ProjectPayload(DomainEventBase domainEvent) =>
        MusicAutomationProjection.ToNowPlayingPayload((NowPlayingChangedEvent)domainEvent); // shared with §3.2
}
```

---

## 4. REST surface

No new controller. Extends the existing data-plane routes (`automation-api.md` §4, `/automation/v1/*`, `IAutomationTokenAuthenticator`-authed):

| Verb | Path | Scope | Response |
|---|---|---|---|
| GET | `/automation/v1/music/now-playing` | `read` | `StatusResponseDto<AutomationNowPlayingDto>` |
| GET | `/automation/v1/music/devices` | `read` | `StatusResponseDto<IReadOnlyList<AutomationDeviceDto>>` |
| GET | `/automation/v1/music/playlists?limit=&offset=` | `read` | `StatusResponseDto<PaginatedResponse<AutomationPlaylistDto>>` |

The `music_*` pipeline actions carry no dedicated route — they run exclusively through the existing `POST /automation/v1/invoke` (and the ordinary in-dashboard pipeline editor/manual trigger, same as any other `ICommandAction`).

---

## 5. DI & testing

`AddPipelineActions()` (wherever `song_*` actions register today) also registers each `MusicControlActions` type. `AddAutomationApi()` registers `SongChangedAutomationEventDescriptor` into `IAutomationEventRegistry`'s auto-discovery set and the three new `IAutomationCommandService` reads on the existing `AutomationCommandService` implementation — no new DI module.

**Tests (prove behavior, not surface):**
- Each `music_*` action: capability present → calls the right provider member with the right args (verify via provider test double, not "no exception"); capability absent → `CAPABILITY_UNSUPPORTED`, provider member never invoked.
- Toggle actions (`music_play_pause`, `music_toggle_shuffle`, `music_cycle_repeat`, `music_toggle_saved`): given a fixed current-state fixture (playing/shuffled/repeat mode/saved), asserts the *opposite* state is what gets written — not just "some write happened."
- `GetNowPlayingAsync`/event descriptor share `MusicAutomationProjection`: one test proves the REST read and the event payload produce byte-identical DTOs for the same domain state (guards the "never drift" claim in §3.2).
- `isSaved` on a provider without `Library` capability → `null`, not a thrown `CAPABILITY_UNSUPPORTED` (projection must degrade gracefully since it's read-implicitly on every `song.changed` event, not requested per-call).
- `GetDevicesAsync`/`GetPlaylistsAsync`: capability-gated identically to their action counterparts; playlist pagination matches the existing dashboard-facing pagination contract (no `pageSize` ignored — `pagination-cap-bug-and-plan` regression class).
- Gate-2: minting an automation token without `music:control:write` on the actor's role still succeeds for non-music scopes but the resulting token's pipeline-invoke of a `music_*`-containing pipeline is rejected — proves the new key actually gates, not just exists.

---

## 6. Decisions (resolved)

Silent `MusicControlActions` family separate from chat-flavored `song_*` (D1); full discrete + toggle/cycle hook coverage, no curated subset (D2); `read`-scoped now-playing/devices/playlists surface on `IAutomationCommandService` for initial paint + pickers (D3); `song.changed` promoted to a public automation event carrying `isSaved`/`shuffleEnabled`/`repeatMode`, position-anchor shape reused verbatim from `widget-sdk.md` §9 (D4); new Gate-2 `music:control:write`, `Critical` tier (D5); zero schema changes (D6).
