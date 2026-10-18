# Music playback determinism

Status: design, 2026-10-18. No code yet.

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

## 2. Rule: one playback owner per channel

- One class owns playback for a channel: `PlaybackDirector` (one instance state per channel).
- It is the ONLY code that starts, pauses, resumes, skips or hands over an engine.
- Every other code sends it an input event. No other code calls a provider's play, pause or push.
- Inputs are handled one at a time per channel (a per-channel lock or channel queue).
- Every transition writes one journal event: `PlaybackTransitionedEvent { From, To, Input, Engine, QueueItemId, Reason }`.

### States

| State | Meaning |
|---|---|
| `Idle` | Nothing plays. Queue empty or no engine ready. |
| `Starting(engine, item)` | A start was sent. We wait to SEE it play. Deadline 10 s. |
| `Playing(engine, item?)` | An engine plays. `item` is the queue head, or null for the owner's own music. |
| `PausedByOwner(engine, item?)` | The owner paused. Nothing starts until the owner acts. |
| `Blocked(reason)` | A start is needed but impossible (no audio widget, token dead). Visible error. |

### Inputs

| Input | Source |
|---|---|
| `RequestAdded` | Song request admitted. |
| `EngineReport(engine, uri, isPlaying, progress)` | Spotify poll, YouTube player report. |
| `TrackEnded(engine, uri)` | YouTube ENDED; Spotify: uri changed or progress reset after near-end. |
| `OwnerPause / OwnerResume / OwnerSkip` | Dashboard, chat command, or detected in Spotify (see 4). |
| `OutputAttached / OutputDetached` | Audio widget presence. |
| `StartTimeout` | `Starting` deadline passed. |

### Transitions (the full table; anything not listed is ignored and logged)

| From | Input | To | Action |
|---|---|---|---|
| Idle | RequestAdded, head ready | Starting(head.engine, head) | start head on its engine |
| Idle | EngineReport playing (owner music) | Playing(engine, null) | none |
| Starting(e, i) | EngineReport e plays i.uri | Playing(e, i) | mark i as played (see 6) |
| Starting(e, i) | StartTimeout | Blocked("did not start") | keep i at head; show error |
| Playing(e, null) | RequestAdded | Playing(e, null) | none until near end (6 s) of the owner track |
| Playing(e, null) | near end, head waits | Starting(head.engine, head) | pause e only if head.engine != e |
| Playing(e, i) | TrackEnded(e, i.uri) | Starting(next) or Idle | hand over to the head |
| Playing(e, i) | OwnerSkip | Starting(next) or Idle | stop e, then start head |
| Playing(e, *) | OwnerPause | PausedByOwner(e, *) | pause e |
| PausedByOwner(e, *) | OwnerResume | Playing(e, *) | resume e ONLY |
| PausedByOwner(e, *) | RequestAdded, EngineReport paused, poll tick | PausedByOwner | none |
| PausedByOwner(e, *) | OwnerSkip | Starting(next) or Idle | start head |
| any | EngineReport of an engine that is not the owner engine, playing | same | pause that engine; log `foreign_engine_silenced` |
| Blocked | OutputAttached / token restored | Idle | re-evaluate |

The poll tick is NOT an input. A poll only produces `EngineReport`. The recovery tick at
MusicStatePollingService.cs:277 goes away; `Blocked` plus `OutputAttached` replace it.

## 3. Rule: exactly one output target

- In-overlay playback (YouTube, and Spotify Web Playback if used) goes to ONE widget: the enabled
  widget with gallery key `audio`. Lookup by gallery key only, never by `enableAudio`.
- `enableAudio` on any other widget is ignored for music. A custom widget never gets `youtube.play`.
- Before `Starting` on YouTube: the director checks presence of that widget.
  Missing → `Blocked("audio widget not open")`, a dashboard error in the actionable-errors inbox, and one chat notice.
- If two `audio` widgets are present, the director picks the newest connection and tells the
  others `youtube.stop`. It logs `duplicate_output`.
- Rename the first-party widget's default name to "Audio" (it plays music and TTS). Existing rows keep their name;
  the dashboard shows the gallery label beside it.

## 4. Rule: owner intent wins

- Owner pause, resume and skip are inputs. They never cause a hand-over by themselves.
- Dashboard and chat: the endpoint sends `OwnerPause` etc. to the director. Not to the provider.
- `music/resume` resumes the engine in the state. If the state is `Playing(youtube)` it never touches Spotify.
- Detection in the Spotify app (no API call from us):
  - The director keeps `expected` = what it last told Spotify (play or pause) and when.
  - A Spotify report of `isPlaying=false` with the same uri, mid-track (progress < duration - 6 s),
    and no pause sent by us in the last 5 s = `OwnerPause`.
  - `isPlaying=true` while state is `PausedByOwner(spotify)` and we sent no resume = `OwnerResume`.
  - A uri change mid-track that we did not send = `OwnerSkip` (owner picked a track in Spotify).
    The new track is owner music: `Playing(spotify, null)`. The queue head stays.
- Detection in YouTube: the widget reports PAUSED without a `youtube.pause` from us = `OwnerPause`.

## 5. Rule: no engine resumes while another is active

- Only the director resumes. It resumes only the engine of its state.
- `ResumeSpotifyAfterYouTubeAsync` runs only on `TrackEnded(youtube)` with an empty queue,
  and only if Spotify was playing owner music before the YouTube item started (remembered in state).
- Hand-over happens only at `TrackEnded`, `OwnerSkip`, or the near-end window of owner music.
- A new request never goes to a provider unless it is the head and the state is `Idle` or a hand-over.
  This removes the jump in incident 3 (`HandOverAdmittedIfIdleAsync` must route through the director).
- Exactly one queue item is ever at a provider. We never push into Spotify's own queue; we start
  the head with `PlayNowAsync` (PUT /me/player/play {uris}).

## 6. Rule: only the owner of state changes the queue

- An item leaves the queue only when the director sees it play as the head (`Starting → Playing`),
  or by an explicit remove (owner, mod, requester), or when it fails to start twice (`Blocked`, then dropped with a notice).
- `FairQueue.RemoveThrough` is deleted. No removal by a match at any depth.
- A report of a queued track that is not the head (Spotify played it on its own) removes that one row only
  and logs `played_out_of_order`. Rows above it stay.
- The reconciler stops changing the queue. It becomes a report adapter that sends `EngineReport` / `TrackEnded`.

## 7. Test matrix (scenario tests, Infrastructure.Tests/Music/PlaybackDirectorTests)

Each test drives inputs into the director with fake engines and a fake clock. It asserts the final state,
the provider calls made (and not made), the queue rows, and the journal transition events.

| # | Scenario | Expected |
|---|---|---|
| 1 | 16 items, Spotify reports item 16 playing | 15 rows stay; only row 16 removed; `played_out_of_order` |
| 2 | Push+skip path: start head | one `PlayNowAsync(head)`; no push, no skip call |
| 3 | YouTube head waits on owner Spotify music; Spotify request arrives | Spotify request stays #2; YouTube head starts at near-end; nothing removed early |
| 4 | Two widgets have `enableAudio`; one has gallery key `audio` | `youtube.play` only to the `audio` widget |
| 5 | YouTube plays; dashboard resume | no Spotify call; YouTube resumes if paused |
| 6 | Owner Spotify music; YouTube head waits; owner pauses in dashboard | `PausedByOwner`; YouTube not started on 10 poll ticks |
| 7 | Same as 6, pause detected from Spotify report | same as 6 |
| 8 | PausedByOwner; owner resumes in Spotify | `Playing(spotify)`; queue untouched |
| 9 | PausedByOwner; owner skips | head starts |
| 10 | YouTube plays; owner pauses in the widget | `PausedByOwner(youtube)`; Spotify untouched |
| 11 | Spotify starts on its own while YouTube plays | Spotify paused; `foreign_engine_silenced` |
| 12 | YouTube ends, queue empty, Spotify was playing before | Spotify resumes once |
| 13 | YouTube ends, queue empty, Spotify was paused by owner before | Spotify stays paused |
| 14 | Audio widget missing at start | `Blocked`; error in inbox; head kept |
| 15 | Audio widget attaches while `Blocked` | head starts |
| 16 | Start sent, no report in 10 s | `Blocked("did not start")`; head kept |
| 17 | Owner picks a non-queue track in Spotify mid-request | `Playing(spotify, null)`; head kept |

## 8. Implementation plan (one builder item each, in order)

1. **S-PD-1 Director core.** `PlaybackDirector` state machine, states, inputs, transition table, per-channel lock,
   `PlaybackTransitionedEvent`. Pure, with fake engines. Tests 1, 2, 6, 8, 9, 12, 13, 16, 17.
2. **S-PD-2 Queue ownership.** Delete `RemoveThrough`; the reconciler becomes a report adapter; queue removal only
   by the director. Test 1, 3 end to end with the real `FairQueue`.
3. **S-PD-3 Single entry point.** Route all six `HandOverNextAsync` callers and `HandOverAdmittedIfIdleAsync`
   through the director. Delete the poll-tick recovery call. A guard test fails if a provider play/pause/push
   is called outside the director (reflection or a source scan test).
4. **S-PD-4 Owner intent.** Dashboard and chat pause/resume/skip become director inputs; Spotify and YouTube
   owner-action detection. Tests 5, 7, 10, 11.
5. **S-PD-5 One output.** Gallery-key-only output, presence check, `Blocked` with an inbox error, duplicate output.
   Tests 4, 14, 15.
6. **S-PD-6 Dashboard state.** The music page shows the director state ("Paused by you", "Blocked: audio widget not open").
7. **S-PD-7 Live proof.** On the box, with a throwaway queue item: the six incident scenarios by hand, journal read back.

## 9. Safe steps for tonight

1. Keep `enableAudio=true` on "TTS Audio" (01a03a23-...). Evidence: it is the gallery `audio` widget, the only one
   the YouTube dispatcher targets (`YouTubePlayerDispatcher.cs`, OverlayHub.cs:192-197). Undoing it stops YouTube songs.
   It is not the wrong widget; only its name is misleading.
2. Set `enableAudio=false` on "Spotify NowPlaying" (019f7867-58fa-...) if Stoney does not need it to play sound.
   It is a custom widget and a possible second player. Back up the Settings value first and read it back.
   Ask Stoney first: it is his real channel row.
3. Until S-PD-4 ships: pause with the Spotify app or dashboard ONLY when the queue is empty. With a YouTube head
   waiting, a pause starts it (cause above).
4. Do not press dashboard resume while a YouTube song plays. It resumes Spotify on top.
5. Ask Stoney which OBS source is "the new audio source". If it is a separate browser source of the same
   overlay, both may load the audio widget. Presence was not checked tonight.
