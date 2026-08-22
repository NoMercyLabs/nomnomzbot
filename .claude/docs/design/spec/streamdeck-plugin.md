# Interface Specification — Elgato Stream Deck Plugin (client artifact)

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** Elgato Stream Deck SDK v2 (`com.elgato.streamdeck`, Node.js/TypeScript plugin runtime, `manifest.json` action registry, property inspector = per-action HTML page, `setImage`/`setTitle`/`setState`/`sendToPropertyInspector` websocket protocol between the Stream Deck app and the plugin process). Corpus: `stream-deck.md` (the backend contract — pairing D2/D7, token lifecycle D8; this plugin is the device side and restates none of it); `music-automation-controls.md` (the 19 `music_*` pipeline actions + `GetNowPlayingAsync`/`GetDevicesAsync`/`GetPlaylistsAsync` reads + `song.changed` event this plugin's keys drive/display); `automation-api.md` (§1 WS protocol `op`/`id`/`response`/`event` shape, `D3` auth-transport: native tools use `Authorization: Bearer`).
**Conventions (binding):** TypeScript, strict mode, `tools/streamdeck/` (D5, `stream-deck.md`). Elgato SDK plugin UUID reverse-DNS: `bot.nomnomz.streamdeck`. No React/framework needed for property inspector pages — Elgato's own `sdpi-components` web components (their documented PI toolkit) keep the PI dependency-free and consistent with every other Elgato plugin's look.

> **Why.** `stream-deck.md`/`music-automation-controls.md` built the entire backend contract this plugin rides — a token, a WS event stream, a REST invoke/read surface. This spec is the client: what ships to the Elgato Marketplace, how the 19 backend music actions become 19 clearly-labeled Stream Deck actions (plus one generic Run pipeline key) (owner: no partial grouping — every capability gets its own tray entry with its own description, plain streamer-friendly language over technical terms), and how a key shows *live* state (elapsed time, shuffle/repeat/favorite) without polling.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| P1 | **19 static music actions, one per backend `music_*` type, PLUS one generic "Run pipeline" action.** The 19 are grouped under one Elgato category, plain-language named (category **"NomNomzBot: Music"**, not "Transport"/"Music Control"); every action ships its own icon, title, and a one-line description in the tray tooltip stating exactly what it does (e.g. Save Track: *"Adds the currently playing track to your Liked Songs."*). Discrete and toggle/cycle variants (`music_play` vs `music_play_pause`, `music_save_track` vs `music_toggle_saved`, …) are **both** present as distinct tray entries — never merged, per owner direction. The **"Run pipeline"** action (category **"NomNomzBot"**) is the one non-static entry: its property inspector lists the channel's pipelines from `GET /automation/v1/pipelines` (`stream-deck.md` D1) and the key invokes the chosen `pipelineId` with optional `{var}` params — so any command/event response the streamer builds is one key away without a plugin release. No other "pick an operation" action exists. |
| P2 | **One shared connection, many key instances.** The Elgato plugin process is a **singleton** per running Stream Deck app (SDK contract) — it owns exactly one WS connection to `/automation/v1/stream` and one in-memory `NowPlayingState` (from `GetNowPlayingAsync` on connect, then kept live by `song.changed` events). Every action instance (however many keys use NomNomzBot actions) reads from this one shared state — no per-key polling, no per-key WS connection. |
| P3 | **Onboarding is dashboard-initiated, plugin-passive (`stream-deck.md` D7; manual-code fallback D2).** The plugin is the device side of D7 (loopback listener up from launch) and of D2 (code redemption). Every key renders a distinct **"Not connected"** state (greyed icon, title `"Connect in\ndashboard"`) until pairing completes — no in-plugin connect button. The property inspector of any not-yet-paired key is the D2 manual-entry surface (a code field + "Pair" button, `sdpi-components` text input). |
| P4 | **Key feedback: canvas-rendered dynamic images for anything with changing content, native multi-state for booleans.** Play/Pause-with-elapsed-time renders a 144×144 PNG (icon + `mm:ss` text baked in) via Node `canvas`, redrawn on a local `requestAnimationFrame`-equivalent (`setInterval(…, 1000)`) that extrapolates position from the last `song.changed` anchor (`widget-sdk.md` §9 pattern, reused client-side exactly as overlay widgets do — `positionMs` + `receivedAt` + `performance.now()`-equivalent `Date.now()` in Node). Favorite/Shuffle toggle keys use Elgato's native 2-state button images (`States: [unfavorited, favorited]` in `manifest.json`, driven by `setState(isSaved ? 1 : 0)`) — no per-frame redraw needed, just a state flip on `song.changed`. |
| P5 | **Device/playlist pickers live in the property inspector, fetched once pairing exists.** `music_transfer_device` and `music_add_to_playlist`/`music_remove_from_playlist` PIs call `GET /automation/v1/music/devices` / `/music/playlists` (via the plugin's Node backend proxying — PI pages can't hold the bearer token themselves, they message the plugin process via the SDK's `sendToPlugin`, which does the authed fetch and relays results back) and render a native `sdpi-select` dropdown (device name / playlist name + track count). Selecting an entry stores its id in the action's per-key `Settings` (`playlistId`/`deviceId`), used as the `{var}`-substituted param on invoke. |
| P6 | **Invoke path: REST, not WS.** Keypresses call `POST /automation/v1/invoke` with the action's `Type` + resolved params (WS is for the event subscription only, per `automation-api.md` D1/D4's plane split) — matches how the backend already expects `invoke` scope to be used; no new invocation path. |
| P7 | **Token lifecycle owned by the plugin process, not per-key (`stream-deck.md` D8).** The plugin implements the D8 device side (startup + daily check, proactive refresh under the D8 threshold) and persists the secret to Stream Deck **global settings** (`setGlobalSettings`, the SDK's per-plugin encrypted-at-rest-by-the-OS-keychain-where-available store — matches the native dashboard app's own keychain-token pattern). A `TOKEN_EXPIRED`/`TOKEN_REVOKED` result on any call clears global settings and every key reverts to the P3 not-connected state, re-arming for a fresh D7 handoff. |

---

## 1. Action manifest (tray entries)

`manifest.json` `Actions[]`: 19 rows under category `"NomNomzBot: Music"` (one per `music-automation-controls.md` §3.1 pipeline action) plus the generic **Run pipeline** row under category `"NomNomzBot"` (P1). `Name`/`Tooltip` are the plain-language surface (not the backend `Type` string), `UUID = bot.nomnomz.streamdeck.<slug>`.

| Backend `Type` | Stream Deck `Name` | Tooltip | Key rendering | PI fields |
|---|---|---|---|---|
| `music_play` | Play | "Resumes playback." | static icon | — |
| `music_pause` | Pause | "Pauses playback." | static icon | — |
| `music_play_pause` | Play/Pause | "Toggles playback and shows the elapsed time of the current track." | dynamic canvas (P4) | — |
| `music_next` | Next Song | "Skips to the next track." | static icon | — |
| `music_previous` | Previous Song | "Goes back to the previous track." | static icon | — |
| `music_set_volume` | Set Volume | "Sets playback volume to a fixed level." | static icon + title `{volume}%` | volume slider (0-100) |
| `music_seek` | Seek | "Jumps to a specific point in the current track." | static icon | position (seconds) input |
| `music_set_shuffle` | Set Shuffle | "Turns shuffle on or off." | 2-state (P4) | on/off select |
| `music_toggle_shuffle` | Toggle Shuffle | "Switches shuffle on/off and shows the current state." | 2-state, live (P4) | — |
| `music_set_repeat` | Set Repeat Mode | "Sets repeat to Off, Track, or Playlist/Album." | title = mode | mode select |
| `music_cycle_repeat` | Cycle Repeat Mode | "Cycles repeat Off → Track → Playlist/Album and shows the current mode." | title, live | — |
| `music_transfer_device` | Switch Playback Device | "Moves playback to a chosen device." | static icon | device picker (P5) |
| `music_save_track` | Save Track | "Adds the currently playing track to your Liked Songs." | static icon | — |
| `music_unsave_track` | Remove Saved Track | "Removes the currently playing track from your Liked Songs." | static icon | — |
| `music_toggle_saved` | Favorite Toggle | "Adds/removes the current track from your Liked Songs and shows whether it's saved." | 2-state, live (P4) | — |
| `music_add_to_playlist` | Add to Playlist | "Adds the currently playing track to a chosen playlist." | static icon | playlist picker (P5) |
| `music_remove_from_playlist` | Remove from Playlist | "Removes the currently playing track from a chosen playlist." | static icon | playlist picker (P5) |
| `music_follow_artist` | Follow Artist | "Follows the current track's artist." | static icon | — |
| `music_unfollow_artist` | Unfollow Artist | "Unfollows the current track's artist." | static icon | — |
| *(pipeline invoke: `pipelineId` + params)* | Run pipeline | "Runs one of your bot's pipelines (a command, event response or timer action chain)." | static icon + title = pipeline name | pipeline picker (from `GET /automation/v1/pipelines`, P1) + optional `{var}` params |

---

## 2. Plugin process architecture

```
tools/streamdeck/
  manifest.json                # SDK manifest: 19 music actions + Run pipeline + plugin metadata
  src/
    plugin.ts                  # SDK entrypoint — singleton connection owner (P2)
    connection/
      automationClient.ts      # WS subscribe (song.changed) + REST invoke/read/refresh (P6, P7)
      pairing.ts                # loopback HTTP listener (P3) + code-fallback relay
      tokenStore.ts             # global-settings read/write, refresh-timer (P7)
    nowPlaying/
      state.ts                  # shared NowPlayingState (P2), anchor+extrapolation (P4)
      keyRenderer.ts             # canvas → PNG data-URI for play/pause+time keys
    actions/
      <one file per manifest action>.ts   # SDK action class: onKeyDown → invoke; onWillAppear → subscribe to state
    propertyinspector/
      shared.html + shared.js    # sdpi-components base
      device-picker.html         # music_transfer_device PI
      playlist-picker.html       # add/remove-from-playlist PI
      pipeline-picker.html       # Run pipeline PI (P1)
      pairing-fallback.html      # not-yet-paired PI (P3 manual code fallback)
```

Every action file follows the same shape: `onWillAppear` registers the key with `keyRenderer`/`state` for live repaint; `onKeyDown` resolves its `Settings` (device/playlist id, volume, etc.) and calls `automationClient.invoke(Type, params)`; a shared error path (`TOKEN_EXPIRED`, `CAPABILITY_UNSUPPORTED`, `PREMIUM_REQUIRED`) flashes the key red via `showAlert()` (SDK built-in) with the failure reason in the tooltip — never a silent no-op, matching the project's "truthful data, not fake enforcement" standard.

---

## 3. Distribution

Elgato Marketplace submission (`.streamDeckPlugin` package, Elgato's signing/review process) + attached to our own GitHub releases (mirrors `stream-deck.md` D5). No auto-update mechanism beyond what the Marketplace/Elgato's own plugin updater provides — the plugin has no opinion on its own distribution channel beyond packaging correctly for both.

---

## 4. Testing

Elgato plugins run inside the Stream Deck app, not a normal test runner — testing is necessarily more integration-flavored than the C# backend's unit-test standard:
- `automationClient`/`state`/`keyRenderer`/`tokenStore` are plain TypeScript modules with no SDK dependency — **unit-testable in isolation** (Vitest): anchor+extrapolation math produces the right `mm:ss` at a given offset; refresh-timer fires under the 7-day threshold and not above it; a `TOKEN_EXPIRED` response clears global settings and flips `isConnected` false.
- Action classes (`onKeyDown`/`onWillAppear`) are thin SDK glue — verified by a manual pass against a real Stream Deck + a real paired dev-channel bot (this project's standard "validate every element live" bar applies here exactly as it does to dashboard UI): every one of the 19 music keys + a Run pipeline key pressed once, live state confirmed against the dashboard's own now-playing display, favorite/shuffle keys toggled and re-toggled to confirm the icon matches server truth after a refresh.

---

## 5. Decisions (resolved)

19 plain-language, individually-described music tray actions + one generic Run pipeline action (P1); one shared singleton connection + state for all key instances (P2); dashboard-initiated onboarding, plugin passively listens + PI-based manual-code fallback (P3); canvas-rendered dynamic keys for changing content, native 2-state for booleans (P4); device/playlist pickers in the PI via plugin-proxied authed reads (P5); invoke rides REST, WS is subscribe-only (P6); token refresh owned by the plugin process on a daily timer + 7-day threshold (P7).
