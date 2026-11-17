# Owner request 2026-11-17: favorites, bangers, SponsorBlock, crossfade

Stoney, 2026-11-17, during his stream, verbatim:

> can we use sponsorblock to get skip markers for the music only section?
> can we oblige bamo?
> the toggle favorite does not work for youtube music.
> basically for the favorite it needs to add it to the users liked videos, and when using banger for both spotify and youtube there needs to be a custom playlist creation for each individual user, so lets say Bamo16's Stream Bangers 2026 for both youtube and spotify. this needs to be a setting that the broadcaster can change. the default is just add to spotify favorites and youtube likes, and opt-in to custom created playlists (per yearr or all time)
> we need to be careful with the ratelimits and may need to queue up the banger/favorite songs till the end of the stream depending on the prediction of usage (if possible)

"oblige bamo" refers to Bamo16's chat message the same evening: "sounds like someone needs to implement a custom crossfade for switching between Spotify and YouTube Music requests".

## Parts

1. **SponsorBlock:** a YouTube request plays only its music section. Use the SponsorBlock `music_offtopic` segments to skip non-music intros and outros. The hand-over timing uses the trimmed length.
2. **Crossfade:** a custom crossfade between Spotify and YouTube requests, in both directions. It uses the channel's crossfade setting (Stoney's value: 3 s). Related: music-gapless-handover-goal-2026-11-10.
3. **Favorite on YouTube:** the Favorite toggle works for a YouTube song. Favorite means a like on the video (YouTube) or a save to Liked Songs (Spotify).
4. **Banger target setting (broadcaster):**
   - Default: banger adds to Spotify favorites and YouTube likes.
   - Opt-in: a custom playlist per requesting user on both services, for example "Bamo16's Stream Bangers 2026". The broadcaster picks per year or all time.
5. **Rate limits:** banger and favorite writes respect each provider's limits. When predicted usage would exceed the budget, the writes are queued and flushed at the end of the stream.
