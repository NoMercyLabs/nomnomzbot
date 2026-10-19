# Benchmark: song-request playback (what keeps the music going when the request queue is empty)

Method: read 2026-10-19. The pages below were opened as raw HTML and stripped to text (curl, then a text stripper), then read line by line: StreamElements, Nightbot (song requests page, `!songs` page, FAQ) and Moobot. The Streamlabs help-center page refused a plain fetch (HTTP 403), so it was opened in a real browser (Chrome DevTools MCP: new_page, navigate_page, take_snapshot); it redirected to the Streamlabs content-hub copy of the same article, and the Media Share setup guide was opened the same way, in one reused page that was closed afterward. No row is taken from a search snippet or a page summary. Ours = `server/src/NomNomzBot.Infrastructure/Music/Playback/PlaybackDirector.*.cs`, `FairQueue.cs`, `MusicService.cs`, `MusicConfigDtos.cs`, `YouTubePlaybackEngine.cs`, `Content/Widgets/Assets/music_player.vue` and the specs `spec/music-sr.md` and `spec/music-playback-determinism.md`, read with Grep this run. A product page that does not mention a feature is written "not described on the page read"; that is not proof the product lacks it.

The owner question (Stoney, 2026-10-19, verbatim): "I don't know what stream elements does for youtube but I believe they do have something for keeping youtube playing".

Answer from the sources: StreamElements has a "Backup playlist: a YouTube playlist that plays whenever the request queue is empty". Nightbot and Moobot have the same idea with a saved personal list. 3 of 4 products document a fallback playlist (N of M = 3 of 4). Streamlabs Media Share describes none on the two pages read.

## Products

- StreamElements Media Request (song request module) https://docs.streamelements.com/chatbot/modules/songrequest
- Nightbot Song Requests https://docs.nightbot.tv/control-panel/song-requests (also https://docs.nightbot.tv/commands/songs and https://docs.nightbot.tv/faq)
- Moobot song requests https://moo.bot/docs/song-requests-music-through-twitch-bot
- Streamlabs Media Share + Cloudbot song requests https://streamlabs.com/content-hub/post/how-to-use-song-requests-in-cloudbot and https://streamlabs.com/content-hub/post/how-to-set-up-media-sharing-in-streamlabs

## Features

Short names in the Source column: SE = the StreamElements URL, NB = the Nightbot song requests URL, NBS = the Nightbot `!songs` URL, NBF = the Nightbot FAQ URL, MB = the Moobot URL, SL1 = the Streamlabs song request article, SL2 = the Streamlabs Media Share setup guide (full URLs in Products).

| Feature | Seen in | Source | Ours | Status | Slice |
| --- | --- | --- | --- | --- | --- |
| Fallback playlist plays when the request queue is empty | StreamElements, Nightbot, Moobot | SE ("Backup playlist ... plays whenever the request queue is empty"); NB (Settings > General > Playlist); MB ("Secondary playlist ... will play when the Primary playlist is empty") | PlaybackDirector.Owner.cs:222 (empty queue goes to Idle, "queue_empty"); a YouTube-only channel is silent | missing | S-SR-FALLBACK-PLAYLIST |
| Fallback source is a YouTube playlist the streamer pastes | StreamElements | SE (Backup playlist: "A YouTube playlist") | none (MusicConfigDtos.cs:24-25 BangerPlaylistId is the `!banger` target, not a fallback; MusicService.cs:958 refuses playlist links as requests) | missing | S-SR-FALLBACK-YT-SOURCE |
| Fallback source is a saved list kept in the bot | Nightbot ("Channel" playlist), Moobot (Secondary playlist) | NB (Playlist: "Channel is your own personal playlist"); MB ("add your own songs to your Secondary playlist") | MusicConfigDtos.cs:24-26 (a provider playlist chosen for `!banger`; MusicService.cs:2334 lists playlists) | partial | S-SR-FALLBACK-SAVED-LIST |
| Save the song that is playing into the fallback list from the player | Nightbot | NB (player has a "Save to Channel Playlist Button") | MusicConfigDtos.cs:78-86 (`!banger` adds to a provider playlist; no player button read) | partial | S-SR-FALLBACK-SAVED-LIST |
| Fallback picks a random song | Moobot | MB ("Moobot will randomly choose from it when the Primary playlist has no requests") | none | missing | S-SR-FALLBACK-RANDOM |
| Fallback list split into collections (genres, moods) that switch on and off | Moobot | MB (Collections: "include or exclude entire groups of songs") | none | missing | S-SR-FALLBACK-COLLECTIONS |
| A new request waits for the current fallback song to finish, then plays next | Nightbot | NB ("The playlist will play if the queue is empty and will allow newly requested songs to be played next"); StreamElements and Moobot: not described on the page read | none (no fallback song exists to yield) | missing | S-SR-FALLBACK-YIELD |
| Fallback songs shown in the queue view in a different color | Nightbot | NB ("Gray colored songs in the queue are playlist entries") | none | missing | S-SR-FALLBACK-YIELD |
| Streamer can turn requests off and play only the own list | Moobot, Nightbot (Enable & Disable) | MB ("disable requests entirely, using the song player to play exclusively from your own playlist"); NB (Enable & Disable) | MusicConfigDtos.cs:28,40-41 (RequestsOpen: music keeps playing, queue stays; but no own list to play) | partial | S-SR-FALLBACK-PLAYLIST |
| Requests limited to songs already in the own list (pre-approved songs) | Nightbot, Moobot | NB (Limits > "Limit to Playlist"); MB ("Only accept songs from the secondary playlist") | none | missing | S-SR-PLAYLIST-ONLY |
| Dead-air warning in the docs: fill the fallback list | Moobot | MB ("add some songs to your Secondary playlist so you don't experience dead air") | none (no empty-fallback hint in the dashboard was read) | missing | S-SR-FALLBACK-PLAYLIST |
| Spotify as a request source | none of the four | SE ("YouTube is the only media source - there is no Spotify or SoundCloud integration"); NB providers are YouTube and SoundCloud; MB and SL1/SL2 describe YouTube links only | MusicConfigDtos.cs:21-22 (AllowYouTube, AllowSpotify); SpotifyPlaybackEngine.cs | have | |
| Mixing Spotify and YouTube without both playing | none of the four | not described on any page read | music-sr.md:489-493 (four rules); PlaybackDirector.Owner.cs:232 SilenceForeignAsync | have | |
| Owner's own Spotify resumes when a YouTube request ends and the queue is empty | none of the four | not described on any page read | PlaybackDirector.Owner.cs:205-220 (resume when the owner music was playing before); music-sr.md:493 | have | |
| Where YouTube plays: dashboard player, stream overlay widget and a public request page | StreamElements | SE ("on your dashboard, on the stream via the overlay widget, and on your public request page") | music_player.vue:27-30 (OBS browser-source widget); a dashboard player and a public page were not read | partial | S-SR-PLAYER-SURFACES |
| Where YouTube plays: a player window the streamer must start with a play button | Moobot | MB ("Find the song player widget on your dashboard and click the play button") | music_player.vue:27-30; the bot starts the next request itself (spec/music-playback-determinism.md:150) | have | |
| Where YouTube plays: control-panel player with controls (play, pause, next, slider, volume) | Nightbot | NB (Player Controls & Song Information) | YouTubePlaybackEngine.cs:68-97 (pause, resume, stop, seek, volume commands to the page) | have | |
| Where YouTube plays: browser-source widget on the stream | Streamlabs | SL1/SL2 ("add it to your scene as a browser source"; Hide Video for audio only) | music_player.vue:27-30 (stays mounted while hidden so audio plays) | have | |
| What happens when the player page is closed | Moobot ("it will wait for a song to come in"; songs play "as long as the song player widget is playing") | MB | YouTubePlaybackEngine.cs:77-83 (CanStartAsync = a Music Player page is open); music_player.vue:97-100 (5 s heartbeat; bot counts 15 s silence as a closed page) | have | |
| Tell the streamer a request is waiting because no player page is open | none of the four | not described on any page read | PlaybackDirector.StartFailure.cs:111,132,181 (Blocked state); dashboard alert not read | partial | S-SR-PLAYER-PRESENCE-ALERT |
| Autoplay of the next queued song | StreamElements, Nightbot, Moobot, Streamlabs | MB ("songs will play one after the other"); NB (Next Song starts the next queued song); SE; SL2 | PlaybackDirector.Playing.cs:297-305 (TrackEnded starts the next item) | have | |
| Gap between songs, crossfade or buffer setting | Streamlabs (Buffer Time, for media requests) | SL2 ("Buffer Time"; its meaning is not stated on the text read); no product describes a crossfade | none (no crossfade in music_player.vue or Music/Playback by grep) | not-doing | Reason: no product page read describes a crossfade, and the one buffer setting is unexplained. Revisit when a source states what it does |
| Queue order: first come first served with a position reply | StreamElements | SE (reply "added ... to the queue at #2 (playing ~in 3 mins 5 secs)") | FairQueue.cs:80-96 (fair order, a different rule); no position or wait-time reply found by grep (ETA, EstimatedWait, PositionInQueue in Music/) | partial | S-SR-ETA-REPLY |
| Queue order: fair, one song per viewer before anyone's second | none of the four | not described on any page read (they cap per user instead) | FairQueue.cs:18-24,80-96 | have | |
| Priority lane for tippers or subscribers | StreamElements, Streamlabs | SE (Tipped requests: "Prioritize requests attached to tips"); SL2 (separate subscriber price per second) | none (FairQueue.cs:191 MoveToFront is an operator move only) | missing | S-SR-PRIORITY-LANE |
| Operator promotes a song to the front | Nightbot | NB (gray up arrow; `!songs`) | FairQueue.cs:191; MusicService.cs:2067 | have | |
| Skip by a moderator | StreamElements, Nightbot, Moobot | SE (`!skip`); NB (Next Song); MB (`!SkipSong`) | MusicService.cs:398-432 (OwnerSkip to the director) | have | |
| Vote skip with a vote threshold | StreamElements, Streamlabs | SE (`!voteskip`, "set how many votes a skip requires"); SL2 ("Votes Required to Skip") | none (grep voteskip in server/src: no match) | missing | S-SR-VOTESKIP |
| Viewer removes their own last request | StreamElements | SE (`!wrongsong`) | MusicService.cs:424 names a `!songwrong` pipeline action; the handler was not read | partial | S-SR-WRONGSONG-CHECK |
| Maximum song length | StreamElements (separate free and paid limits), Moobot, Streamlabs | SE (Maximum request duration); MB ("Maximum length of song"); SL2 ("Max Duration") | MusicService.cs:1077; MusicConfigDtos.cs:52-54 (30 to 3600 s, operators exempt, MusicService.cs:1071) | have | |
| Block embed-disabled and unplayable videos before they queue | none of the four | not described on any page read | MusicService.cs:1019 (!IsEmbeddable); YouTubeMusicProvider.cs:377-381; music_player.vue:155-160 (player error codes skip at once) | have | |
| Per-viewer cap on queued songs, with a higher cap for some roles | StreamElements, Nightbot (User Limit + Exempt Userlevel), Moobot, Streamlabs | SE; NB (Limits); MB ("Max requests in the queue per user"); SL2 | MusicConfigDtos.cs:20,88-94; MusicService.cs:1695-1710 (per-role caps) | have | |
| Per-viewer cap per stream, optionally only when the queue fills | Moobot | MB ("limit Song Requests to a certain number per user per session ... only when your queue is filling up") | MusicConfigDtos.cs:59-64 (PerStreamLimit; always on, no "only when filling" option) | partial | S-SR-SESSION-CAP-WHEN-FILLING |
| Maximum queue size | StreamElements, Nightbot (1 to 100), Moobot, Streamlabs | SE; NB (Queue Length); MB; SL2 ("Queue Limit") | MusicConfigDtos.cs:19,46-47; MusicService.cs:1116-1120 | have | |
| Cost in loyalty points per request, with a subscriber discount | StreamElements | SE (Loyalty cost: "optional subscriber discount"; exempt user level bypasses) | MusicService.cs:1159-1160 (every live request is free, Cost: 0); refund hook MusicService.cs:109-145; Media Share has an entry cost MediaShareService.cs:142-148 | partial | S-SR-POINTS-COST |
| Price per second of media, tip minimum | StreamElements (tipped requests), Streamlabs (Price per second, minimum tip) | SE; SL2 | none (Media Share EntryCost is a flat cost, MediaShareService.cs:142-148) | not-doing | Reason: needs a tip provider; the flat points cost in S-SR-POINTS-COST covers the channel-currency case |
| Approval queue: every request waits for a moderator before it enters | StreamElements, Moobot, Streamlabs | SE (Queue moderation, Pending Approval); MB (moderators remove requests from the Primary playlist); SL2 (auto-show vs auto-hide) | MusicService.cs:770-780 (a moderator `!permit` admits a request held by a limit); MinTrustLevel MusicConfigDtos.cs:23; no approve-all mode | partial | S-SR-APPROVAL-QUEUE |
| Minimum user level to request | StreamElements, Nightbot (Userlevel) | SE (Minimum user level); NB (General > Userlevel) | MusicConfigDtos.cs:23; MusicService.cs:1659-1672 | have | |
| Song blocklist | StreamElements, Moobot | SE (blacklist videos); MB (blacklist songs from the widget) | MusicService.cs:1054; BlockedTrackService.cs:44 | have | |
| Blocklist by title word or tag | StreamElements (tags), Moobot (title words) | SE ("blacklist specific videos or tags"); MB ("Song title filtering") | none | missing | S-SR-TITLE-FILTER |
| Viewer blocklist from the player | Moobot | MB ("blacklist viewers through the song player widget") | MusicService.cs:2149 (ban reason "Banned from dashboard queue"); no per-viewer ban read | partial | S-SR-VIEWER-BLOCK |
| Music category only | StreamElements, Nightbot, Streamlabs | SE (YouTube filters); NB (Limit to Music); SL2 ("Limit Requests to Music Only") | MusicConfigDtos.cs:66-70 (YouTubeMusicCategoryOnly) | have | |
| Filter by video rating or view count | StreamElements, Nightbot (Limit to Liked Videos), Streamlabs | SE; NB; SL2 (rating and view count cross-reference) | none | missing | S-SR-RATING-FILTER |
| Request a start and end time inside the video | Moobot | MB (`#start=00m00s&end=00m00s` after the link) | none | missing | S-SR-REQUEST-RANGE |
| Request by link, video ID or search words | StreamElements, Nightbot, Moobot, Streamlabs (link only) | SE; NBS (`!songs request`); MB; SL1 ("You can only request with a link") | MusicService.cs:889 (SongSearchRanker on search text); MusicService.cs:958 (links) | have | |
| Music audio captured apart from the voice so VODs can leave it out | Moobot | MB (OBS Application Audio Capture of the player window) | spec/music-playback-determinism.md:201-237 (two system widgets, two audio levels) | have | |

## Slices this benchmark adds (each partial or missing row maps to one)

S-SR-FALLBACK-PLAYLIST (core: EndOfQueueAsync at PlaybackDirector.Owner.cs:222 starts a fallback item instead of Idle when the owner has no Spotify music to resume), S-SR-FALLBACK-YT-SOURCE, S-SR-FALLBACK-SAVED-LIST, S-SR-FALLBACK-RANDOM, S-SR-FALLBACK-COLLECTIONS, S-SR-FALLBACK-YIELD, S-SR-PLAYLIST-ONLY, S-SR-PLAYER-SURFACES, S-SR-PLAYER-PRESENCE-ALERT, S-SR-ETA-REPLY, S-SR-PRIORITY-LANE, S-SR-VOTESKIP, S-SR-WRONGSONG-CHECK, S-SR-SESSION-CAP-WHEN-FILLING, S-SR-POINTS-COST, S-SR-APPROVAL-QUEUE, S-SR-TITLE-FILTER, S-SR-VIEWER-BLOCK, S-SR-RATING-FILTER, S-SR-REQUEST-RANGE.

## Coverage ledger

Sources opened: 8 of 8 checked (N of M = 8 of 8).

| Source | How opened | Status |
| --- | --- | --- |
| https://docs.streamelements.com/chatbot/modules/songrequest | raw fetch, read in full | checked |
| https://docs.nightbot.tv/control-panel/song-requests | raw fetch, read in full | checked |
| https://docs.nightbot.tv/commands/songs | raw fetch, searched by term (playlist, skip, promote, delete, request) | checked |
| https://docs.nightbot.tv/faq | raw fetch, searched by term (playlist, song, queue) | checked |
| https://moo.bot/docs/song-requests-music-through-twitch-bot | raw fetch, searched by term and the matching lines read | checked |
| https://support.streamlabs.com/hc/en-us/articles/360051406434-How-to-Use-Song-Requests-in-Cloudbot | plain fetch refused (HTTP 403); opened in a real browser, redirected to the content-hub copy below | checked |
| https://streamlabs.com/content-hub/post/how-to-use-song-requests-in-cloudbot | real browser, snapshot read | checked |
| https://streamlabs.com/content-hub/post/how-to-set-up-media-sharing-in-streamlabs | real browser, snapshot searched by term (playlist, skip, vote, limit, duration, price, approve) | checked |

Not opened: other bots (Fossabot, WizeBot, PhantomBot, Mix It Up). This run's time and context budget ended after the four named products. They are leads for a second pass, not part of this table.
