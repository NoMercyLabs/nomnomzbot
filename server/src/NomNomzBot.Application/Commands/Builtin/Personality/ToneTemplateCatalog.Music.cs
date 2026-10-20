// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>Reply content of the music built-ins (<c>BuiltinResponseSlots.Music.cs</c>).</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddMusicSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Usage,
            variables: [],
            informative: ["Usage: !sr <song name or URL>"],
            friendly: ["Tell me which song you want! !sr <song name or URL>"],
            sassy: ["You forgot the song. Try: !sr <song name or URL>"],
            hype: ["WHICH SONG? !sr <song name or URL>"],
            chill: ["pick a song: !sr <song name or URL>"]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.RequestsOff,
            variables: [],
            informative: ["Song requests are turned off in this channel."],
            friendly: ["Song requests are taking a break right now — check back soon!"],
            sassy: ["Song requests are off. The DJ has left the building."],
            hype: ["SONG REQUESTS ARE OFF RIGHT NOW. COME BACK LATER."],
            chill: ["requests are off right now."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.QueueClosed,
            variables: [],
            informative: ["The song queue is closed."],
            friendly: ["The song queue is closed for now — check back soon!"],
            sassy: ["The queue is closed. The DJ is not taking requests."],
            hype: ["THE SONG QUEUE IS CLOSED. NO MORE REQUESTS FOR NOW."],
            chill: ["the queue is closed right now."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.CloseQueue.Key,
            BuiltinResponseSlots.CloseQueue.Closed,
            variables: [],
            informative: ["The song queue is now closed."],
            friendly: ["The song queue is closed for now. Thanks for all the requests!"],
            sassy: ["Queue closed. The DJ needs a break."],
            hype: ["SONG QUEUE CLOSED! THANKS FOR THE REQUESTS!"],
            chill: ["the queue is closed now."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.CloseQueue.Key,
            BuiltinResponseSlots.CloseQueue.AlreadyClosed,
            variables: [],
            informative: ["The song queue is already closed."],
            friendly: ["The song queue was already closed!"],
            sassy: ["Already closed. Nothing to close twice."],
            hype: ["THE QUEUE IS ALREADY CLOSED!"],
            chill: ["the queue was already closed."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.CloseQueue.Key,
            BuiltinResponseSlots.CloseQueue.Failed,
            variables: [],
            informative: ["Could not close the song queue. Try again in a moment."],
            friendly: ["Hmm, closing the queue did not work. Try again in a moment!"],
            sassy: ["The queue refused to close. Try again."],
            hype: ["CLOSING THE QUEUE FAILED! TRY AGAIN!"],
            chill: ["could not close the queue. try again soon."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.ClearSongs.Usage,
            variables: [],
            informative: ["Usage: !clearsongs <user>"],
            friendly: ["Tell me whose songs to clear! !clearsongs <user>"],
            sassy: ["Whose songs? Try: !clearsongs <user>"],
            hype: ["WHOSE SONGS? !clearsongs <user>"],
            chill: ["whose songs? !clearsongs <user>"]
        );

        Add(
            catalog,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.ClearSongs.Cleared,
            variables: ["count", "target"],
            informative: ["Removed {count} songs requested by {target}."],
            friendly: ["All done! Removed {count} songs requested by {target}."],
            sassy: ["Gone. {count} songs from {target} are out of the queue."],
            hype: ["{count} SONGS FROM {target} CLEARED OUT!"],
            chill: ["cleared {count} songs from {target}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.ClearSongs.NothingQueued,
            variables: ["target"],
            informative: ["{target} has no songs in the queue."],
            friendly: ["{target} does not have any songs in the queue!"],
            sassy: ["{target} has nothing queued. Nothing to clear."],
            hype: ["{target} HAS NO SONGS IN THE QUEUE!"],
            chill: ["{target} has nothing queued."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.ClearSongs.Failed,
            variables: ["target"],
            informative: ["Could not remove the songs of {target}. Try again in a moment."],
            friendly: ["Hmm, clearing the songs of {target} did not work. Try again in a moment!"],
            sassy: ["The songs of {target} refused to leave. Try again."],
            hype: ["CLEARING THE SONGS OF {target} FAILED! TRY AGAIN!"],
            chill: ["could not clear the songs of {target}. try again soon."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.OpenQueue.Key,
            BuiltinResponseSlots.OpenQueue.Opened,
            variables: [],
            informative: ["The song queue is now open."],
            friendly: ["The song queue is open again. Send in your requests!"],
            sassy: ["Queue open. Make your picks count."],
            hype: ["SONG QUEUE IS OPEN! SEND THOSE REQUESTS!"],
            chill: ["the queue is open again."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.OpenQueue.Key,
            BuiltinResponseSlots.OpenQueue.AlreadyOpen,
            variables: [],
            informative: ["The song queue is already open."],
            friendly: ["The song queue was already open!"],
            sassy: ["Already open. Go request something."],
            hype: ["THE QUEUE IS ALREADY OPEN!"],
            chill: ["the queue was already open."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.OpenQueue.Key,
            BuiltinResponseSlots.OpenQueue.Failed,
            variables: [],
            informative: ["Could not open the song queue. Try again in a moment."],
            friendly: ["Hmm, opening the queue did not work. Try again in a moment!"],
            sassy: ["The queue refused to open. Try again."],
            hype: ["OPENING THE QUEUE FAILED! TRY AGAIN!"],
            chill: ["could not open the queue. try again soon."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Revoked,
            variables: [],
            informative: ["Stop requesting songs, your permission has been revoked"],
            friendly: ["Your song request permission is revoked, so I cannot add that song."],
            sassy: ["Your song requests are revoked. Stop asking."],
            hype: ["SONG REQUESTS REVOKED. STOP ASKING."],
            chill: ["your song requests are revoked."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackBlocked,
            variables: ["track.name"],
            informative: ["\"{track.name}\" is blocked in this channel."],
            friendly:
            [
                "Sorry, \"{track.name}\" is not on the list for this channel. Try another one!",
            ],
            sassy: ["\"{track.name}\" is banned here. There is a reason. Pick something else."],
            hype: ["\"{track.name}\" IS BLOCKED. PICK ANOTHER BANGER."],
            chill: ["\"{track.name}\" is blocked here. pick another."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoActiveDevice,
            variables: ["track.name"],
            informative:
            [
                "Couldn't queue \"{track.name}\" — nothing is playing on any device right now. Start playback and try again.",
            ],
            friendly:
            [
                "I couldn't queue \"{track.name}\" because nothing is playing yet. Start playback and try again!",
            ],
            sassy:
            [
                "\"{track.name}\" would be great, if anything was playing. Start playback first.",
            ],
            hype:
            [
                "NOTHING IS PLAYING ON ANY DEVICE. START PLAYBACK, THEN TRY \"{track.name}\" AGAIN.",
            ],
            chill:
            [
                "nothing is playing anywhere, so \"{track.name}\" did not queue. start playback and retry.",
            ]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoOverlayPlayer,
            variables: ["track.name"],
            informative:
            [
                "Couldn't queue \"{track.name}\" — the audio overlay is not open. Open it in OBS to play YouTube requests.",
            ],
            friendly:
            [
                "I couldn't queue \"{track.name}\" because the audio overlay isn't open. Open it in OBS to play YouTube requests!",
            ],
            sassy:
            [
                "\"{track.name}\" is YouTube, and the audio overlay is closed. Open it in OBS first.",
            ],
            hype: ["THE AUDIO OVERLAY IS CLOSED. OPEN IT IN OBS, THEN TRY \"{track.name}\" AGAIN."],
            chill:
            [
                "the audio overlay isn't open, so \"{track.name}\" did not queue. open it in obs for youtube requests.",
            ]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.PremiumRequired,
            variables: ["track.name"],
            informative:
            [
                "Couldn't queue \"{track.name}\" — a Premium account is required for that.",
            ],
            friendly:
            [
                "I couldn't queue \"{track.name}\" — the music account needs Premium for that.",
            ],
            sassy: ["\"{track.name}\" needs Premium. The free tier says no."],
            hype: ["\"{track.name}\" NEEDS PREMIUM. THE FREE TIER SAID NO."],
            chill: ["\"{track.name}\" needs a premium account."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.AuthFailed,
            variables: ["track.name"],
            informative:
            [
                "Couldn't queue \"{track.name}\" — the music connection needs to be reconnected.",
            ],
            friendly:
            [
                "I couldn't queue \"{track.name}\" — the music connection needs to be reconnected.",
            ],
            sassy: ["\"{track.name}\" is stuck. The music connection logged out on me."],
            hype: ["MUSIC CONNECTION DROPPED. \"{track.name}\" DID NOT QUEUE. RECONNECT IT."],
            chill: ["music connection dropped, \"{track.name}\" did not queue."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Forbidden,
            variables: ["track.name"],
            informative:
            [
                "Couldn't queue \"{track.name}\" — the music connection doesn't have permission for that.",
            ],
            friendly:
            [
                "I couldn't queue \"{track.name}\" — the music connection is not allowed to do that.",
            ],
            sassy:
            [
                "\"{track.name}\" did not queue. The music connection is not allowed to touch it.",
            ],
            hype: ["\"{track.name}\" DID NOT QUEUE. THE MUSIC CONNECTION HAS NO PERMISSION."],
            chill: ["no permission to queue \"{track.name}\"."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackTooLong,
            variables: ["track.name", "track.max", "user"],
            informative:
            [
                "Failed to add to queue. \"{track.name}\" exceeds the maximum allowed duration of {track.max}.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" is longer than the {track.max} limit, so I could not add it. Pick a shorter one!",
            ],
            sassy: ["\"{track.name}\" is over {track.max}. This is a queue, not a marathon."],
            hype: ["\"{track.name}\" IS OVER {track.max}. TOO LONG. FIND A SHORTER BANGER."],
            chill: ["\"{track.name}\" is over {track.max}. too long, pick a shorter one."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackExplicit,
            variables: ["track.name"],
            informative: ["\"{track.name}\" is explicit, and explicit songs are not allowed here."],
            friendly:
            [
                "Sorry, \"{track.name}\" is marked explicit and this channel does not take those. Pick a clean one!",
            ],
            sassy: ["\"{track.name}\" is explicit. This channel does not do explicit."],
            hype: ["\"{track.name}\" IS EXPLICIT. NOT HERE. FIND A CLEAN BANGER."],
            chill: ["\"{track.name}\" is explicit. not allowed here, pick a clean one."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotEmbeddable,
            variables: ["track.name"],
            informative:
            [
                "\"{track.name}\" can't be played here — its uploader doesn't allow it on other sites. Try another upload of the song.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" can't play on stream, its uploader blocks that. Another upload of it might work!",
            ],
            sassy:
            [
                "\"{track.name}\" is locked to YouTube by its uploader. Find an upload that actually plays.",
            ],
            hype: ["\"{track.name}\" IS LOCKED BY ITS UPLOADER. FIND ANOTHER UPLOAD!"],
            chill: ["\"{track.name}\" can't play here, the uploader blocks it. try another upload."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.AgeRestricted,
            variables: ["track.name"],
            informative:
            [
                "\"{track.name}\" is age-restricted on YouTube, so it can't play on stream. Try another upload of the song.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" is age-restricted on YouTube and can't play on stream. Another upload might work!",
            ],
            sassy:
            [
                "\"{track.name}\" is age-restricted. Not playing that one. Find another upload.",
            ],
            hype: ["\"{track.name}\" IS AGE-RESTRICTED. CAN'T PLAY IT. FIND ANOTHER UPLOAD!"],
            chill: ["\"{track.name}\" is age-restricted, can't play it here. try another upload."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.LiveBroadcast,
            variables: [],
            informative:
            [
                "Live streams and premieres can't be requested. Send a link to a regular video.",
            ],
            friendly:
            [
                "That one is a live stream or a premiere, so I can't queue it. A regular video link works!",
            ],
            sassy: ["That is a live stream. It never ends. Send a regular video."],
            hype: ["NO LIVE STREAMS OR PREMIERES! SEND A REGULAR VIDEO!"],
            chill: ["can't queue live streams or premieres. send a regular video."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackMissing,
            variables: [],
            informative: ["That video doesn't exist or is private."],
            friendly: ["I can't find that video, it may be deleted or private. Got another link?"],
            sassy: ["That video is gone or private. Try one that exists."],
            hype: ["THAT VIDEO IS GONE OR PRIVATE! TRY ANOTHER!"],
            chill: ["that video is deleted or private. try another."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.UnreadableLink,
            variables: [],
            informative:
            [
                "I couldn't read that link. Paste the full video link, or send the song name instead.",
            ],
            friendly:
            [
                "Hmm, I couldn't read that link. Try the full video link, or just the song name!",
            ],
            sassy: ["That link is unreadable. Full video link or the song name, please."],
            hype: ["CAN'T READ THAT LINK! SEND THE FULL VIDEO LINK OR THE SONG NAME!"],
            chill: ["couldn't read that link. send the full video link or the song name."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.LengthUnknown,
            variables: [],
            informative:
            [
                "I couldn't tell how long that video is, so it can't be requested. Try another upload.",
            ],
            friendly:
            [
                "Hmm, YouTube won't tell me how long that video is, so I can't queue it. Another upload might work!",
            ],
            sassy:
            [
                "That video has no length I can read. Not queuing a mystery. Try another upload.",
            ],
            hype: ["NO LENGTH ON THAT VIDEO! CAN'T QUEUE IT! TRY ANOTHER UPLOAD!"],
            chill: ["can't tell how long that video is, so no queue. try another upload."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotMusic,
            variables: [],
            informative:
            [
                "That video can't be requested. YouTube requests must be in YouTube's Music category or come from an official artist (- Topic) channel. Send a YouTube Music link, or a song name.",
            ],
            friendly:
            [
                "Ooh, I can't queue that one! YouTube requests have to be in YouTube's Music category or come from an official artist (- Topic) channel. Send me a YouTube Music link, or just a song name!",
            ],
            sassy:
            [
                "Not queuing that. YouTube requests must be in YouTube's Music category or come from an official artist (- Topic) channel, and that video is neither. Send a YouTube Music link, or a song name.",
            ],
            hype:
            [
                "NOT MUSIC! YOUTUBE REQUESTS MUST BE IN YOUTUBE'S MUSIC CATEGORY OR COME FROM AN OFFICIAL ARTIST (- TOPIC) CHANNEL! SEND A YOUTUBE MUSIC LINK, OR A SONG NAME!",
            ],
            chill:
            [
                "can't queue that. youtube requests have to be in youtube's music category or come from an official artist (- topic) channel. send a youtube music link, or a song name.",
            ]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.QuotaExhausted,
            variables: ["retry.time"],
            informative:
            [
                "YouTube's daily request limit is used up; song requests resume after {retry.time}.",
            ],
            friendly:
            [
                "YouTube says we've used up today's requests! Song requests are back after {retry.time}.",
            ],
            sassy: ["YouTube cut us off for today. Song requests are back after {retry.time}."],
            hype: ["YOUTUBE LIMIT HIT FOR TODAY! SONG REQUESTS BACK AFTER {retry.time}!"],
            chill: ["youtube's daily limit is used up. song requests are back after {retry.time}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.PerStreamLimit,
            variables: ["request.limit"],
            informative:
            [
                "You already requested {request.limit} song(s) this stream. That is the limit.",
            ],
            friendly:
            [
                "You have used your {request.limit} request(s) for this stream. Come back next stream!",
            ],
            sassy: ["{request.limit} request(s) per stream. You have had yours."],
            hype: ["THAT IS YOUR {request.limit} REQUEST(S) FOR THIS STREAM. SEE YOU NEXT TIME!"],
            chill: ["you used your {request.limit} request(s) for this stream."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackTooLongRefunded,
            variables: ["track.name", "track.max", "user"],
            informative:
            [
                "Failed to add to queue. \"{track.name}\" exceeds the maximum allowed duration of {track.max}, your point has been refunded.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" is longer than the {track.max} limit, so I could not add it. Your points are back, pick a shorter one!",
            ],
            sassy:
            [
                "\"{track.name}\" is over {track.max}. This is a queue, not a marathon. Points refunded.",
            ],
            hype: ["\"{track.name}\" IS OVER {track.max}. TOO LONG. POINTS REFUNDED, GO AGAIN."],
            chill: ["\"{track.name}\" is over {track.max}. points refunded, pick a shorter one."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.AddedWithCode,
            variables: ["request.code", "track.artist", "track.name", "user"],
            informative: ["Added to queue: {track.name} by {track.artist} (code {request.code})"],
            friendly:
            [
                "Added \"{track.name}\" by {track.artist} to the queue! (code {request.code})",
            ],
            sassy: ["Fine, {track.name} by {track.artist} is in the queue. (code {request.code})"],
            hype: ["{track.name} BY {track.artist} IS IN THE QUEUE! (code {request.code})"],
            chill: ["queued {track.name} by {track.artist}. (code {request.code})"]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.QueueFull,
            variables: ["queue.max"],
            informative: ["The queue is full ({queue.max} max) — try again once it's shorter."],
            friendly: ["The queue is full right now ({queue.max} max) — try again in a bit!"],
            sassy: ["The queue is full at {queue.max}. Patience is free."],
            hype: ["THE QUEUE IS FULL AT {queue.max}. HOLD YOUR BANGERS."],
            chill: ["queue is full ({queue.max}). try later."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.PerUserLimit,
            variables: ["request.limit"],
            informative:
            [
                "You already have {request.limit} request(s) queued — wait for one to play before adding more.",
            ],
            friendly:
            [
                "You already have {request.limit} request(s) in the queue — wait for one to play, then add more!",
            ],
            sassy: ["{request.limit} request(s) each. You have used yours. Wait your turn."],
            hype: ["YOU HAVE {request.limit} IN THE QUEUE ALREADY. LET ONE PLAY FIRST."],
            chill: ["you have {request.limit} queued already. wait for one to play."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.MissingScope,
            variables: [],
            informative: ["The music connection needs to be reconnected."],
            friendly:
            [
                "The music connection needs to be reconnected — the streamer can do that in the dashboard.",
            ],
            sassy: ["The music connection is broken. Tell the streamer to fix it."],
            hype: ["THE MUSIC CONNECTION NEEDS A RECONNECT."],
            chill: ["music connection needs a reconnect."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotConfigured,
            variables: [],
            informative:
            [
                "YouTube song requests are not set up on this bot yet. The bot owner must add a YouTube API key.",
            ],
            friendly:
            [
                "YouTube song requests aren't set up on this bot yet — the bot owner needs to add a YouTube API key.",
            ],
            sassy:
            [
                "YouTube requests aren't set up here. The bot owner forgot the YouTube API key.",
            ],
            hype:
            [
                "YOUTUBE REQUESTS ARE NOT SET UP YET. THE BOT OWNER MUST ADD A YOUTUBE API KEY.",
            ],
            chill: ["youtube requests aren't set up yet. the bot owner needs to add an api key."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.ProviderUnavailable,
            variables: [],
            informative: ["The music provider is temporarily unavailable."],
            friendly: ["The music service is having a moment — try again soon!"],
            sassy: ["The music service is down. Not my fault this time."],
            hype: ["THE MUSIC SERVICE IS DOWN FOR NOW. TRY AGAIN SOON."],
            chill: ["music service is down for now."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.RateLimited,
            variables: ["retry.minutes"],
            informative:
            [
                "The music service is rate-limiting this channel — try again in about {retry.minutes} minute(s).",
            ],
            friendly:
            [
                "The music service needs a breather — try again in about {retry.minutes} minute(s)!",
            ],
            sassy: ["The music service cut us off for {retry.minutes} minute(s). Blame the quota."],
            hype: ["THE MUSIC SERVICE NEEDS {retry.minutes} MINUTE(S). HOLD THAT REQUEST."],
            chill: ["music service is rate-limited. try again in {retry.minutes} minute(s)."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.UnsupportedContent,
            variables: [],
            informative:
            [
                "Song requests only take individual tracks — that link is a playlist, album, episode, show, or artist page. Paste a single track link, or just search by name instead.",
            ],
            friendly:
            [
                "I can only take single tracks, not playlists, albums or artist pages. Paste a track link or search by name!",
            ],
            sassy:
            [
                "That is a whole playlist, album or artist. I take one track at a time. Paste a track link or search by name.",
            ],
            hype:
            [
                "ONE TRACK AT A TIME! NO PLAYLISTS, ALBUMS OR ARTIST PAGES. PASTE A TRACK LINK OR SEARCH BY NAME.",
            ],
            chill: ["only single tracks please. paste a track link or search by name."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.UnsupportedLink,
            variables: [],
            informative:
            [
                "That link can't be used here. Send a song name, or a link for the music service this channel uses.",
            ],
            friendly:
            [
                "I can't use that link, sorry! Send a song name or a link for this channel's music service.",
            ],
            sassy:
            [
                "I can't do anything with that link. Send a song name or a link for this channel's music service.",
            ],
            hype:
            [
                "CAN'T USE THAT LINK! SEND A SONG NAME OR A LINK FOR THIS CHANNEL'S MUSIC SERVICE.",
            ],
            chill:
            [
                "can't use that link. send a song name or a link for this channel's music service.",
            ]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.SourceNotAllowed,
            variables: [],
            informative:
            [
                "This channel does not take song requests from that music service. Send a song name instead.",
            ],
            friendly:
            [
                "This channel doesn't take requests from that music service, sorry! Send a song name instead.",
            ],
            sassy: ["Not that music service. This channel has it switched off. Send a song name."],
            hype: ["THAT MUSIC SERVICE IS OFF IN THIS CHANNEL! SEND A SONG NAME INSTEAD."],
            chill: ["that music service is off here. send a song name instead."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.SourceNeedsRole,
            variables: [],
            informative:
            [
                "Your role can't request songs from that music service in this channel. Send a song name instead.",
            ],
            friendly:
            [
                "Sorry, your role can't request from that music service here. Try a song name instead!",
            ],
            sassy: ["Not for your role. That music service is locked here. Send a song name."],
            hype: ["THAT MUSIC SERVICE IS LOCKED FOR YOUR ROLE HERE! SEND A SONG NAME INSTEAD."],
            chill: ["that music service is locked for your role here. send a song name instead."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotPlayable,
            variables: [],
            informative:
            [
                "That track can't play in the streamer's country. Try a different version of the song.",
            ],
            friendly:
            [
                "That one is not available in the streamer's country. Maybe another version of it is!",
            ],
            sassy: ["That track is region-locked here. Find a version that actually plays."],
            hype: ["THAT TRACK IS BLOCKED IN THIS COUNTRY. TRY ANOTHER VERSION!"],
            chill: ["that track can't play here. try another version."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Unreachable,
            variables: ["query"],
            informative:
            [
                "Couldn't reach the music service for \"{query}\" — try again in a moment.",
            ],
            friendly:
            [
                "I couldn't reach the music service for \"{query}\" — try again in a moment!",
            ],
            sassy: ["The music service ignored me for \"{query}\". Try again in a moment."],
            hype: ["COULD NOT REACH THE MUSIC SERVICE FOR \"{query}\". TRY AGAIN IN A MOMENT."],
            chill: ["couldn't reach the music service for \"{query}\". try again soon."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoProviderBroadcaster,
            variables: [],
            informative:
            [
                "Song requests aren't connected yet — connect Spotify or YouTube in the dashboard.",
            ],
            friendly:
            [
                "Song requests are not connected yet — connect Spotify or YouTube in the dashboard to start!",
            ],
            sassy:
            [
                "Song requests have nowhere to go. Connect Spotify or YouTube in the dashboard.",
            ],
            hype: ["CONNECT SPOTIFY OR YOUTUBE IN THE DASHBOARD TO TURN ON SONG REQUESTS."],
            chill: ["song requests aren't connected. add spotify or youtube in the dashboard."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoProviderModerator,
            variables: [],
            informative:
            [
                "Song requests aren't connected — let the broadcaster know to connect Spotify or YouTube in the dashboard.",
            ],
            friendly:
            [
                "Song requests are not connected — please tell the broadcaster to connect Spotify or YouTube in the dashboard.",
            ],
            sassy:
            [
                "Song requests are not connected. Tell the broadcaster to connect Spotify or YouTube.",
            ],
            hype:
            [
                "SONG REQUESTS ARE NOT CONNECTED. TELL THE BROADCASTER TO CONNECT SPOTIFY OR YOUTUBE.",
            ],
            chill: ["song requests aren't connected. tell the broadcaster."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Failed,
            variables: [],
            informative: ["Nothing to skip or skip failed."],
            friendly: ["I couldn't skip that one — try again in a moment!"],
            sassy: ["Skip failed. The track is stubborn."],
            hype: ["SKIP FAILED. TRY AGAIN."],
            chill: ["skip didn't work."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NoPlayer,
            variables: [],
            informative: [NoPlayerLine],
            friendly: ["The music player isn't open yet — add the Audio widget to OBS!"],
            sassy: ["Can't skip a player that isn't open. Add the Audio widget to OBS."],
            hype: ["NO PLAYER OPEN! ADD THE AUDIO WIDGET TO OBS!"],
            chill: ["the player isn't open. add the audio widget to obs."]
        );

        AddNoSpotifyDeviceSlot(catalog, BuiltinResponseSlots.Skip.Key, "skip");
        AddPlaybackControlSlots(catalog);

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NoProvider,
            variables: [],
            informative: ["No active music provider."],
            friendly: ["No music service is connected, so there is nothing to skip."],
            sassy: ["No music service is connected. There is nothing to skip."],
            hype: ["NO MUSIC SERVICE CONNECTED. NOTHING TO SKIP."],
            chill: ["no music service connected."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.PremiumRequired,
            variables: [],
            informative: ["The music service needs a Premium account to skip."],
            friendly: ["The music account needs Premium to skip tracks."],
            sassy: ["Skipping needs Premium. The free tier says no."],
            hype: ["SKIPPING NEEDS PREMIUM."],
            chill: ["skipping needs premium."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NotConnected,
            variables: [],
            informative: ["Couldn't skip — the music connection needs to be reconnected."],
            friendly: ["I couldn't skip — the music connection needs to be reconnected."],
            sassy: ["Can't skip with a dead music connection. Reconnect it."],
            hype: ["NO SKIP! THE MUSIC CONNECTION NEEDS RECONNECTING!"],
            chill: ["couldn't skip. the music connection needs reconnecting."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Forbidden,
            variables: [],
            informative: ["Couldn't skip — the music connection doesn't have permission for that."],
            friendly: ["I couldn't skip — the music connection doesn't have permission for that."],
            sassy: ["The music connection isn't allowed to skip. Fix its permissions."],
            hype: ["NO SKIP! THE MUSIC CONNECTION LACKS PERMISSION!"],
            chill: ["couldn't skip. the music connection lacks permission."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Usage,
            variables: [],
            informative:
            [
                "Usage: !skip <N> — removes YOUR Nth queued request. !skip with no number skips the current track (mods skip any song; you can skip your own).",
            ],
            friendly:
            [
                "Try !skip <N> to remove your Nth request. !skip alone skips the current track (mods skip any song; you can skip your own).",
            ],
            sassy:
            [
                "!skip <N> removes YOUR Nth request. !skip alone skips the current track: mods any song, you only yours. Read again.",
            ],
            hype:
            [
                "!skip <N> REMOVES YOUR NTH REQUEST. !skip ALONE SKIPS THE CURRENT TRACK (MODS ANY, YOU YOUR OWN).",
            ],
            chill:
            [
                "!skip <N> removes your Nth request. !skip alone skips the current track (mods any, you your own).",
            ]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NoRequest,
            variables: ["request.position", "user"],
            informative: ["@{user} You don't have a request at position {request.position}."],
            friendly: ["@{user} I can't find your request at position {request.position}."],
            sassy:
            [
                "@{user} There is no request of yours at position {request.position}. Count again.",
            ],
            hype: ["@{user} NO REQUEST OF YOURS AT POSITION {request.position}."],
            chill: ["@{user} no request of yours at position {request.position}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.RemoveFailed,
            variables: ["user"],
            informative: ["@{user} Couldn't remove that request — try again."],
            friendly: ["@{user} I couldn't remove that request — try again!"],
            sassy: ["@{user} The request refused to leave. Try again."],
            hype: ["@{user} REMOVE FAILED. GO AGAIN."],
            chill: ["@{user} couldn't remove it. try again."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Removed,
            variables: ["track.artist", "track.name", "user"],
            informative: ["@{user} Removed your request: {track.name} by {track.artist}"],
            friendly: ["@{user} Done! I removed your request: {track.name} by {track.artist}"],
            sassy: ["@{user} Fine. {track.name} by {track.artist} is gone from the queue."],
            hype: ["@{user} REMOVED: {track.name} BY {track.artist}."],
            chill: ["@{user} removed {track.name} by {track.artist}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.SkippedOwn,
            variables: [],
            informative: ["Skipped your song."],
            friendly: ["Done, I skipped your song!"],
            sassy: ["Skipped your song. Own your choices, then un-own them."],
            hype: ["YOUR SONG IS SKIPPED. NEXT ONE."],
            chill: ["skipped your song."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NotYours,
            variables: [],
            informative: ["You can only skip songs you requested yourself."],
            friendly: ["You can only skip songs you requested yourself, sorry!"],
            sassy: ["Nice try. You can only skip songs you requested yourself."],
            hype: ["ONLY YOUR OWN REQUESTS CAN BE SKIPPED BY YOU."],
            chill: ["you can only skip your own requests."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NothingPlaying,
            variables: [],
            informative: ["No song is currently playing!"],
            friendly: ["Nothing is playing right now, so there is nothing to skip!"],
            sassy: ["Nothing is playing. There is nothing to skip. Impressive."],
            hype: ["NOTHING PLAYING. NOTHING TO SKIP."],
            chill: ["nothing's playing."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.Current,
            variables: ["volume.level"],
            informative: ["Current volume level is {volume.level}"],
            friendly: ["The volume is at {volume.level}% right now!"],
            sassy: ["Volume is at {volume.level}%. Yes, that is what you hear."],
            hype: ["VOLUME IS AT {volume.level}%!"],
            chill: ["volume is at {volume.level}%."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.Set,
            variables: ["volume.level"],
            informative: ["Volume set to {volume.level}%."],
            friendly: ["Done! The volume is now {volume.level}%."],
            sassy: ["Volume is now {volume.level}%. Your ears are on their own."],
            hype: ["VOLUME SET TO {volume.level}%!"],
            chill: ["volume set to {volume.level}%."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.SetFailed,
            variables: [],
            informative: ["Failed to set volume."],
            friendly: ["I couldn't change the volume — try again in a moment."],
            sassy: ["The volume knob did not move. Try again."],
            hype: ["VOLUME CHANGE FAILED. TRY AGAIN."],
            chill: ["couldn't change the volume."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.NoProvider,
            variables: [],
            informative: ["No active music provider."],
            friendly: ["No music service is connected, so I cannot change the volume."],
            sassy: ["No music service is connected. There is no volume to change."],
            hype: ["NO MUSIC SERVICE CONNECTED."],
            chill: ["no music service connected."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.PremiumRequired,
            variables: [],
            informative: ["The music service needs a Premium account to change the volume."],
            friendly: ["The music account needs Premium to change the volume."],
            sassy: ["Volume control needs Premium. The free tier says no."],
            hype: ["VOLUME CONTROL NEEDS PREMIUM."],
            chill: ["volume control needs premium."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.Banned,
            variables: ["track.name", "user"],
            informative: ["{track.name} banned from being requested again."],
            friendly:
            [
                "@{user} banned \"{track.name}\" from song requests — it will not come back!",
            ],
            sassy: ["@{user} banned \"{track.name}\" from song requests. Good riddance."],
            hype: ["@{user} BANNED \"{track.name}\" FROM SONG REQUESTS. GONE."],
            chill: ["@{user} banned \"{track.name}\" from requests."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.StrikeWarning,
            variables: [],
            informative:
            [
                "You have gotten 5 banned songs now. Don't get your ass banned from this feature.",
            ],
            friendly:
            [
                "That is a lot of banned songs now. Please be careful, or you will lose this feature.",
            ],
            sassy: ["So many banned songs. Keep going and this feature is gone."],
            hype: ["TOO MANY BANNED SONGS. WATCH OUT OR THIS FEATURE IS GONE."],
            chill: ["that is a lot of banned songs. careful, or the feature goes."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.StrikeRevoked,
            variables: [],
            informative:
            [
                "Your permission to redeem songs have been revoked, points will not be refunded if you request more",
            ],
            friendly:
            [
                "Your song request permission is revoked now. Points are not refunded if you request more.",
            ],
            sassy: ["Your song requests are revoked. Points will not come back if you try."],
            hype: ["SONG REQUESTS REVOKED. NO REFUND IF YOU TRY AGAIN."],
            chill: ["your song request permission is revoked. no refund if you try again."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.Nothing,
            variables: [],
            informative: ["Nothing is playing right now, so there is no banger to add."],
            friendly: ["Nothing is playing right now. Start a song and try !banger again!"],
            sassy: ["No song is currently playing! Hard to call silence a banger."],
            hype: ["NOTHING IS PLAYING. NO BANGER TO ADD YET!"],
            chill: ["nothing playing right now, nothing to add."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.Added,
            variables: ["user", "track.name", "track.artist", "playlist.name"],
            informative:
            [
                "@{user} added {track.name} by {track.artist} to the playlist {playlist.name}.",
            ],
            friendly:
            [
                "Nice pick @{user}! {track.name} by {track.artist} is now in {playlist.name}!",
            ],
            sassy:
            [
                "Good taste, @{user}. {track.name} by {track.artist} just made it into {playlist.name}.",
            ],
            hype:
            [
                "@{user} BANGED IT! {track.name} BY {track.artist} IS NOW IN {playlist.name}! BANGER!",
            ],
            chill: ["@{user} added {track.name} by {track.artist} to {playlist.name}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.AlreadyThere,
            variables: ["user", "track.name", "track.artist", "playlist.name"],
            informative:
            [
                "@{user}, {track.name} by {track.artist} is already in the playlist {playlist.name}.",
            ],
            friendly:
            [
                "@{user}, {track.name} by {track.artist} is already in {playlist.name}. Great minds think alike!",
            ],
            sassy:
            [
                "@{user}, {track.name} by {track.artist} is already banging in {playlist.name}! Your taste is consistent, but your memory needs work.",
                "Breaking: @{user} discovers {track.name} by {track.artist} can't be a banger twice! Scientists baffled, {playlist.name} unchanged.",
                "Nice try @{user}, but {track.name} by {track.artist} is already vibing in {playlist.name}. Maybe check before you wreck?",
                "Plot twist @{user}: {track.name} by {track.artist} was already certified banger material in {playlist.name}! At least this mistake was free!",
                "@{user} just tried to double-banger {track.name} by {track.artist}. That's not how {playlist.name} works, bestie.",
                "Ooof @{user}, {track.name} by {track.artist} is already living its best life in {playlist.name}! Good thing this command doesn't cost anything.",
                "Achievement unlocked @{user}: 'Banger Déjà Vu!' {track.name} by {track.artist} is in {playlist.name} already. Reward: great taste, twice.",
                "Fun fact @{user}: {track.name} by {track.artist} is already in {playlist.name}! Less fun fact: you just wasted a perfectly good command.",
                "@{user}, you can't make {track.name} by {track.artist} more banger by adding it to {playlist.name} again. That's not how banger math works!",
                "Alert: @{user} attempted a banger paradox with {track.name} by {track.artist}! {playlist.name} rejected this temporal anomaly. At least it was free!",
            ],
            hype:
            [
                "{track.name} BY {track.artist} IS ALREADY A BANGER IN {playlist.name}, @{user}!",
            ],
            chill: ["@{user}, {track.name} by {track.artist} is already in {playlist.name}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.NoPlaylist,
            variables: [],
            informative:
            [
                "No banger playlist is chosen. A moderator can pick one in the music settings.",
            ],
            friendly: ["There is no bangers playlist yet. Pick one in the music settings!"],
            sassy:
            [
                "No bangers playlist exists. Bold of you to collect bangers with nowhere to put them.",
            ],
            hype: ["NO BANGERS PLAYLIST YET! PICK ONE IN THE MUSIC SETTINGS!"],
            chill: ["no bangers playlist chosen yet, pick one in the music settings."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.Failed,
            variables: ["user", "track.name", "track.artist", "playlist.name"],
            informative:
            [
                "@{user}, {playlist.name} could not be updated with {track.name} by {track.artist}. Try again in a moment.",
            ],
            friendly:
            [
                "Oops @{user}, {track.name} by {track.artist} did not make it into {playlist.name}. Try again in a moment!",
            ],
            sassy:
            [
                "@{user}, {playlist.name} said no to {track.name} by {track.artist}. Try again in a moment.",
            ],
            hype:
            [
                "{playlist.name} FAILED ON {track.name} BY {track.artist}, @{user}! TRY AGAIN IN A MOMENT!",
            ],
            chill:
            [
                "could not add {track.name} by {track.artist} to {playlist.name}, try again soon.",
            ]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Playlist.Key,
            BuiltinResponseSlots.Playlist.Link,
            variables: ["playlist.url"],
            informative: ["The bangers playlist is: {playlist.url}"],
            friendly: ["Here is the bangers playlist, enjoy! {playlist.url}"],
            sassy: ["The bangers playlist, since you asked: {playlist.url}"],
            hype: ["THE BANGERS PLAYLIST IS HERE: {playlist.url}"],
            chill: ["bangers playlist: {playlist.url}"]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Playlist.Key,
            BuiltinResponseSlots.Playlist.NoPlaylist,
            variables: [],
            informative: ["No playlist ID configured."],
            friendly: ["No bangers playlist is set up yet."],
            sassy: ["No playlist ID configured. The bangers are homeless."],
            hype: ["NO BANGERS PLAYLIST SET UP YET!"],
            chill: ["no bangers playlist set up."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Playlist.Key,
            BuiltinResponseSlots.Playlist.NotFound,
            variables: [],
            informative: ["Playlist not found."],
            friendly: ["I could not find the bangers playlist."],
            sassy: ["Playlist not found. The bangers went missing."],
            hype: ["PLAYLIST NOT FOUND!"],
            chill: ["could not find the playlist."]
        );
    }

    private const string NoPlayerLine =
        "The music player isn't open — add the Audio widget to OBS.";

    private const string NoSpotifyDeviceLine =
        "No Spotify device is playing — open Spotify on a device.";

    /// <summary>The "no Spotify device" reply for one playback-control command.</summary>
    private static void AddNoSpotifyDeviceSlot(
        Dictionary<(string, string), SlotEntry> catalog,
        string builtinKey,
        string verb
    ) =>
        Add(
            catalog,
            builtinKey,
            BuiltinResponseSlots.PlaybackControl.NoSpotifyDevice,
            variables: [],
            informative: [NoSpotifyDeviceLine],
            friendly: ["No Spotify device is playing yet — open Spotify on a device!"],
            sassy: [$"Can't {verb} Spotify when no device is playing it. Open Spotify."],
            hype: ["NO SPOTIFY DEVICE! OPEN SPOTIFY!"],
            chill: ["no spotify device playing. open spotify."]
        );

    /// <summary><c>!pause</c> / <c>!resume</c>: one success slot each, plus the shared failure slots.</summary>
    private static void AddPlaybackControlSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.Pause.Key,
            BuiltinResponseSlots.Pause.Paused,
            variables: [],
            informative: ["Paused the music."],
            friendly: ["Music paused — back in a bit!"],
            sassy: ["Music paused. Enjoy the silence."],
            hype: ["MUSIC PAUSED!"],
            chill: ["music paused."]
        );
        Add(
            catalog,
            BuiltinResponseSlots.Resume.Key,
            BuiltinResponseSlots.Resume.Resumed,
            variables: [],
            informative: ["Resumed the music."],
            friendly: ["Music is back on — enjoy!"],
            sassy: ["Music resumed. The silence was getting awkward."],
            hype: ["MUSIC IS BACK ON!"],
            chill: ["music's back on."]
        );

        foreach (
            (string key, string verb) in new[]
            {
                (BuiltinResponseSlots.Pause.Key, "pause"),
                (BuiltinResponseSlots.Resume.Key, "resume"),
            }
        )
        {
            Add(
                catalog,
                key,
                BuiltinResponseSlots.PlaybackControl.NoPlayer,
                variables: [],
                informative: [NoPlayerLine],
                friendly: ["The music player isn't open yet — add the Audio widget to OBS!"],
                sassy: [$"Can't {verb} a player that isn't open. Add the Audio widget to OBS."],
                hype: ["NO PLAYER OPEN! ADD THE AUDIO WIDGET TO OBS!"],
                chill: ["the player isn't open. add the audio widget to obs."]
            );
            AddNoSpotifyDeviceSlot(catalog, key, verb);
            Add(
                catalog,
                key,
                BuiltinResponseSlots.PlaybackControl.NoProvider,
                variables: [],
                informative: ["No music service is set up for this channel."],
                friendly: [$"No music service is set up yet, so there is nothing to {verb}."],
                sassy: [$"No music service is set up. There is nothing to {verb}."],
                hype: ["NO MUSIC SERVICE SET UP!"],
                chill: ["no music service set up."]
            );
            Add(
                catalog,
                key,
                BuiltinResponseSlots.PlaybackControl.PremiumRequired,
                variables: [],
                informative: ["The music service needs a Premium account for that."],
                friendly: [$"The music account needs Premium to {verb}."],
                sassy: [$"Need Premium to {verb}. The free tier says no."],
                hype: [$"PREMIUM NEEDED TO {verb.ToUpperInvariant()}!"],
                chill: [$"{verb} needs premium."]
            );
            Add(
                catalog,
                key,
                BuiltinResponseSlots.PlaybackControl.Failed,
                variables: [],
                informative: [$"Couldn't {verb} the music."],
                friendly: [$"I couldn't {verb} the music — try again in a moment!"],
                sassy: [$"The music refused to {verb}. Try again."],
                hype: [$"{verb.ToUpperInvariant()} FAILED! TRY AGAIN!"],
                chill: [$"couldn't {verb} the music."]
            );
        }
    }

    private static void AddMusicSamples(Dictionary<string, string> samples)
    {
        samples["count"] = "3";
        samples["target"] = "SomeViewer";
        samples["song.name"] = "Never Gonna Give You Up";
        samples["song.artist"] = "Rick Astley";
        samples["song.link"] = "https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT";
        samples["song.status"] = "Now playing:";
        samples["track.name"] = "Never Gonna Give You Up";
        samples["track.artist"] = "Rick Astley";
        samples["track.max"] = "6 minutes";
        samples["track.link"] = "https://open.spotify.com/track/4PTG3Z6ehGkBFwjybzWkR8";
        samples["playlist.name"] = "Road Trip";
        samples["requested.by"] = "StreamFan42";
        samples["query"] = "never gonna give you up";
        samples["queue.list"] =
            "1. Take On Me by a-ha (StreamFan42) | 2. Africa by Toto (NightOwl)";
        samples["queue.mine"] =
            "#2 Africa by Toto (in ~4 min) | #5 Hold the Line by Toto (in ~15 min)";
        samples["queue.mine.count"] = "2";
        samples["queue.count"] = "2";
        samples["queue.next"] = "Take On Me — a-ha";
        samples["queue.more"] = "";
        samples["queue.max"] = "25";
        samples["request.limit"] = "3";
        samples["request.code"] = "K7QM";
        samples["request.position"] = "2";
        samples["retry.minutes"] = "2";
        samples["retry.time"] = "07:00 UTC";
        samples["volume.level"] = "60";
        samples["playlist.url"] = "https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M";
        samples["song.requester"] = "StreamFan42";
        samples["song.source"] = "request";
        samples["song.provider"] = "spotify";
        samples["song.attribution"] = "(requested by StreamFan42)";
    }
}
