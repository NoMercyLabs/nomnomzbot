# YouTube song requests work without OAuth and without an API key (2026-10-17)

Stoney, verbatim: "meaning that the youtube integration is broken, this should work without oauth" and, after the answer "set a platform API key": "i think you missheard me, it must work without oauth"

Cause: `YouTubeMusicProvider.WorksWithoutConnectionAsync` (YouTubeMusicProvider.cs:131) returns true only when a platform YouTube Data API key exists. The live box has none, so every channel without its own YouTube OAuth gets "Song requests aren't connected".

Decision:

- YouTube is usable for every channel with no OAuth connection and no API key. `WorksWithoutConnectionAsync` is true for YouTube.
- Pasted link: the video id comes from the link; title and channel come from oEmbed (keyless).
- Length: read from YouTube's public player data (keyless). The Audio widget's player reports the real length when the song loads, and that value wins.
- Word search: YouTube's public web search, keyless, with the same filters as today (no 10-hour loops, no reactions).
- Quota (Stoney: "can we use my api keys for providing basic oauth or is everything cutting deep into my own api quota?"): every Data API call, by key or by any user's OAuth token, counts against the bot owner's one Google Cloud project (10,000 units a day; `search.list` 100 units, `videos.list` 1 unit). So keyless always goes first. The key is only a fallback for the 1-unit `videos.list` lookup when keyless data fails. Search never uses the Data API. OAuth is only for managing the streamer's own playlists.
- A keyless failure is a clear typed message, never "not connected".
- Music only (Stoney, verbatim: "make sure that the links are validated music links"): a request must be a music video. Keyless: the player data category is "Music". Key fallback: `categoryId` 10. Anything else is refused with a typed "not a music video" message. Update 2026-10-19: the channel setting `YouTubeMusicCategoryOnly` (music config JSON, default on) decides. On, a YouTube upload must be in the Music category or come from a "- Topic" artist channel, a `music.youtube.com` link always passes, and every mood reply for the refusal states that rule (owner, verbatim: "it needs to say that the music can only come from channels marked as artists or something that makes it super clear in every mood reply"). Off, any on-demand video is admitted. The provider only tags the track (`TrackInfo.IsMusicCategory`); `MusicService` applies the setting.
- Playback is the Audio widget's YouTube player, as today.
