# Interface Specification — Elgato Stream Deck Plugin (client artifact)

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** Elgato Stream Deck SDK v2 (`com.elgato.streamdeck`, Node.js/TypeScript plugin runtime, `manifest.json` action registry, property inspector = per-action HTML page, `setImage`/`setTitle`/`setState`/`sendToPropertyInspector` websocket protocol between the Stream Deck app and the plugin process). Corpus: `stream-deck.md` (the backend contract — pairing D2/D7, token lifecycle D8; this plugin is the device side and restates none of it); `music-automation-controls.md` (the `music_*` pipeline actions — 19 specified there, plus `music_volume_up`/`music_volume_down`/`music_volume_mute` added since — + `GetNowPlayingAsync`/`GetDevicesAsync`/`GetPlaylistsAsync` reads + `song.changed` event this plugin's keys drive/display); `automation-api.md` (§1 WS protocol `op`/`id`/`response`/`event` shape, `D3` auth-transport: native tools use `Authorization: Bearer`).
**Conventions (binding):** TypeScript, strict mode, `tools/streamdeck/` (D5, `stream-deck.md`) — an npm workspace with three packages: `shared/` (the connection/pairing/token layer, not itself an installable plugin), `music/` (installable plugin, UUID `bot.nomnomzbot.streamdeck.music`), and `obs/` (installable plugin, UUID `bot.nomnomzbot.streamdeck.obs`). No React/framework needed for property inspector pages — Elgato's own `sdpi-components` web components (their documented PI toolkit) keep the PI dependency-free and consistent with every other Elgato plugin's look.

> **Why.** `stream-deck.md`/`music-automation-controls.md` built the entire backend contract this plugin rides — a token, a WS event stream, a REST invoke/read surface. This spec is the client: what ships to the Elgato Marketplace, how the backend music actions become clearly-labeled Stream Deck actions — 23 in the shipped manifest (owner: no partial grouping — every capability gets its own tray entry with its own description, plain streamer-friendly language over technical terms); the generic Run pipeline key is not built yet (open, S110), and how a key shows *live* state (elapsed time, shuffle/repeat/favorite) without polling.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| P1 | **23 static music actions, one tray entry each.** The shipped `music/` manifest (`bot.nomnomzbot.streamdeck.music.sdPlugin/manifest.json`) declares **23** actions under the plugin-level category **"NomNomzBot Music"** (manifest `Category`; the OBS plugin's manifest uses **"NomNomzBot OBS"**). They are the 19 `music-automation-controls.md` §3.1 types plus **Now Playing** (cover art + scrolling title/artist marquee, tap toggles playback — invokes `music_play_pause`), **Volume Up**, **Volume Down** and **Volume Mute/Unmute** (`music_volume_up` / `music_volume_down` / `music_volume_mute`). Every action ships its own icon, name and a one-line tooltip stating exactly what it does (e.g. Save Track: *"Adds the currently playing track to your Liked Songs."*). Discrete and toggle/cycle variants (`music_play` vs `music_play_pause`, `music_save_track` vs `music_toggle_saved`, …) are **both** present as distinct tray entries — never merged, per owner direction. **Open — S110:** the generic **"Run pipeline"** action (category "NomNomzBot"; a property-inspector picker fed from `GET /automation/v1/pipelines`, key invokes the chosen `pipelineId` with optional `{var}` params) is **not built** — no manifest row, no action class, no picker page exist today. `GET /automation/v1/pipelines` and `POST /automation/v1/invoke` are ready on the backend; only the plugin side is missing. |
| P2 | **One shared connection, many key instances.** The Elgato plugin process is a **singleton** per running Stream Deck app (SDK contract) — it owns exactly one WS connection to `/automation/v1/stream` and one in-memory `NowPlayingState` (from `GetNowPlayingAsync` on connect, then kept live by `song.changed` events). Every action instance (however many keys use NomNomzBot actions) reads from this one shared state — no per-key polling, no per-key WS connection. |
| P3 | **Onboarding is plugin-driven device-flow pairing (`stream-deck.md` D9; the D7 loopback handoff and the D2 code fallback are not what shipped).** On launch, and again whenever pairing is lost, the plugin calls `runDeviceFlowLoop` (`shared/src/pairing.ts`). If it holds no pairing it opens its own local page (`shared/src/authWindow.ts`, an HTTP server on `127.0.0.1:21617`, opened in the operator's default browser) with a "Bot URL" field (default `http://localhost:5080`) and an **Authorize** button. Authorize calls `POST /automation/v1/pair/device/init`, opens the returned `verificationUri` (the backend's own approval page, `/api/v1/automation/pair/device/approve?code=…`) in a second tab, and polls `POST /automation/v1/pair/device/poll` at the server-given interval until an operator approves. On approval it stores `{ backendUrl, token, tokenExpiresAt, deviceKind }` in Stream Deck global settings and starts the WS stream. There is **no "Not connected" key face** and no in-key connect button: a key pressed while unpaired calls `showAlert()`. The property inspector (`ui/settings.html`) shows pairing status (starting / waiting for approval with the link / last error and retry / paired with expiry) and the Host field; it never redeems anything itself. |
| P4 | **Key feedback: SVG data-URI via `setImage`, no native canvas.** Every key image is an SVG built in `nowPlaying/keyRenderer.ts` (icon + optional text such as `mm:ss`, per-key background colour) and pushed as `data:image/svg+xml;base64,…` through the SDK's `setImage`. SVG needs no native image library, so a fresh install works with no bundled Cairo/Skia. Play/Pause-with-elapsed-time redraws on a 1 s `setInterval` that extrapolates position from the last `song.changed` anchor (`positionMs` + `receivedAt` + `Date.now()`; `widget-sdk.md` §9 pattern reused client-side). Toggle keys (Favorite, Shuffle, Volume Mute) swap between icon variants (`favorite-outline`/`favorite-filled`, `shuffle-off`/`shuffle-on`, `volume`/`volume-mute`) on every `song.changed` push, and a key whose provider-side `actions.disallows` flag blocks it renders dimmed. The manifest still declares `States` for the two-state actions. |
| P5 | **Device/playlist pickers live in the property inspector, fetched once pairing exists.** `music_transfer_device` and `music_add_to_playlist`/`music_remove_from_playlist` PIs call `GET /automation/v1/music/devices` / `/music/playlists` (via the plugin's Node backend proxying — PI pages can't hold the bearer token themselves, they message the plugin process via the SDK's `sendToPlugin`, which does the authed fetch and relays results back) and render a native `sdpi-select` dropdown (device name / playlist name + track count). Selecting an entry stores its id in the action's per-key `Settings` (`playlistId`/`deviceId`), used as the `{var}`-substituted param on invoke. |
| P6 | **Invoke path: REST, not WS.** Keypresses call `POST /automation/v1/invoke` with the action's `Type` + resolved params (WS is for the event subscription only, per `automation-api.md` D1/D4's plane split) — matches how the backend already expects `invoke` scope to be used; no new invocation path. |
| P7 | **Token lifecycle owned by the plugin process, not per-key (`stream-deck.md` D8).** The plugin implements the D8 device side (startup + daily check, proactive refresh under the D8 threshold) and persists the secret to Stream Deck **global settings** (`setGlobalSettings`, the SDK's per-plugin encrypted-at-rest-by-the-OS-keychain-where-available store — matches the native dashboard app's own keychain-token pattern). An HTTP 401 on any call (an expired or revoked token — the wire carries no `TOKEN_*` code, only the status) or a hard-expired token at the startup/daily check clears the stored pairing (keeping the Host setting) and fires `onDisconnected`, which re-runs the P3 device flow. |

---

## 1. Action manifest (tray entries)

`manifest.json` `Actions[]`: **23** rows under the plugin-level category `"NomNomzBot Music"` (the 19 `music-automation-controls.md` §3.1 pipeline actions plus Now Playing and the three volume-step/mute keys, P1). The generic **Run pipeline** row is **not built** (open, S110) and is listed last for reference only. `Name`/`Tooltip` are the plain-language surface (not the backend `Type` string), `UUID = bot.nomnomzbot.streamdeck.music.<slug>` in the `music/` plugin's own manifest (OBS actions carry `bot.nomnomzbot.streamdeck.obs.<slug>` in the `obs/` plugin's manifest — 23 actions under category `"NomNomzBot OBS"`, specified in `obs-control.md` §5 (the pipeline actions its keys invoke); each plugin ships its own `manifest.json`, per §2).

| Backend `Type` | Stream Deck `Name` | Tooltip | Key rendering | PI fields |
|---|---|---|---|---|
| `music_play` | Play | "Resumes playback." | static icon | — |
| `music_pause` | Pause | "Pauses playback." | static icon | — |
| `music_play_pause` | Play/Pause | "Toggles playback and shows the elapsed time of the current track." | dynamic SVG (P4) | — |
| `music_next` | Next Song | "Skips to the next track." | static icon | — |
| `music_previous` | Previous Song | "Goes back to the previous track." | static icon | — |
| `music_play_pause` | Now Playing | "Real cover art with a scrolling title/artist marquee; taps toggle playback." | dynamic SVG, cover art (P4) | — |
| `music_set_volume` | Set Volume | "Sets playback volume to a fixed level." | static icon + title `{volume}%` | volume slider (0-100) |
| `music_volume_up` | Volume Up | "Raises playback volume by a step." | static icon | step (default 10) |
| `music_volume_down` | Volume Down | "Lowers playback volume by a step." | static icon | step (default 10) |
| `music_volume_mute` | Volume Mute/Unmute | "Mutes if audible, or unmutes to a configured level if muted. Icon reflects the real current volume." | 2-state, live (P4) + title = volume % | unmute level (default 50) |
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
| *(pipeline invoke: `pipelineId` + params)* — **NOT BUILT, open (S110)** | Run pipeline | "Runs one of your bot's pipelines (a command, event response or timer action chain)." | static icon + title = pipeline name | pipeline picker (from `GET /automation/v1/pipelines`, P1) + optional `{var}` params |

---

## 2. Plugin process architecture

An npm workspace of three packages — one shared library plus one installable plugin per action
group, each with its own `manifest.json`, so Music and OBS ship and version independently:

```
tools/streamdeck/
  package.json                 # workspace root: {"workspaces": ["shared", "music", "obs"]}
  shared/                      # @nomnomzbot/streamdeck-shared — NOT an installable plugin
    src/
      automationClient.ts      # WS subscribe (song.changed) + REST invoke/read/refresh (P6, P7)
      pairing.ts                # runDeviceFlowLoop entry point (P3)
      tokenStore.ts             # global-settings read/write, refresh-timer (P7)
      deviceFlow.ts             # /pair/device/init + /pair/device/poll client (P3)
      deviceFlowState.ts        # status the property inspector reads
      authWindow.ts             # local 127.0.0.1:21617 Authorize page (P3)
      index.ts                  # barrel
    tests/
  music/                        # installable plugin, UUID bot.nomnomzbot.streamdeck.music
    bot.nomnomzbot.streamdeck.music.sdPlugin/
      manifest.json             # SDK manifest: 23 music actions + plugin metadata (no Run pipeline yet, S110)
      ui/                       # property-inspector pages (see below)
    src/
      plugin.ts                 # SDK entrypoint — singleton connection owner (P2)
      nowPlaying/
        state.ts                 # shared NowPlayingState (P2), anchor+extrapolation (P4)
        keyRenderer.ts            # SVG data-URI renderer for icon, play/pause+time and cover-art keys
      actions/
        <one file per manifest action>.ts   # SDK action class: onKeyDown → invoke; onWillAppear → subscribe to state
    tests/
  obs/                          # installable plugin, UUID bot.nomnomzbot.streamdeck.obs
    bot.nomnomzbot.streamdeck.obs.sdPlugin/
      manifest.json
    src/
      plugin.ts
      keyRenderer.ts             # generic icon-key SVG renderer (no now-playing state)
      actions/
    tests/
```

Both plugins' `ui/` folders (property inspector pages) hold `settings.html` (the plugin-level
pairing status + Host page) + `shared.js` (`sdpi-components` base), `appearance-only.html` (background
colour only) and `simple-param.html` (one numeric/select field), plus per-action picker pages selected
by each action's own `PropertyInspectorPath` in its plugin's `manifest.json`:
- `music/ui/`: `device-picker.html`, `playlist-picker.html`.
- `obs/ui/`: `scene-picker.html`, `input-picker.html`, `input-volume-picker.html`, `media-picker.html`,
  `scene-source-picker.html`, `screenshot-picker.html`, `source-filter-picker.html`,
  `transition-picker.html`.
- No `pipeline-picker.html` exists — it arrives with Run pipeline (S110).

Every action file follows the same shape: `onWillAppear` registers the key with `keyRenderer`/`state` for live repaint; `onKeyDown` resolves its `Settings` (device/playlist id, volume, etc.) and calls `automationClient.invoke(Type, params)`; a shared error path flashes the key via `showAlert()` (SDK built-in) and logs the backend's reason (`AutomationApiError.errorCode ?? message`) to the plugin log — never a silent no-op, matching the project's "truthful data, not fake enforcement" standard. The wire carries the error kind as an HTTP status plus a `message`, not a typed code: a 401 is the one status that means "token dead" and triggers the P7 re-pair. The reason is not shown on the key; the plugin log is where it lives.

---

## 3. Distribution

Elgato Marketplace submission (`.streamDeckPlugin` package, Elgato's signing/review process) + attached to our own GitHub releases (mirrors `stream-deck.md` D5). No auto-update mechanism beyond what the Marketplace/Elgato's own plugin updater provides — the plugin has no opinion on its own distribution channel beyond packaging correctly for both.

---

## 4. Testing

Elgato plugins run inside the Stream Deck app, not a normal test runner — testing is necessarily more integration-flavored than the C# backend's unit-test standard:
- `automationClient`/`state`/`keyRenderer`/`tokenStore`/`deviceFlow` are plain TypeScript modules with no SDK dependency — **unit-testable in isolation** (Vitest): anchor+extrapolation math produces the right `mm:ss` at a given offset; refresh-timer fires under the 7-day threshold and not above it; a 401 response clears the stored pairing and fires the disconnect listeners.
- Action classes (`onKeyDown`/`onWillAppear`) are thin SDK glue — verified by a manual pass against a real Stream Deck + a real paired dev-channel bot (this project's standard "validate every element live" bar applies here exactly as it does to dashboard UI): every one of the 23 music keys pressed once (plus a Run pipeline key once S110 lands), live state confirmed against the dashboard's own now-playing display, favorite/shuffle keys toggled and re-toggled to confirm the icon matches server truth after a refresh.

---

## 5. Decisions (resolved)

23 plain-language, individually-described music tray actions; the generic Run pipeline action is open (S110) (P1); one shared singleton connection + state for all key instances (P2); plugin-driven device-flow pairing through a local Authorize page (P3); SVG data-URI keys via `setImage`, 2-state icon swaps for booleans (P4); device/playlist pickers in the PI via plugin-proxied authed reads (P5); invoke rides REST, WS is subscribe-only (P6); token refresh owned by the plugin process on a daily timer + 7-day threshold (P7).
