# Music playback determinism

Status: design, 2026-10-18 (rev 2: equal engines, separate music and bot audio). No code yet.

Stoney, verbatim (2026-10-18): "i paused spotify and it started playing through my old tts widget
instead of the new audio source. it then started spotify again while it was still active. i need a
better and more reliable solution and more deterninistic behavior from this".

## 1. Facts (read on the live box, 2026-10-18)

Channel `019f146e-8303-71ef-b698-18d1098d7d7e`.

Widgets that carry `enableAudio` (Postgres `Widgets` + `WidgetGalleryItems`):

| Widget | Id | Gallery key | Source | enableAudio | Updated |
|---|---|---|---|---|---|
| TTS Audio | 01a03a23-d5f7-7978-abc8-8954f55a8561 | `audio` | first_party | true | 2026-10-18 19:44 |
| Spotify NowPlaying | 019f7867-58fa-7564-bdc4-f1c5405b243e | none | custom | true | 2026-10-17 23:03 |
| TTS Caption | 01a1190f-70f4-787e-8dba-c603239c9262 | `tts_caption` | first_party | false | 2026-10-08 |

- "TTS Audio" IS the system audio widget (gallery key `audio`). Its name says "TTS".
  So Stoney sees it as "the old TTS widget". There is no second `audio` widget.
- "Spotify NowPlaying" is a custom widget with `enableAudio=true`. It can be a second player. Not proven.
- Presence (which widgets are attached now): not checked.
- The "new audio source" Stoney names: not identified in the database. Ask him which OBS source it is.

The pause, from the event journal and the API log:

| Time (UTC) | Event |
|---|---|
| 20:57:22 | Spotify plays "In This Shirt" (1.7 s). Queue = [No Limit (YouTube), KE6X]. |
| 20:57:29 | `POST music/pause` (dashboard). Spotify IsPlaying=false. |
| 20:57:30.17 | YouTube "No Limit" starts (ProgressMs 28). |
| 20:57:30.27 | Queue snapshot `Items: []`. |
| 20:57:37 | `POST music/resume`. Spotify IsPlaying=true while YouTube plays. |
| 20:57:39 | `POST music/pause`. |

Cause of the pause incident:
- `MusicStatePollingService.cs:277` calls `HandOverNextAsync` on every poll tick.
- With Spotify paused, the "is another engine playing" check passes. So the waiting YouTube head starts.
- A pause is read as "the engine is free". That is wrong. A pause is owner intent.
- `MusicService.ResumeAsync` resumes the active provider (Spotify). It does not know YouTube plays.

Earlier incidents tonight (cause files in the session scratchpad):
1. Queue wipe: `FairQueue.RemoveThrough` removes up to a deep match. 16 rows went.
2. Push+skip played a stray Spotify track.
3. A new Spotify request jumped a waiting YouTube head (`HandOverAdmittedIfIdleAsync`, MusicService.cs:1221).
   Then the head was removed as played.
4. The YouTube player ran in the wrong overlay.
5. Spotify resumed while YouTube played.
6. A manual pause by the owner caused a hand-over (above).

Shared root: six places can start playback (`HandOverNextAsync` callers: OverlayHub.cs:488-489,
MusicStatePollingService.cs:277, SongRequestQueueReconciler.cs:168, YouTubePlayerReportService.cs:80,
PlayOnceResumeHandler.cs:108, YouTubeMusicProvider.cs:160). Each one guesses the state from what it sees.
Nobody owns the state.

Later on 2026-10-18 (owner action, by Arc): `enableAudio` set to false on "Spotify NowPlaying"
(019f7867-...). The old Settings value is backed up in the session scratchpad.

Facts for the equal-engine rules (read in the repo, 2026-10-18):
- `IMusicProvider` (Domain/Music/Interfaces/IMusicProvider.cs:181-215) already has Play, Pause, Skip,
  Previous, SetVolume, Seek, SetShuffle, SetRepeat. Start-a-track is split over `IMusicProviderPlayNow`
  and `IMusicProviderStartTrack`.
- The Stream Deck plugin (tools/streamdeck/music/src/actions, 24 actions) calls the Automation API through
  `automationClient.invoke` (musicAction.ts:95). It names no engine. It is engine-neutral today on its side.
- The overlay already gets a Spotify Web Playback SDK token: `GET /api/v{version}/overlay/spotify-token`
  (server/openapi/v1.json:69473). So the music widget can host both engines in the browser.
- Not checked: which of the 8 provider methods the YouTube provider really implements (no-op or real).
  S-PD-0 lists it before any build.

## 2. Rule: one playback owner per channel

- One class owns playback for a channel: `PlaybackDirector` (one instance state per channel).
- It is the ONLY code that starts, pauses, resumes, skips, seeks, sets volume or hands over an engine.
- Every other code sends it an input event. No other code calls an engine's play, pause or push.
- Inputs are handled one at a time per channel (a per-channel lock or channel queue).
- Every transition writes one journal event: `PlaybackTransitionedEvent { From, To, Input, Engine, QueueItemId, Reason }`.

### 2.1 Equal engines (Stoney, verbatim, 2026-10-18)

"i need youtube and spotify to get equal treatment, controls via streamdeck/ui and the now playing widgets
should ALL work with both. the queue MUST be able to switch effortlessly and obey the fair queue system."

- One engine interface: `IPlaybackEngine`. Spotify and YouTube each implement all of it. No optional part.

  | Member | Meaning |
  |---|---|
  | `EngineKind Kind` | `Spotify` or `YouTube`. Data only; no caller branches on it. |
  | `Start(item)` | Play this queue item now. Spotify: PUT /me/player/play {uris}. YouTube: `music.play` to the music widget. |
  | `Pause()` / `Resume()` | Pause or resume the current track. |
  | `Stop()` | Pause and forget the track (used at a hand-over). |
  | `Seek(ms)` | Move inside the current track. |
  | `SetVolume(0-100)` | Engine volume (the music level, see 3). |
  | `Previous()` | Restart the current track if progress > 3 s, else replay the last played item (see below). |
  | `Reports` | A stream of `EngineReport` and `TrackEnded` in one shape for both. |

- `Previous` is the director's job, not the engine's. The director keeps a played-history list per channel
  (the last 20 played items). Previous puts the last played item back at the head and starts it.
  Spotify's own previous is never called: it would leave our queue and history.
- One now-playing shape for both engines: `NowPlaying { Engine, ItemId?, Title, Artists[], AlbumArtUrl,
  DurationMs, ProgressMs, IsPlaying, RequestedBy?, Volume, Muted, State }`. The director builds it from reports.
  One hub event carries it: `music.nowPlaying`. One REST read returns it: `GET music/now-playing`.
- Every control surface sends director inputs, never engine calls:
  - Dashboard music page and the mini player.
  - Automation API (the Stream Deck plugin and any client). Same action names as today.
  - Chat commands (`!skip`, `!pause`, `!resume`, `!volume`, `!song`, `!previous`).
  - Pipeline actions (music play/pause/skip/seek/volume/previous).
- Every now-playing widget (first-party and custom) reads `music.nowPlaying` only. A widget never calls
  the Spotify API or reads a YouTube player for its data.
- Account features with no YouTube counterpart (save track, follow artist, add to a Spotify playlist) stay as
  they are. They act on the Spotify catalog id of the item. For a YouTube item they return a typed
  `NotSupportedForEngine` result. The Stream Deck key shows it dimmed (the `disallows` path, musicAction.ts:58).
  They are account actions, not playback controls, so they do not break equal playback treatment.
- Guard: a source-scan test fails if any controller, chat command, pipeline action, Automation handler or widget
  file names `Spotify` or `YouTube` while it calls a playback control (allow-list: the two engine classes and their adapters).

### States

| State | Meaning |
|---|---|
| `Idle` | Nothing plays. Queue empty or no engine ready. |
| `Starting(engine, item)` | A start was sent. We wait to SEE it play. Deadline 10 s. |
| `Playing(engine, item?)` | An engine plays. `item` is the queue head, or null for the owner's own music. |
| `PausedByOwner(engine, item?)` | The owner paused. Nothing starts until the owner acts. |
| `Blocked(reason)` | A start is needed but impossible (music widget not open, token dead). Visible error. |

Music mute is not a state. It is a flag on the music output (see 3). A muted engine keeps playing and
keeps its place in the queue.

### Inputs

| Input | Source |
|---|---|
| `RequestAdded` | Song request admitted to the fair queue. |
| `EngineReport(engine, uri, isPlaying, progress)` | Spotify poll, YouTube player report. Same shape. |
| `TrackEnded(engine, uri)` | YouTube ENDED; Spotify: uri changed or progress reset after near-end. |
| `OwnerPause / OwnerResume / OwnerSkip / OwnerPrevious` | Any control surface (2.1), or detected in an engine (see 4). |
| `OwnerSeek(ms) / OwnerVolume(level) / OwnerMute(on)` | Any control surface. |
| `DuckStart(percent) / DuckEnd` | Bot audio (TTS, sounds) starts and ends. While ducked the music level is the owner level times percent. |
| `OutputAttached / OutputDetached` | Music widget presence. |
| `StartTimeout` | `Starting` deadline passed. |

### Transitions (the full table; anything not listed is ignored and logged)

`next` means the fair-queue head (`FairQueue<T>`, music-sr.md §3.8/§3.9). The director never picks an item itself.

| From | Input | To | Action |
|---|---|---|---|
| Idle | RequestAdded, head ready | Starting(head.engine, head) | start head on its engine |
| Idle | EngineReport playing (owner music) | Playing(engine, null) | none |
| Starting(e, i) | EngineReport e plays i.uri | Playing(e, i) | mark i as played (see 6) |
| Starting(e, i) | StartTimeout | Blocked("did not start") | keep i at head; show error. When the start paused the owner's music on another engine, that music resumes and the director follows it |
| Starting(e, i) | OwnerSkip, i lined up in e's queue behind the playing track | Starting(e, i) | e's skip plays i |
| Playing(e, null) | RequestAdded | Playing(e, null) | none until near end (6 s) of the owner track |
| Playing(e, null) | near end, head waits | Starting(head.engine, head) | stop e only if head.engine != e AND head.engine can start now (player or connection there); otherwise e plays on and the head counts a failed start |
| Playing(e, i) | TrackEnded(e, i.uri) | Starting(next) or Idle | if next.engine != e: e already ended, start next on its engine |
| Playing(e, *) | OwnerSkip, e has its own queue (Spotify) | Starting(next) or Playing(e, null) | e is never paused: a head e already holds plays through e's skip; another head on e starts over the current track; no head: e's skip moves on to the owner's next track |
| Playing(e, *) | OwnerSkip, e has no queue of its own (YouTube) or the head is on another engine | Starting(next) or Idle | stop e; wait for e's paused report (max 2 s); start next |
| Playing(e, *) | OwnerPrevious, progress > 3 s | Playing(e, *) | seek e to 0 |
| Playing(e, *) | OwnerPrevious, progress <= 3 s | Starting(last.engine, last) | put last played item at head; stop e; start it |
| Playing(e, *) | OwnerSeek / OwnerVolume / OwnerMute | Playing(e, *) | apply to e (mute: see 3) |
| Playing(e, *) | DuckStart / DuckEnd | Playing(e, *) | set e to the effective level: muted ? 0 : ducked ? volume * percent / 100 : volume |
| PausedByOwner(e, *) | OwnerSeek / OwnerVolume / OwnerMute / DuckStart / DuckEnd | PausedByOwner(e, *) | apply to e, same effective level |
| Playing(e, *) | OwnerPause | PausedByOwner(e, *) | pause e |
| PausedByOwner(e, *) | OwnerResume | Playing(e, *) | resume e ONLY |
| PausedByOwner(e, *) | RequestAdded, EngineReport paused, poll tick | PausedByOwner | none |
| PausedByOwner(e, *) | OwnerSkip | Starting(next) or Idle | start head; a head e already holds in its own queue plays through e's skip |
| PausedByOwner(e, *) | OwnerSeek / OwnerVolume / OwnerMute | PausedByOwner(e, *) | apply to e. Arc's decision, pending Stoney: OwnerSeek, OwnerVolume, OwnerMute are accepted in PausedByOwner |
| any | EngineReport of an engine that is not the owner engine, playing | same | pause that engine; log `foreign_engine_silenced` |
| Blocked | EngineReport playing | Idle, then Playing(engine, null) or Playing(engine, row) | follow that music (journal `music_playing`); a waiting head lines up at its end |
| Blocked | OutputAttached / token restored | Idle | re-evaluate |
| Blocked | RequestAdded | Idle, then Starting(head) | an engine whose last report plays is followed first, so the head never starts over it; otherwise re-evaluate the kept head and start it as Idle + RequestAdded does; journal reason `request_added` |

Changed 2026-10-19 (behavior audit). Source: Stoney 2026-10-19, "the music should NEVER EVER be cleared out".
Arc's reading: a skip, a failed start or a Blocked state never leaves the music paused or stopped. The old
rows paused Spotify on every skip and left it paused after a failed YouTube hand-over. Row 28 still blocks at
the device change itself; the next playing report from the new device is followed (no transfer is sent).

Engine switch (Spotify to YouTube or back) happens ONLY at the queue head, in the rows above.
No timer guesses the gap. The next engine starts after the old engine's `TrackEnded` or its confirmed
paused report, never on a fixed delay. A missing confirmation after 2 s is logged `stop_unconfirmed`
and the start goes ahead (the foreign-engine row silences the old engine if it plays on).

The poll tick is NOT an input. A poll only produces `EngineReport`. The recovery tick at
MusicStatePollingService.cs:277 goes away; `Blocked` plus `OutputAttached` replace it.

Decided (Stoney, 2026-10-19, picked 'Blocked error'): when the bot cannot start a requested song, the requester sees a clear, visible Blocked error that says why, the same as other blocked actions.

Arc's decision, pending Stoney: a play-once video that ends with no resume pending lets the director start the waiting request.

Arc's decision, pending Stoney: Blocked + RequestAdded retries the head (Blocked -> Idle, re-evaluate head).

Arc's decision, pending Stoney: PREMIUM_REQUIRED already wired. A Spotify start that answers PremiumRequired ends the director Blocked with the premium reason (thrown at SpotifyMusicProvider.cs:2056-2059, mapped at EngineBlockMap.cs:28, proven by commit 217872749).

## 3. Rule: two system widgets, two audio levels

Stoney, verbatim (2026-10-18): "we need a separate widget for the music, and one for the tts and other audio
comming from the bot. music must be controllable individually and be allowed to run on the users machine with
a different volume than chat hears."

### 3.1 Two first-party widgets

| Widget | Gallery key | Plays | Never plays |
|---|---|---|---|
| Music Player | `music_player` (new) | YouTube iframe player and the Spotify Web Playback SDK player | TTS, alerts, sounds |
| Bot Audio | `audio` (existing, renamed) | TTS, alert sounds, sound commands, other bot audio | music |

- Music output goes to ONE widget: the enabled widget with gallery key `music_player`. Lookup by gallery key only.
- `enableAudio` on any other widget is ignored for music. A custom widget never gets a music command.
- Before `Starting` on any engine that plays in the overlay: the director checks presence of the music widget.
  Missing → `Blocked("music widget not open")`, an error in the actionable-errors inbox, and one chat notice.
- Two `music_player` widgets present: the director picks the newest connection, tells the others `music.stop`,
  and logs `duplicate_output`.
- Each widget is its own OBS browser source. So OBS shows two mixer rows: "Music" and "Bot Audio".

### 3.2 Music level, mute and pause apart from bot audio

- The music widget has its own level and mute, set by `OwnerVolume` / `OwnerMute`. Bot audio has a separate level.
- Music mute sets the player volume to 0 and keeps it playing. Music pause pauses the engine. Neither touches bot audio.
- TTS ducking (if enabled) lowers the music widget level while TTS plays. It is a director action, so a
  ducked level never overwrites the owner's set level.

### 3.3 Hear music at home at a different level than the stream

Options:

| Option | How | Verdict |
|---|---|---|
| A. Local monitor player in sync | A second player in the dashboard tab plays the same track at the same position. | Rejected. Spotify plays on one device per account at a time; a second Spotify player takes the stream away from OBS. Sync drift on YouTube needs constant seeks. |
| B. OBS monitoring of the Music source | Music source set to "Monitor and Output". | Chosen, with a condition (below). One player, so no drift and no second Spotify device. |
| C. Spotify desktop app as the engine device | OBS captures the Spotify app with application audio capture. | Allowed for owner music only. The bot cannot reach a YouTube item that way. |

Condition for B: in plain OBS, the monitor plays the source after its volume fader (not checked; S-PD-9
proves it). Then the stream and the monitor hear the same level. Two ways to split them:
1. The streamer's monitor device has its own volume in the OS mixer (Windows: the OBS row in the volume mixer, or a
   separate output device). Simple; it changes all monitored sources together.
2. The exeldro "Audio Monitor" OBS plugin gives the Music source its own monitor volume. Per source; an extra plugin.

The setup guide lists both. The dashboard shows a one-time hint on the Music Player widget: "To hear music at a
different level, set the Music source to Monitor and Output in OBS".

What Spotify's own client does: Spotify plays on one active device per account. If the streamer opens Spotify on the
PC while the overlay player is the active device, playback moves to the PC app and the stream loses the music. The
director sees the device change in the poll (`device.id` differs) and reports `Blocked("Spotify moved to another
device")` with a one-click "Move back" (the existing transferDevice action). It never moves the device back by itself.

## 4. Rule: owner intent wins

- Owner pause, resume, skip and previous are inputs. They never cause a hand-over by themselves.
- Every control surface sends `OwnerPause` etc. to the director. Not to an engine.
- `music/resume` resumes the engine in the state. If the state is `Playing(youtube)` it never touches Spotify.
- Detection in the Spotify app (no API call from us):
  - The director keeps `expected` = what it last told Spotify (play or pause) and when.
  - A Spotify report of `isPlaying=false` with the same uri, mid-track (progress < duration - 6 s),
    and no pause sent by us in the last 5 s = `OwnerPause`.
  - `isPlaying=true` while state is `PausedByOwner(spotify)` and we sent no resume = `OwnerResume`.
  - A uri change mid-track that we did not send = `OwnerSkip` (owner picked a track in Spotify).
    The new track is owner music: `Playing(spotify, null)`. The queue head stays.
- Detection in YouTube: the music widget reports PAUSED without a `music.pause` from us = `OwnerPause`.

## 5. Rule: no engine resumes while another is active

- Only the director resumes. It resumes only the engine of its state.
- `ResumeSpotifyAfterYouTubeAsync` runs only on `TrackEnded(youtube)` with an empty queue,
  and only if Spotify was playing owner music before the YouTube item started (remembered in state).
  The same rule holds the other way: owner music on YouTube is resumed after a Spotify item on the same terms.
- Hand-over happens only at `TrackEnded`, `OwnerSkip`, `OwnerPrevious`, or the near-end window of owner music.
- A new request never goes to an engine unless it is the head and the state is `Idle` or a hand-over.
  This removes the jump in incident 3 (`HandOverAdmittedIfIdleAsync` must route through the director).
- Exactly one queue item is ever at an engine. We never push into Spotify's own queue; we start
  the head with `PlayNowAsync` (PUT /me/player/play {uris}).

## 6. Rule: only the owner of state changes the queue

- The queue is `FairQueue<T>` (Bamo's algorithm, music-sr.md §3.8/§3.9). The director reads its head; it never
  reorders. One queue holds both engines' items; engine is a field of the item, not a separate queue.
- An item leaves the queue only when the director sees it play as the head (`Starting → Playing`),
  or by an explicit remove (owner, mod, requester), or when it fails to start twice (`Blocked`, then dropped with a notice).
- `FairQueue.RemoveThrough` is deleted. No removal by a match at any depth.
- A report of a queued track that is not the head (Spotify played it on its own) removes that one row only
  and logs `played_out_of_order`. Rows above it stay.
- The reconciler stops changing the queue. It becomes a report adapter that sends `EngineReport` / `TrackEnded`.

## 7. Test matrix (scenario tests, Infrastructure.Tests/Music/PlaybackDirectorTests)

Each test drives inputs into the director with fake engines and a fake clock. It asserts the final state,
the engine calls made (and not made), the queue rows, the `music.nowPlaying` payload, and the journal transition events.
Every control test (marked E) runs twice: once with the playing item on Spotify, once on YouTube. Same assertions.

| # | Scenario | Expected |
|---|---|---|
| 1 | 16 items, Spotify reports item 16 playing | 15 rows stay; only row 16 removed; `played_out_of_order` |
| 2 | Push+skip path: start head | one `PlayNowAsync(head)`; no push, no skip call |
| 3 | YouTube head waits on owner Spotify music; Spotify request arrives | Spotify request stays #2; YouTube head starts at near-end; nothing removed early |
| 4 | Two widgets have `enableAudio`; one has gallery key `music_player` | music commands only to `music_player` |
| 5 | YouTube plays; dashboard resume | no Spotify call; YouTube resumes if paused |
| 6 | Owner Spotify music; YouTube head waits; owner pauses in dashboard | `PausedByOwner`; YouTube not started on 10 poll ticks |
| 7 | Same as 6, pause detected from Spotify report | same as 6 |
| 8 | PausedByOwner; owner resumes in Spotify | `Playing(spotify)`; queue untouched |
| 9 | PausedByOwner; owner skips | head starts |
| 10 | YouTube plays; owner pauses in the widget | `PausedByOwner(youtube)`; Spotify untouched |
| 11 | Spotify starts on its own while YouTube plays | Spotify paused; `foreign_engine_silenced` |
| 12 | YouTube ends, queue empty, Spotify was playing before | Spotify resumes once |
| 13 | YouTube ends, queue empty, Spotify was paused by owner before | Spotify stays paused |
| 14 | Music widget missing at start | `Blocked`; error in inbox; head kept |
| 15 | Music widget attaches while `Blocked` | head starts |
| 16 | Start sent, no report in 10 s | `Blocked("did not start")`; head kept |
| 17 | Owner picks a non-queue track in Spotify mid-request | `Playing(spotify, null)`; head kept |
| 18 E | Pause, resume, skip, seek, volume from dashboard | same state, same engine-call count, same journal events for both engines |
| 19 E | Same controls through the Automation API (Stream Deck path) | identical to 18 |
| 20 E | Same controls through chat commands and pipeline actions | identical to 18 |
| 21 E | `music.nowPlaying` payload | every field filled for both engines; `Engine` differs only |
| 22 E | Previous at progress 10 s / at 1 s | seek to 0 / last played item back at head and started; queue order below unchanged |
| 23 | Queue: Spotify A (user1), YouTube B (user2), Spotify C (user1); A ends | B starts on YouTube after A's `TrackEnded`; no fixed delay; then C; order equals the FairQueue order |
| 24 | Spotify item ends; YouTube head; Spotify never reports paused | YouTube starts at 2 s; `stop_unconfirmed` logged |
| 25 | Music mute while TTS plays | music level 0, music still plays; bot audio level unchanged; no TTS call |
| 26 | TTS ducking ends after owner changed the volume mid-TTS | music returns to the owner's new level |
| 27 | TTS played: which widget gets it | only `audio` (Bot Audio); `music_player` gets no TTS |
| 28 | Spotify device changes to the PC app while the overlay was active | `Blocked("Spotify moved to another device")`; no automatic transfer |
| 29 | Save-track on a YouTube item | `NotSupportedForEngine`; no Spotify call |
| 30 | Source-scan guard | fails when a control surface or widget names an engine at a control call |

## 8. Implementation plan (one builder item each)

Lane notes: a "P" mark means the item may run in parallel with the others marked P in the same step.
Within the 2-builder limit.

| Step | Item | Depends on | Tests |
|---|---|---|---|
| 0 | **S-PD-0 Engine audit.** Read-only: list each `IMusicProvider` member for YouTube and Spotify (real / no-op). Output: a table in this spec. | none | none |
| 1 | **S-PD-1 Director core.** State machine, inputs, transition table, per-channel lock, `PlaybackTransitionedEvent`, played history. Pure, fake engines. | 0 | 1, 2, 6, 8, 9, 12, 13, 16, 17, 22, 23, 24 |
| 2 P | **S-PD-2 Engine interface.** `IPlaybackEngine` with both adapters over the existing providers; fill the gaps from S-PD-0. | 1 | 18 (director level), 21 |
| 2 P | **S-PD-3 Queue ownership.** Delete `RemoveThrough`; reconciler becomes a report adapter; removal only by the director. | 1 | 1, 3 end to end with the real `FairQueue` |
| 3 | **S-PD-4 Single entry point.** Route all six `HandOverNextAsync` callers and `HandOverAdmittedIfIdleAsync` through the director; delete the poll-tick recovery. Engine-call guard. | 2, 3 | 30 |
| 4 P | **S-PD-5 Control surfaces.** REST, Automation API, chat commands, pipeline actions send director inputs only. `music.nowPlaying` event + `GET music/now-playing`. | 4 | 18, 19, 20, 29 |
| 4 P | **S-PD-6 Owner intent detection.** Spotify and YouTube owner-action detection; Spotify device change. | 4 | 5, 7, 10, 11, 28 |
| 5 P | **S-PD-7 Music Player widget.** New first-party `music_player` widget hosting the YouTube player and the Web Playback SDK; gallery-key-only output; presence; duplicate output. | 5 (S-PD-5) | 4, 14, 15 |
| 5 P | **S-PD-8 Bot Audio split.** The `audio` widget plays bot audio only; rename to "Bot Audio"; separate music level, mute, ducking. | 5 (S-PD-5) | 25, 26, 27 |
| 6 | **S-PD-9 Migration.** See 9. | 7, 8 | migration test per channel shape |
| 7 P | **S-PD-10 Now-playing widgets.** First-party now-playing widgets read `music.nowPlaying` only; widget SDK gets the same event. | 5 | 21 in the widget test |
| 7 P | **S-PD-11 Dashboard state.** Music page and mini player show the director state ("Paused by you", "Blocked: music widget not open"), music level and mute apart from bot audio; Stream Deck key states from `music.nowPlaying`. | 5 | dashboard view tests |
| 8 | **S-PD-12 Live proof.** On the box, with throwaway queue items only: the six incidents, an engine switch both ways, every Stream Deck control on both engines, the OBS monitor level check (3.3). Journal read back. | all | live |

## 9. Migration for existing channels

Runs once per channel (a run-once guard by DB provider; both migration sets if a column changes).

1. Every channel gets one `music_player` widget row, created disabled-for-music until its first OBS attach.
   Name "Music Player". It is a new widget, so OBS needs a new browser source: the dashboard shows an
   actionable-errors entry "Add the Music Player to OBS" with the URL and a copy button.
2. Until that widget attaches, the existing `audio` widget keeps playing music (the current behavior), so no
   channel loses music on deploy. The first `music_player` attach moves music over; `audio` then plays bot audio only.
3. The existing `audio` widget (first-party): default name changes to "Bot Audio" only where the row still has the
   default name "TTS Audio". A name the streamer changed stays.
4. Custom widgets with `enableAudio=true`: nothing is changed in their row. They never got music after S-PD-7
   (gallery-key rule). The dashboard lists them once: "These custom widgets have audio on: <names>. Music now plays
   in the Music Player."
5. Stoney's channel: "Spotify NowPlaying" (019f7867-...) already has `enableAudio=false` (backup in the session
   scratchpad). After S-PD-10 it reads `music.nowPlaying`, so it shows YouTube items too. If his custom code calls
   Spotify directly, it keeps working for Spotify only; the migration does not edit custom code. S-PD-9 reports it.
6. Existing music volume (if stored on the `audio` widget settings) is copied to the music level; the bot audio
   level keeps the old value. Not checked where the volume lives today; S-PD-0 finds it.

## 10. Safe steps until the director ships

1. Keep `enableAudio=true` on "TTS Audio" (01a03a23-...). It is the gallery `audio` widget, the only one the
   YouTube dispatcher targets today (`YouTubePlayerDispatcher.cs`, OverlayHub.cs:192-197). Undoing it stops YouTube songs.
2. Pause with the Spotify app or dashboard ONLY when the queue is empty. With a YouTube head waiting, a pause starts it.
3. Do not press dashboard resume while a YouTube song plays. It resumes Spotify on top.
4. Ask Stoney which OBS source is "the new audio source". If it is a separate browser source of the same overlay,
   both may load the audio widget. Presence was not checked.

## 11. S-PD-0 audit result

Read from the code on 2026-10-18, branch master. No code changed. Paths are under `server/src/`.
Legend: real = does the work. no-op = returns without effect. throws = raises on a known failure.
Interface files in `NomNomzBot.Domain/Music/Interfaces/`: `IMusicProvider.cs` (IMP), `IMusicProviderPlayNow.cs` (PlayNow), `IMusicProviderStartTrack.cs` (StartTrack).
Provider files in `NomNomzBot.Infrastructure/Music/`: `SpotifyMusicProvider.cs` (SP), `YouTubeMusicProvider.cs` (YT).
SP implements IMusicProvider, IMusicRemoteProvider, IMusicProviderManageApi, IMusicProviderStartTrack (SP:56-60). YT implements IMusicProvider, IMusicProviderManageApi, IMusicProviderPlayNow, IMusicProviderPlayerGate (YT:58-63).

### 11.1 (a) Member table

| Member (interface line) | Spotify | YouTube |
|---|---|---|
| `Provider`, `Capabilities` (IMP:168, 171) | real (SP:128, 176) | real (YT:109, 117) |
| `PlayAsync` (IMP:181) | real: PUT /me/player/play; silent no-op when the token is null (SP:193-207) | real: `youtube.resume` to the player; throws `NoActiveDeviceException` with no player (YT:143-147, 193-203) |
| `PauseAsync` (IMP:183) | real (SP:209) | real: `youtube.pause`; throws with no player (YT:149) |
| `SkipAsync` (IMP:185) | real (SP:225) | real: calls the dispatcher `HandOverNextAsync`, else `StopAsync` (YT:155-163) |
| `PreviousAsync` (IMP:188) | real: POST /me/player/previous; silent no-op when the token is null (SP:241-258) | no-op, logs debug (YT:205-211) |
| `SetVolumeAsync` (IMP:191) | real: PUT /me/player/volume; silent no-op when the token is null (SP:260-279) | real: `youtube.volume`; throws with no player (YT:213-221) |
| `SeekAsync` (IMP:198) | real (SP:696-716) | real: `youtube.seek`; throws with no player (YT:223-231) |
| `SetShuffleAsync` (IMP:205) | real (SP:718) | no-op (YT:233-241) |
| `SetRepeatAsync` (IMP:212) | real (SP:740) | no-op (YT:243-251) |
| `GetDevicesAsync` (IMP:219) | real; returns [] when the token is null (SP:797-804) | no-op, returns [] (YT:253-260) |
| `TransferPlaybackAsync` (IMP:225) | real; throws `DeviceTransferFailedException` on a rejected transfer (SP:767-795) | no-op (YT:262-271) |
| `GetCurrentTrackAsync` (IMP:240) | real: GET player state (SP:281) | real: built from the player report and the pushed track, no Data API call; CanSkipPrevious, CanSetShuffle, CanSetRepeat false (YT:273-339) |
| `SearchAsync` (IMP:255) | real (SP:347) | real (YT:341) |
| `ResolveTrackAsync` (IMP:269) | real (SP:428) | real (YT:393) |
| `OwnsLink`, `OwnsTrackUri` (IMP:277, 281) | real (SP:421, 425) | `OwnsLink` real (YT:391); `OwnsTrackUri` uses the interface default |
| `AddToQueueAsync` (IMP:283) | real: POST /me/player/queue; throws `MusicAuthenticationFailedException`, `NoActiveDeviceException`, `MusicForbiddenException` (SP:578-605, 631-652) | real, different meaning: plays now if the player is idle, else fills a one-slot "next" buffer; with no player it stores the track in that buffer and answers true (YT:483-518) |
| `GetQueueAsync` (IMP:298) | real: GET /me/player/queue, null when it cannot answer (SP:664) | real: only the one-slot "next" buffer (YT:536-559) |
| `GetEmbeddedPlaybackTokenAsync` (IMP:311) | real (SP:530) | no-op, returns null (YT:562-569) |
| `TryGetCoolingUntil` (IMP:326) | real (SP:131) | real: Data API quota (YT:131) |
| `WorksWithoutConnectionAsync` (IMP:178) | interface default, false | real, true (YT:140) |
| `PlayNowAsync` (PlayNow:415) | not implemented | real: finds the player, sends `youtube.play`, marks pushed; returns false with no video id or no player (YT:170-191) |
| `StartTrackAsync` (StartTrack:443) | real: PUT /me/player/play {uris}; same exceptions as AddToQueue (SP:607-662) | not implemented |

### 11.2 Gaps the S-PD-2 adapters must fill

1. Start: Spotify has only `StartTrackAsync`. YouTube has only `PlayNowAsync`. The adapters map `Start(item)` to each. Failure differs: SP throws on no device or a dead token, YT returns false on no player. The adapter must turn both into one typed result.
2. Missing precondition differs. SP returns silently when the token is null (SP:196-197, 244-245, 263-264). YT throws `NoActiveDeviceException` with no player (YT:200-201). Both must become the same `Blocked` reason.
3. Previous: YT is a no-op. Spec 2.1 puts Previous in the director, so the engine needs only `Seek(0)` and `Start(item)`. SP's POST /me/player/previous must not be called.
4. Stop: no member exists. SP has no stop (use pause). YT has `StopAsync` only on the dispatcher (`youtube.stop`), reached from `SkipAsync` (YT:160-162). The adapters must add `Stop()`.
5. Resume: SP `PlayAsync` sends PUT /me/player/play with no body. YT `PlayAsync` sends `youtube.resume`. Both must be tested as "resume this engine only".
6. Reports: SP is a poll (`GetCurrentTrackAsync`, `MusicStatePollingService`). YT is a push (`YouTubePlayerReportService`, plus a heartbeat in `audio.vue`). YT ended comes from `YouTubePlayerReportService.cs:78-80`. SP has no ended signal; the adapter must derive it (spec 2.1 Inputs).
7. Shuffle, Repeat, Devices, Transfer, embedded token: YT no-op. They are not director inputs. The adapter returns a typed "not supported"; the dashboard gates on `Capabilities`.
8. YT `SkipAsync` calls the dispatcher `HandOverNextAsync` (YT:160). That is one of the six hand-over callers and must go through the director.
9. `AddToQueueAsync` on both engines must not be called by the director (spec 5: start the head with Start).

### 11.3 (b) Hand-over callers (as of now)

Two methods shared the name. `ISongRequestHandover.HandOverNextAsync(string)` (DELETED in S-PD-4c2b B2; every caller now sends a director input) (declared `Music/SongRequestQueueStore.cs:48`, implemented `Music/MusicService.cs:2594`) takes the fair-queue head to the engine. `IYouTubePlayerDispatcher.HandOverNextAsync(Guid)` (declared `Music/YouTubePlayerDispatcher.cs:71`, implemented `:189`) hands the YouTube one-slot buffer to the player.

| # | Caller | Method called | Trigger |
|---|---|---|---|
| 1 | `NomNomzBot.Api/Hubs/OverlayHub.cs:488` | dispatcher | audio widget ready, when `ReplayCurrentAsync` replayed nothing |
| 1b | `NomNomzBot.Api/Hubs/OverlayHub.cs:489` | ~~`ISongRequestHandover`~~ | same ready event, always. GONE in S-PD-4c2b B2: sends the director `OutputAttached` |
| 2 | `NomNomzBot.Infrastructure/BackgroundServices/MusicStatePollingService.cs:277` | ~~`ISongRequestHandover`~~ | every poll tick (recovery tick). GONE in S-PD-4c2b B2: sends the director `RequestAdded` |
| 3 | `NomNomzBot.Infrastructure/Music/SongRequestQueueReconciler.cs:168` | ~~`ISongRequestHandover`~~ | playback change, when nothing is in flight. GONE in S-PD-4c2b B2: the reconciler no longer starts songs; the director does |
| 4 | `NomNomzBot.Infrastructure/Music/YouTubePlayerReportService.cs:80` | dispatcher | player reported ended or error |
| 5 | `NomNomzBot.Infrastructure/Music/PlayOnceResumeHandler.cs:108` | dispatcher | play-once track ended |
| 6 | `NomNomzBot.Infrastructure/Music/YouTubeMusicProvider.cs:160` | dispatcher | `SkipAsync` |
| - | `NomNomzBot.Infrastructure/Music/MusicService.cs:1192` calls `HandOverAdmittedIfIdleAsync` (defined `:1221`) | ~~private; the only call site~~ | a new request is admitted. GONE in S-PD-4c2b B2: `MusicService` sends the director `RequestAdded` (`MusicService.cs:1247`) |

Count (before B2): 6 caller files, 7 call lines (OverlayHub has two). Section 1 matches. After B2 only the dispatcher callers (rows 1, 4, 5, 6) remain; the `ISongRequestHandover` callers (rows 1b, 2, 3 and the `MusicService` line) are deleted and send director inputs.

### 11.4 (c) Where the music volume lives today

- There is no stored music volume setting. The `audio` widget defaults (`NomNomzBot.Infrastructure/Content/Widgets/FirstPartyWidgetCatalogue.cs:259-267`) hold `showIndicator`, `accentColor`, `enableAudio` only. `WidgetSettingsSchemaProvider.cs` has no "volume" match.
- Spotify: the volume is on the Spotify device. `SetVolumeAsync` sends PUT /me/player/volume (SP:260-279). The level reads back in `TrackInfo.VolumePercent` (IMP:369). The in-widget Web Playback SDK player is created with `volume: 1.0` (`Content/Widgets/Assets/audio.vue:325`).
- YouTube: a page variable `ytVolume`, default 100 (`audio.vue:79`). The `youtube.volume` event sets it (`audio.vue:213-217`); each start applies it (`audio.vue:126`). It is not persisted: a reload resets it to 100. The server sends it from `YouTubePlayerDispatcher.VolumeAsync` (`:171`).
- Routing: `MusicService.SetVolumeAsync` (`MusicService.cs:1377`) picks one provider with `VolumeProviderAsync` (`:1422`: the last engine from the queue store, else the active provider) and sets only that engine.
- Mute memory: `MuteVolumeMemory` keeps the pre-mute level in the cache, key `music:premute:{broadcasterId}`, TTL 24 h (`Music/MuteVolumeMemory.cs`). It is not a setting.
- REST entry: `PUT music/volume` (`NomNomzBot.Api/Controllers/V1/MusicController.cs:525`).
- For spec 9.6: there is nothing to copy from the `audio` widget settings. The music level needs a new stored value. The migration seeds it with 100.

### 11.5 (d) Where the music output widget is picked

1. `YouTubePlayerDispatcher.FindPlayersAsync` (`Music/YouTubePlayerDispatcher.cs:106-120`): asks `presence.GetAudioWidgetId`, then checks the `Widgets` row is enabled for that broadcaster. Returns zero or one widget id. Callers: `YouTubeMusicProvider` (YT:165-168, 180, 198, 498), the dispatcher's own methods (`:198, 212, 227`), `YouTubePlayerReportService.cs:40`.
2. `OverlayPresenceRegistry.GetAudioWidgetId` (`NomNomzBot.Api/Hubs/Overlay/OverlayPresenceRegistry.cs:102-111`): the audio-source connection with the highest join order wins. `GetAudioTarget` (`:92-100`) does the same for the connection id.
3. `OverlayHub.cs:192-197` (join): a widget is the audio source when `WidgetKeyAliases.Canonical(naturalKey) == OverlayPresenceRegistry.AudioSourceNaturalKey`, that is the gallery key `audio`. It does not read `enableAudio`. A new audio source sends the old one `youtube.stop { handoff: true }`.
4. `OverlayHub.cs:366-368` (player state report) and `:476-478` (widget ready): both accept only the current audio target and widget id.
5. `NomNomzBot.Api/Controllers/OverlayHostController.cs:169-170` (`WantsSpotifyPlayback`): the page CSP allows player frames when the gallery key is `audio` OR the widget setting `enableAudio` is true. This is the only server code that reads `enableAudio` (other hits: `FirstPartyWidgetCatalogue.cs:266`, `WidgetSettingsSchemaProvider.cs:194`).
6. `audio.vue:19, 124, 387, 406`: the widget makes YouTube audible and connects the Spotify SDK only when its own `cfg.enableAudio` is true.
7. Bot audio (TTS) goes to the same widget by `WidgetNotifier.SendAudioWidgetEventAsync` (`NomNomzBot.Api/Hubs/WidgetNotifier.cs:114`, used by `WidgetEventNotifierAdapter.cs:43`). Which events take that path: not checked.

For spec 3.1: the `audio` gallery key is the only output key today, for music and for bot audio. The `music_player` key must change sites 1, 3 and 4. Sites 5 and 6 need the new key added.
