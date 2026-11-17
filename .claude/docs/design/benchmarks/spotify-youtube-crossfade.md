# Benchmark: crossfade between Spotify and YouTube requests

Owner request: `.claude/docs/design/requests/2026-11-17-music-favorites-sponsorblock-crossfade.md`, part 2.
Stoney, 2026-11-10: "i have a crossfade on spotify of 3 seconds"; 2026-11-17: a custom crossfade between Spotify and YouTube Music requests, both directions.

Scope: crossfade, fade curves, volume control and rate limits. Read 2026-11-17.

## Products and sources

| Product | Source (read this run) |
|---|---|
| Apple Music (iPhone) | https://support.apple.com/guide/iphone/transition-songs-iphadf2fe1f4/ios (opened in a real browser; a plain fetch returned only the navigation) |
| Mixxx 2.4 (Auto DJ and mixer) | https://manual.mixxx.org/2.4/en/chapters/djing_with_mixxx.html and https://manual.mixxx.org/2.4/en/chapters/user_interface.html |
| Spotify (Web API as the control surface) | https://developer.spotify.com/documentation/web-api/reference/set-volume-for-users-playback , https://developer.spotify.com/documentation/web-api/concepts/rate-limits , https://developer.spotify.com/documentation/web-api/reference/get-information-about-the-users-current-playback |
| YouTube IFrame Player API | https://developers.google.com/youtube/iframe_api_reference |
| Web Audio API (MDN) | https://developer.mozilla.org/en-US/docs/Web/API/AudioParam/linearRampToValueAtTime |
| Howler.js (fade API) | https://github.com/goldfire/howler.js |

Not sourced: the Spotify support article on its own crossfade setting. Every URL tried (`/article/crossfade/`, `/article/playback-settings/`, `/article/crossfade-and-gapless-playback/`) returned "page not found", also in a real browser. No Spotify app crossfade row is written from memory.
Not sourced: Plexamp. Its support pages returned 403 to a fetch and 0 search results in a browser.

## Feature table

Slices: XF-1 director owns the fade (state, tick, fallback), XF-2 overlay page `youtube.fade` ramp, XF-3 Spotify stepped volume ramp, XF-4 restore and fallback.

| Feature | Seen in | Source | Ours | Status | Slice |
|---|---|---|---|---|---|
| Crossfade: next song fades in while the previous fades out | Apple Music | Apple "Turn on Crossfade" | Spotify does it inside Spotify only; no Spotify to YouTube fade | partial | XF-1 |
| User picks the crossfade length with a slider | Apple Music | Apple "drag the slider to choose the length" | One channel value, 3 s (`HandOverLeadMs`) | have | - |
| Crossfade length in seconds set per mode | Mixxx | Mixxx Auto DJ "Full Track" | Same 3 s value used for both directions | have | XF-1 |
| Transition time of 0 s acts like a plain player | Mixxx | Mixxx Auto DJ "transition time of 0 seconds" | A 0 s value skips the fade and keeps today's hand-over | missing | XF-1 |
| Crossfade between catalog and other sources | Apple Music | Apple AutoMix applies to Apple Music catalog only | Spotify to YouTube fade | missing | XF-1, XF-2, XF-3 |
| Fade out of the old song and fade in of the new one run at the same time | Mixxx | Mixxx crossfader "Sets each deck's volume as it moves" | Planned hand-over starts the video, then cuts Spotify | partial | XF-1 |
| Two decks, each with its own volume | Mixxx | Mixxx "Line faders" control each deck's volume | Two engines, each with its own `SetVolumeAsync` | have | - |
| Next song never audible before its turn | Owner goal | `music-gapless-handover-goal-2026-11-10` | Plan runs ahead of the track end; Spotify starts at 0 on the way back | partial | XF-1 |
| Albums play without transitions | Apple Music | Apple AutoMix note "Albums and some genres play without transitions" | Not applicable: the fade applies only at a Spotify and YouTube switch | not-doing | - |
| Automatic choice of transition type (trim silence, simple fade) | Apple Music | Apple "About AutoMix" | Fixed crossfade only. AutoMix needs beat analysis the platform does not have for YouTube audio | not-doing | - |
| Skip silence at track edges | Mixxx | Mixxx Auto DJ "Skip Silence" (-60 dBFS) | SponsorBlock music section already trims the video (a68a6b0ae); Spotify songs are not trimmed | not-doing | - |
| Fade starts at the outro | Mixxx | Mixxx "Fade At Outro Start" | Fade starts a fixed 3 s before the end; no outro analysis | not-doing | - |
| Crossfade unavailable in some audio settings | Apple Music | Apple note: not available with Hi-Res Lossless | Not applicable: browser audio has no such mode | not-doing | - |
| Volume range 0 to 100 integer | YouTube IFrame | `setVolume(volume)` 0 to 100 | Page applies integer percent | have | - |
| Read the current volume | YouTube IFrame | `getVolume()` | Page keeps its own `ytVolume`; the director holds the owner level | have | - |
| Volume readable while muted | YouTube IFrame | `getVolume()` "even if the player is muted" | Mute is a separate flag in the page level | have | - |
| Playback blocked until a user gesture | YouTube IFrame | `onAutoplayBlocked` | OBS browser source has no gesture block; start failure falls back | have | XF-4 |
| Per-frame volume ramp in the browser | Web Audio | `linearRampToValueAtTime` | Page ramps `setVolume` with `requestAnimationFrame` (IFrame has no audio graph) | missing | XF-2 |
| Linear fade curve | Web Audio | `linearRampToValueAtTime` | Linear ramp | missing | XF-2 |
| Equal-power or exponential curve | Web Audio | MDN "exponentialRampToValueAtTime ... more natural" | Linear only; two players mixed by volume sum, a curve is a later polish | not-doing | - |
| Fade with from, to and duration in one call | Howler.js | `fade(from, to, duration)` | `youtube.fade` event with `to` and `durationMs` | missing | XF-2 |
| Completion event when the fade ends | Howler.js | Howler `fade` event | Page sets the final level at the end; no event is sent back | not-doing | - |
| Cancel a running fade when the level changes | Howler.js | `fade` on a playing sound replaces the prior fade | New `youtube.volume` or `youtube.fade` cancels the running ramp | missing | XF-2 |
| Set playback volume over the API | Spotify | `PUT /v1/me/player/volume`, `volume_percent` 0 to 100 | `SpotifyPlaybackEngine.SetVolumeAsync` | have | - |
| Volume call needs Premium and `user-modify-playback-state` | Spotify | Set Playback Volume page | Existing token scope; failure falls back | have | XF-4 |
| Order of calls is not guaranteed across Player endpoints | Spotify | Set Playback Volume "order of execution isn't guaranteed" | Ramp steps are one second apart and the resume is awaited first | partial | XF-3 |
| Device may not support volume | Spotify | playback state `device.supports_volume` | A refused volume call ends the fade and falls back | missing | XF-4 |
| Read device volume to restore it later | Spotify | playback state `device.volume_percent` | Director level (`EffectiveVolume`) is the restore value; no extra read | have | XF-3 |
| Crossfade length not reported by the API | Spotify | Playback state has no `crossfade` field | Channel setting stays the source (3 s) | have | - |
| Rate limit is a rolling 30 s window with 429 and `Retry-After` | Spotify | Rate limits page | Steps are one per second at most: 3 s fade is 3 calls plus 1 restore | missing | XF-3 |
| Lower limit for development mode apps | Spotify | Rate limits page | Same small step count for every channel | missing | XF-3 |
| Restore the original volume after the fade | Mixxx | Mixxx "crossfader will stay on its last position" (state stays after Auto DJ) | Last step is the owner level; failure sends the owner level once | missing | XF-4 |
| Fall back to a plain switch when the fade fails | Owner rule | `youtube-must-never-break-spotify` | Today's hand-over stays the fallback | partial | XF-4 |

Rows: 33. Status counts: have 11, partial 5, missing 10, not-doing 7 (every not-doing row has its reason in the Ours column). Every missing and partial row names a slice.
