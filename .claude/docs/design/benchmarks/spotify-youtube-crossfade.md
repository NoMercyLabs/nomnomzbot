# Benchmark: crossfade between Spotify and YouTube requests

Owner request: `.claude/docs/design/requests/2026-11-17-music-favorites-sponsorblock-crossfade.md`, part 2.
Stoney, 2026-11-10: "i have a crossfade on spotify of 3 seconds"; 2026-11-17: a custom crossfade between Spotify and YouTube Music requests, both directions.

Scope: crossfade, fade curves, volume control and rate limits. Read 2026-11-17.

## Products

- Apple Music (iPhone): https://support.apple.com/guide/iphone/transition-songs-iphadf2fe1f4/ios (opened in a real browser; a plain fetch returned only the navigation)
- Mixxx 2.4 (Auto DJ and mixer): https://manual.mixxx.org/2.4/en/chapters/djing_with_mixxx.html and https://manual.mixxx.org/2.4/en/chapters/user_interface.html
- Spotify (Web API as the control surface): https://developer.spotify.com/documentation/web-api/reference/set-volume-for-users-playback , https://developer.spotify.com/documentation/web-api/concepts/rate-limits , https://developer.spotify.com/documentation/web-api/reference/get-information-about-the-users-current-playback
- YouTube IFrame Player API: https://developers.google.com/youtube/iframe_api_reference
- Web Audio API (MDN): https://developer.mozilla.org/en-US/docs/Web/API/AudioParam/linearRampToValueAtTime
- Howler.js (fade API): https://github.com/goldfire/howler.js

Not sourced: the Spotify support article on its own crossfade setting. Every URL tried (`/article/crossfade/`, `/article/playback-settings/`, `/article/crossfade-and-gapless-playback/`) returned "page not found", also in a real browser. No Spotify app crossfade row is written from memory.
Not sourced: Plexamp. Its support pages returned 403 to a fetch and 0 search results in a browser.

## Feature table

Slices: XF-1 director owns the fade (state, tick, fallback), XF-2 overlay page fade ramp, XF-3 Spotify stepped volume ramp, XF-4 restore and fallback. All four shipped in 0fffa404e (`PlaybackDirector.Crossfade.cs`, `music_player.vue`); the rows below were re-checked against that code on 2026-11-24.

| Feature | Seen in | Source | Ours | Status | Slice |
|---|---|---|---|---|---|
| Crossfade: next song fades in while the previous fades out | Apple Music | Apple "Turn on Crossfade" | `PlaybackDirector.Crossfade.cs` fades Spotify to YouTube and back | have | - |
| User picks the crossfade length with a slider | Apple Music | Apple "drag the slider to choose the length" | One channel value, 3 s (`HandOverLeadMs`) | have | - |
| Crossfade length in seconds set per mode | Mixxx | Mixxx Auto DJ "Full Track" | Same 3 s value used for both directions | have | - |
| Transition time of 0 s acts like a plain player | Mixxx | Mixxx Auto DJ "transition time of 0 seconds" | `CrossfadeMs` returns 0 below `MinCrossfadeMs`, so the plain hand-over runs | have | - |
| Crossfade between catalog and other sources | Apple Music | Apple AutoMix applies to Apple Music catalog only | Spotify to YouTube fade in `PlaybackDirector.Crossfade.cs` | have | - |
| Fade out of the old song and fade in of the new one run at the same time | Mixxx | Mixxx crossfader "Sets each deck's volume as it moves" | Spotify steps down while the page ramps the video up (`DriveCrossfadeAsync`) | have | - |
| Two decks, each with its own volume | Mixxx | Mixxx "Line faders" control each deck's volume | Two engines, each with its own `SetVolumeAsync` | have | - |
| Next song never audible before its turn | Owner goal | `music-gapless-handover-goal-2026-11-10` | The head starts at 0 (`StartHeadSilentAsync`) and the plan runs ahead of the end (cd37b73cc) | have | - |
| Albums play without transitions | Apple Music | Apple AutoMix note "Albums and some genres play without transitions" | Not applicable: the fade applies only at a Spotify and YouTube switch | not-doing | Not applicable: the fade runs only at a Spotify and YouTube switch |
| Automatic choice of transition type (trim silence, simple fade) | Apple Music | Apple "About AutoMix" | Fixed crossfade only | not-doing | AutoMix needs beat analysis the platform does not have for YouTube audio |
| Skip silence at track edges | Mixxx | Mixxx Auto DJ "Skip Silence" (-60 dBFS) | SponsorBlock music section already trims the video (a68a6b0ae); Spotify songs are not trimmed | not-doing | Spotify gives no audio to analyze; SponsorBlock covers the video side |
| Fade starts at the outro | Mixxx | Mixxx "Fade At Outro Start" | Fade starts a fixed 3 s before the end | not-doing | No outro marker exists for Spotify or YouTube audio; a fixed lead is used |
| Crossfade unavailable in some audio settings | Apple Music | Apple note: not available with Hi-Res Lossless | Browser audio has no such mode | not-doing | Not applicable: browser audio has no lossless mode that blocks a fade |
| Volume range 0 to 100 integer | YouTube IFrame | `setVolume(volume)` 0 to 100 | Page applies integer percent | have | - |
| Read the current volume | YouTube IFrame | `getVolume()` | Page keeps its own `ytVolume`; the director holds the owner level | have | - |
| Volume readable while muted | YouTube IFrame | `getVolume()` "even if the player is muted" | Mute is a separate flag in the page level | have | - |
| Playback blocked until a user gesture | YouTube IFrame | `onAutoplayBlocked` | OBS browser source has no gesture block; start failure falls back | have | - |
| Per-frame volume ramp in the browser | Web Audio | `linearRampToValueAtTime` | `fadeYouTube` in `music_player.vue` ramps `setVolume` on a short interval (the IFrame has no audio graph) | have | - |
| Linear fade curve | Web Audio | `linearRampToValueAtTime` | `fadeYouTube` steps linearly from the start level to the target | have | - |
| Equal-power or exponential curve | Web Audio | MDN "exponentialRampToValueAtTime ... more natural" | Linear only | not-doing | Two players mixed by volume sum; a curve is a later polish, not a gap |
| Fade with from, to and duration in one call | Howler.js | `fade(from, to, duration)` | `youtube.volume` carries `percent` and `fadeMs`; the page starts from its current level | have | - |
| Completion event when the fade ends | Howler.js | Howler `fade` event | Page sets the final level at the end | not-doing | The director owns the deadline tick, so the page needs to send nothing back |
| Cancel a running fade when the level changes | Howler.js | `fade` on a playing sound replaces the prior fade | A new `youtube.volume` calls `stopYouTubeFade` first | have | - |
| Set playback volume over the API | Spotify | `PUT /v1/me/player/volume`, `volume_percent` 0 to 100 | `SpotifyPlaybackEngine.SetVolumeAsync` | have | - |
| Volume call needs Premium and `user-modify-playback-state` | Spotify | Set Playback Volume page | Existing token scope; failure falls back | have | - |
| Order of calls is not guaranteed across Player endpoints | Spotify | Set Playback Volume "order of execution isn't guaranteed" | Steps are `CrossfadeStepMs` (1 s) apart and each is awaited | have | - |
| Device may not support volume | Spotify | playback state `device.supports_volume` | A refused call makes `TrySetVolumeAsync` false; the fade ends and the plain hand-over runs | have | - |
| Read device volume to restore it later | Spotify | playback state `device.volume_percent` | Director level (`EffectiveVolume`) is the restore value; no extra read | have | - |
| Crossfade length not reported by the API | Spotify | Playback state has no `crossfade` field | Channel setting stays the source (3 s) | have | - |
| Rate limit is a rolling 30 s window with 429 and `Retry-After` | Spotify | Rate limits page | At most one volume call per second: a 3 s fade is 3 calls plus 1 restore | have | - |
| Lower limit for development mode apps | Spotify | Rate limits page | Same small step count for every channel | have | - |
| Restore the original volume after the fade | Mixxx | Mixxx "crossfader will stay on its last position" (state stays after Auto DJ) | `SettleCrossfadeAsync` and `EndCrossfadeAsync` set the owner level | have | - |
| Fall back to a plain switch when the fade fails | Owner rule | `youtube-must-never-break-spotify` | A refused volume call or failed page fade runs the plain hand-over and restores every volume | have | - |

Rows: 33. Status counts: have 26, not-doing 7 (every not-doing row has its reason in the slice column). No partial or missing rows remain.
