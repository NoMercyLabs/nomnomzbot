# Interface Specification — Music Control Grid (Stream Deck controls as a streamer-edited grid on the dashboard Music page)

**Status:** Build-ready, with the open decisions in §9 for Stoney.
**Sources read for this spec:** `music-automation-controls.md` §3.1 (the `music_*` action ledger), `streamdeck-plugin.md` §1 (the 23-action manifest), `tools/streamdeck/music/src/actions/*.ts` and `bot.nomnomzbot.streamdeck.music.sdPlugin/manifest.json` (the shipped plugin), `dashboard/src/views/dashboard/music/MusicView.vue` and `components/NowPlayingCard.vue`, `dashboard/src/api/music.ts`, `server/src/NomNomzBot.Api/Controllers/V1/MusicController.cs`, `dashboard/src/components/layout/BoardGrid.vue`, `dashboard/src/realtime/configDomains.ts`.

## Stoney's words (verbatim, 2026-10-16)

> "i like the music pannel to have all the buttons from the streamdeck as a pick and place with all its options like picking a playlist for the add to playlist button. lets add a grid instead of centered buttons and let the user drop in controls where they please."

Arc's reading (not settled, Stoney must confirm): the "panel" is the Now Playing panel of the Music page, where the transport buttons are centered today. The grid replaces that centered row.

---

## 1. Ledger: every Stream Deck music action (23 of 23)

Count check: the manifest declares 23 actions (`manifest.json`, `Actions[]`, counted by script this run). 22 map to a backend `music_*` type. Now Playing reuses `music_play_pause` and adds cover art.

"Options" are the keys the plugin sends in `resolveParams` (read in `src/actions/*.ts`; defaults in brackets). Every key also stores `backgroundColor` (`musicAction.ts`, `backgroundColorOf`); the grid does not need it, so it is dropped (see §9, Q3).

| # | Stream Deck name | Backend `Type` | Options (default) | Live state on the key |
|---|---|---|---|---|
| 1 | Play | `music_play` | none | none |
| 2 | Pause | `music_pause` | none | none |
| 3 | Play/Pause | `music_play_pause` | none | 2 states, elapsed time |
| 4 | Now Playing | `music_play_pause` | none | cover art, marquee |
| 5 | Next Song | `music_next` | none | dim if `canSkipNext` is false |
| 6 | Previous Song | `music_previous` | none | dim if `canSkipPrevious` is false |
| 7 | Set Volume | `music_set_volume` | `volume` 0-100 (50) | title `{volume}%` |
| 8 | Volume Up | `music_volume_up` | `step` (10) | none |
| 9 | Volume Down | `music_volume_down` | `step` (10) | none |
| 10 | Volume Mute/Unmute | `music_volume_mute` | `unmuteVolume` (50) | icon flips at volume 0 |
| 11 | Seek | `music_seek` | `positionSeconds` (0) | dim if `canSeek` is false |
| 12 | Set Shuffle | `music_set_shuffle` | `enabled` bool (true) | dim if `canSetShuffle` is false |
| 13 | Toggle Shuffle | `music_toggle_shuffle` | none | 2 states |
| 14 | Set Repeat Mode | `music_set_repeat` | `mode` off, track, context (off) | dim if `canSetRepeat` is false |
| 15 | Cycle Repeat Mode | `music_cycle_repeat` | none | mode title |
| 16 | Switch Playback Device | `music_transfer_device` | `deviceId` from device picker | none |
| 17 | Save Track | `music_save_track` | none | none |
| 18 | Remove Saved Track | `music_unsave_track` | none | none |
| 19 | Favorite Toggle | `music_toggle_saved` | none | 2 states from `isSaved` |
| 20 | Add to Playlist | `music_add_to_playlist` | `playlistId` from playlist picker | none |
| 21 | Remove from Playlist | `music_remove_from_playlist` | `playlistId` from playlist picker | none |
| 22 | Follow Artist | `music_follow_artist` | none | none |
| 23 | Unfollow Artist | `music_unfollow_artist` | none | none |

Not in the ledger on purpose: `play_track_once` (backend only, no Stream Deck key) and the open "Run pipeline" row (S110, not built).

## 2. Map to the dashboard Music page (N of M, gaps marked)

What the page does today: `MusicView.vue` wires `pause`, `resume` and `skip` only (`usePauseMusic`, `useResumeMusic`, `useSkipMusic` in `api/music.ts`). `NowPlayingCard.vue` already emits `previous`, `seek`, `volume`, `shuffle` and `repeat`, but `MusicView.vue` does not listen to them. `api/music.ts` has no hook for them, for devices, or for playlists (the `use*` list was read this run).

What the server already offers per channel (`MusicController.cs`): `POST pause`, `POST resume`, `POST skip`, `POST previous`, `PUT volume`, `POST seek`, `PATCH shuffle`, `PATCH repeat`, `GET devices`, `POST transfer`, `GET playlists`, `POST play-context`, `GET now-playing`. Gate: `music:queue:moderate` for pause, resume and skip; `music:remote:control` for previous, volume, seek, shuffle, repeat, devices, transfer; `music:library:write` for playlists.

| # | Control | Dashboard control today | API it can use today | Gap |
|---|---|---|---|---|
| 1 | Play | Resume button | `POST music/resume` | none |
| 2 | Pause | Pause button | `POST music/pause` | none |
| 3 | Play/Pause | Pause or Resume (one is shown) | `pause` + `resume` | none |
| 4 | Now Playing | Now Playing card | `GET music/queue` (`nowPlaying`) | none |
| 5 | Next | Skip button | `POST music/skip` | none |
| 6 | Previous | emitted, not wired | `POST music/previous` | UI hook only |
| 7 | Set Volume | slider emits, not wired | `PUT music/volume` | UI hook only |
| 8 | Volume Up | none | `PUT music/volume` (client reads volume, adds step) | UI hook; step logic |
| 9 | Volume Down | none | `PUT music/volume` | UI hook; step logic |
| 10 | Mute/Unmute | none | `PUT music/volume` | needs the remembered pre-mute level (`IMuteVolumeMemory`, server side) |
| 11 | Seek | slider emits, not wired | `POST music/seek` | UI hook only |
| 12 | Set Shuffle | emits, not wired | `PATCH music/shuffle` | UI hook only |
| 13 | Toggle Shuffle | emits `!shuffleState`, not wired | `PATCH music/shuffle` | UI hook only |
| 14 | Set Repeat | none | `PATCH music/repeat` | UI hook only |
| 15 | Cycle Repeat | emits next mode, not wired | `PATCH music/repeat` | UI hook only |
| 16 | Switch Device | none | `GET music/devices`, `POST music/transfer` | UI hook + picker |
| 17 | Save Track | none | none | **server gap** |
| 18 | Remove Saved | none | none | **server gap** |
| 19 | Favorite Toggle | none | none | **server gap** |
| 20 | Add to Playlist | none | `GET music/playlists` lists only | **server gap** (add) |
| 21 | Remove from Playlist | none | `GET music/playlists` lists only | **server gap** (remove) |
| 22 | Follow Artist | none | none | **server gap** |
| 23 | Unfollow Artist | none | none | **server gap** |

Totals: 5 of 23 work from the page today (1, 2, 3, 4, 5). 11 need only a UI hook (6, 7, 8, 9, 11, 12, 13, 14, 15, 16 plus 10 after the mute-level fix). 7 need a new server route (17-23). Row 10 also needs the server mute memory, so the server-gap count for new routes is 7 and the "mute" fix is one extra item (slice S2).

Not checked this run: the exact request bodies of `VolumeDto`, `SeekDto`, `ShuffleDto`, `RepeatDto`, `TransferDto`. S1 reads them from `server/openapi/v1.json` before writing hooks.

## 3. Decisions

| # | Decision |
|---|---|
| G1 | **One run path for every control.** New route `POST /channels/{channelId}/music/controls/run` with body `{ type, options }`. The server runs the same `music_*` `ICommandAction` the Stream Deck runs, with the same checks (capability, premium, scope). The Stream Deck and the dashboard then cannot drift, and the 7 server gaps close in one place without 7 new routes. The 11 existing typed routes stay for the Now Playing card. |
| G2 | **Gate.** The run route uses `music:remote:control` (the existing key the dashboard transport routes use). `music:control:write` (Broadcaster floor, `music-automation-controls.md` D5) stays the key for automation tokens. Open question Q1 in §9. |
| G3 | **The layout is data, stored per channel on the server, one row per channel.** Not a view preference, so it does not use `useLocalStorage`. The edit-mode toggle is transient and not stored. |
| G4 | **Live sync reuses the existing ConfigChanged pipe.** After a save the server publishes `ChannelConfigChangedEvent` with `Domain = "music-control-grid"`, `Action = "updated"` (same call shape as `MusicConfigService.cs:111`). The dashboard adds the domain to `configDomains.ts`, so every open tab refetches. |
| G5 | **Drag and drop uses the dependency already installed:** `sortablejs` ^1.15.7 with `useSortable` from `@vueuse/integrations` ^15 (both in `dashboard/package.json`; `MusicView.vue` already uses `useSortable` for the queue). No hand-rolled drag code. Cells swap through SortableJS's bundled `Swap` plugin (`Sortable.mount(new Swap())`, option `swap: true`). The picker is a second Sortable list with `group: { name: 'music-controls', pull: 'clone', put: false }`. Not checked: the `Swap` plugin export path in the installed 1.15.7 build. S4 checks it first and falls back to `onAdd`/`onUpdate` handlers that write the new cell index. |
| G6 | **Fixed cell grid, not free x/y.** The grid has `columns` (4 to 8, default 6) and `rows` (1 to 4, default 2). A control sits in one cell. This matches the Stream Deck mental model and keeps drag and drop to a cell swap. |
| G7 | **Pickers live in the control's options popover.** Add to Playlist and Remove from Playlist open a playlist select (`GET music/playlists`). Switch Device opens a device select (`GET music/devices`). The saved option holds the id plus a display name snapshot so the cell label survives a deleted playlist. |
| G8 | **Layout is one JSON blob, validated by the server.** Unknown `type` or wrong option shape is rejected with a 400 (no silent drop). |
| G9 | **Default layout.** A channel with no row sees a default grid: Previous, Play/Pause, Next, Toggle Shuffle, Cycle Repeat, Favorite Toggle. This replaces the centered buttons. The default is computed in code and never written until the streamer edits. |
| G10 | **UI rules from the repo apply.** Icon-only buttons are `size="icon"` (36px) with `size-5` icons. One primary action per group (Edit grid is `outline`, Done is the one primary while editing). Concentric radius for a cell inside the card. Load the `sleak` skill before writing the UI. All text through i18n keys (American English). Icons from `~icons/nnz/<name>` (designer icons only); the Stream Deck SVGs in `imgs/actions/` are the reference for which glyph each control gets, not a source to copy. |

## 4. Storage model

New entity `MusicControlLayout` in `NomNomzBot.Domain.Music.Entities` (beside `BlockedTrack`), `ITenantScoped`, soft delete not needed (the row is replaced).

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` (`CreateVersion7`) | key |
| `BroadcasterId` | `Guid` | tenant key, **unique index** (one row per channel) |
| `Columns` | `int` | 4-8 |
| `Rows` | `int` | 1-4 |
| `CellsJson` | `string` (JSON, max 16 KB) | array of `{ cell, type, options }` |
| `UpdatedAt` | `DateTimeOffset` | |
| `UpdatedByUserId` | `string?` (max 50) | |

`CellsJson` shape (the server parses it into a typed record; no raw JSON in the API):

```json
[
  { "cell": 0, "type": "music_previous", "options": {} },
  { "cell": 1, "type": "music_play_pause", "options": {} },
  { "cell": 7, "type": "music_add_to_playlist", "options": { "playlistId": "37i9...", "playlistName": "Chill" } }
]
```

Rules: `cell` is in `0 .. columns*rows-1` and unique; `type` is one of the 22 backend types from the ledger (Now Playing is the Now Playing card, not a cell); option keys per type are exactly the ledger "Options" column. Shrinking `columns`/`rows` is refused with a 409 while a control sits outside the new size (the streamer removes or moves it first; nothing is dropped silently).

Migrations (both sets, same slice, per the project rule):
- Postgres: `server/src/NomNomzBot.Infrastructure/Platform/Persistence/Migrations/<timestamp>_AddMusicControlLayout.cs` + Designer + `AppDbContextModelSnapshot.cs`.
- SQLite: `server/src/NomNomzBot.Migrations.Sqlite/` same name + Designer + snapshot.
- EF configuration `MusicControlLayoutConfiguration` in `NomNomzBot.Infrastructure/Music/Persistence/` (beside `BlockedTrackConfiguration`), `DbSet` on `AppDbContext` and `IApplicationDbContext`. Adding a `DbSet` can break test fakes of `IApplicationDbContext`; S3 fixes them in the same commit.
- Check with `scripts/migration-check.ps1`.

## 5. Endpoints

All under `api/v{version}/channels/{channelId}/music`, `[ApiVersion("1.0")]`, `StatusResponseDto<T>`, errors through `Result<T>`.

| Verb | Path | Gate | Body / response |
|---|---|---|---|
| GET | `controls/layout` | `music:remote:control` | `MusicControlLayoutDto { columns, rows, cells[], isDefault }` (default layout when no row) |
| PUT | `controls/layout` | `music:remote:control` | full `MusicControlLayoutDto`; replaces the row; publishes `music-control-grid`; returns the saved layout |
| DELETE | `controls/layout` | `music:remote:control` | deletes the row; the page shows the default again; publishes `music-control-grid` |
| POST | `controls/run` | `music:remote:control` | `{ type, options }` -> runs the `music_*` action through the pipeline-action seam; returns the action result (error codes `CAPABILITY_UNSUPPORTED`, `PREMIUM_REQUIRED`, `MISSING_SCOPE` pass through) |
| GET | `playlists` (exists) | `music:library:write` | picker source; gate differs from the layout gate, so the picker needs Q1 settled |
| GET | `devices` (exists) | `music:remote:control` | picker source |

DTOs live in `NomNomzBot.Application/Music/Dtos/`, service `IMusicControlLayoutService` in `Application/Music/Services/` (typed interface, DI, no MediatR), implementation in `Infrastructure/Music/`. Refresh `server/openapi/v1.json` with `scripts/refresh-openapi` and regenerate `dashboard/src/api/schema.d.ts` in the same slice as the endpoint.

## 6. Dashboard design

Files (all under `dashboard/src/views/dashboard/music/components/`, one responsibility each):

| File | Job |
|---|---|
| `ControlGrid.vue` | CSS grid of `columns x rows` cells. Normal mode: each filled cell is a control button; empty cells are blank. Edit mode: empty cells show a dashed drop target; filled cells show a drag handle and a remove button. Owns the `useSortable` wiring. |
| `ControlCell.vue` | One control button: icon, short label, live state (shuffle on/off, saved heart, repeat mode, dim when a capability flag is false). Runs its control on click. |
| `ControlPicker.vue` | The list of all 22 controls (icon + name), grouped Transport, Volume, Shuffle and repeat, Library, Playlists, Device. Each item is draggable into the grid; a click places it in the first empty cell. |
| `ControlOptions.vue` | Popover opened from a placed control in edit mode. Fields per type from the §1 ledger: volume slider, step, unmute level, seek seconds, shuffle on/off, repeat mode select, playlist select, device select. |
| `controlCatalog.ts` | The 22-entry table `as const satisfies Record<ControlType, ControlSpec>`: icon, i18n key, group, option schema, `isToggleLive`. Single source for picker, options and cell. |
| `useControlLayout.ts` (in `dashboard/src/api/musicControls.ts`) | TanStack Query hooks: `useMusicControlLayout`, `useSaveMusicControlLayout`, `useRunMusicControl`, `useMusicDevices`, `useMusicPlaylists`. Query key joins `configDomains.ts` under `music-control-grid`. |

Placement on the page: the grid sits inside the Now Playing `PanelCard` below the track info, replacing the centered transport row of `NowPlayingCard.vue`. The panel header gets one `outline` button "Edit controls" (and "Done" as the primary while editing). Layout size controls (columns, rows) appear in edit mode only.

Edit flow: Edit -> picker opens as a side list in the panel -> drag a control to a cell (or click it) -> options popover opens at once for types with options (an unset option shows a warning badge on the cell and the cell stays inert until set) -> drag a placed control onto another cell to swap -> remove with the cell's remove button -> Done saves with `PUT controls/layout`. Autosave per change is not used; one Done = one PUT = one event (open Q2).

Live state comes from the existing `GET music/queue` `nowPlaying` data (shuffle, repeat, volume) kept fresh by the existing hub; `isSaved` and the capability flags are not on that DTO today. S2 adds them to the dashboard now-playing DTO from `MusicAutomationProjection` (same projection, no drift) so Favorite Toggle and dimmed cells are truthful. Until S2 lands those two cell kinds show no state (never a guessed one).

## 7. Tests (prove behavior)

- Layout service: save then read returns identical cells; shrinking under a placed control returns 409 and the stored row is unchanged; duplicate `cell` or unknown `type` returns 400; a second channel cannot read or write the first channel's row (tenant isolation).
- Save publishes exactly one `ChannelConfigChangedEvent` with `Domain = "music-control-grid"`.
- Run route: each `type` calls the matching provider member with the options given (provider test double); capability missing returns `CAPABILITY_UNSUPPORTED` and the member is never invoked; no `music:remote:control` returns 403.
- Dashboard: dropping a picker item on cell 7 produces a PUT body with `cell: 7`; swapping two cells swaps their `cell` values; remove deletes the entry; a playlist control with no `playlistId` does not call run; a `music-control-grid` hub event refetches the layout (liveSync test); opening a second mount shows the saved layout.
- Contract: `ApiContractTest`/schema drift check passes after `refresh-openapi`.

## 8. Slices (each one vertical, validated, committed on its own)

| Slice | Delivers | Layers |
|---|---|---|
| S1 | UI hooks for the 11 existing routes (`previous`, `volume`, `seek`, `shuffle`, `repeat`, `devices`, `transfer`, `playlists`) in `api/music.ts`, wired in `MusicView.vue` to the events `NowPlayingCard` already emits. Fixes the dead Now Playing buttons first. | dashboard |
| S2 | Server: `POST controls/run` + dashboard now-playing DTO gains `isSaved` and capability flags; mute memory reachable from the run path. Closes gaps 10 and 17-23. | server + openapi + schema.d.ts |
| S3 | Storage + layout service + `GET/PUT/DELETE controls/layout` + ConfigChanged event + both migration sets + tests. | server |
| S4 | Dashboard: `ControlGrid`, `ControlCell`, `ControlPicker`, `controlCatalog`, `musicControls.ts`, default layout, live sync via `configDomains.ts`, replaces the centered row. View mode only (click runs the control). | dashboard |
| S5 | Dashboard edit mode: drag from picker, swap, remove, resize grid, `ControlOptions` popover with playlist and device pickers. | dashboard |
| S6 | Live check on the box with a throwaway channel row (create, delete in the same run, prove by read), side-by-side screenshot of the Stream Deck key set against the grid, list every difference. | verify |

Order: S1 and S3 can run in parallel (different trees); S2 follows S1; S4 needs S2 and S3; S5 follows S4. Dashboard slices edit the main tree under the Vite dev server, one writer at a time.

## 9. Open decisions for Stoney (one line each; defaults chosen so work can start)

- Q1. Gate for the run and layout routes: default `music:remote:control` (already used by the dashboard transport). The playlist list needs `music:library:write`, which is a different key. Default: the picker route accepts either key.
- Q2. Save on Done (default) or save after every change?
- Q3. Per-control background color like the Stream Deck key (default: no, the design system tokens decide the look).
- Q4. Should the grid also be offered as an overlay or a viewer-facing panel? Default: no, streamer and moderators only.
