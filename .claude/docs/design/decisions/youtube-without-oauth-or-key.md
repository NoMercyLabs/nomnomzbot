# YouTube song requests work without OAuth and without an API key (2026-10-17)

Stoney, verbatim: "meaning that the youtube integration is broken, this should work without oauth" and, after the answer "set a platform API key": "i think you missheard me, it must work without oauth"

Cause: `YouTubeMusicProvider.WorksWithoutConnectionAsync` (YouTubeMusicProvider.cs:131) returns true only when a platform YouTube Data API key exists. The live box has none, so every channel without its own YouTube OAuth gets "Song requests aren't connected".

Decision:

- YouTube is usable for every channel with no OAuth connection and no API key. `WorksWithoutConnectionAsync` is true for YouTube.
- Pasted link: the video id comes from the link; title and channel come from oEmbed (keyless).
- Length: read from YouTube's public player data (keyless). The Audio widget's player reports the real length when the song loads, and that value wins.
- Word search: YouTube's public web search, keyless, with the same filters as today (no 10-hour loops, no reactions).
- The Data API key and a channel's OAuth stay optional. When present, they are used first. A keyless failure is a clear typed message, never "not connected".
- Playback is the Audio widget's YouTube player, as today.
