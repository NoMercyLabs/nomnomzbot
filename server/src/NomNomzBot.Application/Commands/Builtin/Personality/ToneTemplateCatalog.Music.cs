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
            BuiltinResponseSlots.SongRequest.MinTrust,
            variables: ["trust.level"],
            informative: ["Song requests need at least {trust.level} right now."],
            friendly: ["Song requests are open to {trust.level} and up right now — hang in there!"],
            sassy:
            [
                "Song requests need at least {trust.level}. Come back when you have earned it.",
            ],
            hype: ["YOU NEED {trust.level} OR HIGHER TO REQUEST SONGS. LEVEL UP."],
            chill: ["requests need {trust.level} or higher right now."]
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
            BuiltinResponseSlots.Skip.Usage,
            variables: [],
            informative:
            [
                "Usage: !skip <N> — removes YOUR Nth queued request. !skip with no number skips the current track (mods+).",
            ],
            friendly:
            [
                "Try !skip <N> to remove your Nth request. !skip alone skips the current track (mods only).",
            ],
            sassy: ["!skip <N> removes YOUR Nth request. !skip alone is for mods. Read again."],
            hype:
            [
                "!skip <N> REMOVES YOUR NTH REQUEST. !skip ALONE SKIPS THE CURRENT TRACK (MODS).",
            ],
            chill: ["!skip <N> removes your Nth request. !skip alone is mods only."]
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
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.Current,
            variables: ["volume.level"],
            informative: ["Volume is at {volume.level}%."],
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
            informative: ["@{user} banned \"{track.name}\" from song requests."],
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
            BuiltinResponseSlots.Playlist.Key,
            BuiltinResponseSlots.Playlist.Empty,
            variables: [],
            informative: ["Nothing is playing and the queue is empty."],
            friendly: ["Nothing is playing and the queue is empty — request a song with !sr!"],
            sassy: ["Nothing is playing and the queue is empty. Very avant-garde."],
            hype: ["NOTHING PLAYING. QUEUE EMPTY. SEND SONGS WITH !sr!"],
            chill: ["nothing playing, queue is empty."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Playlist.Key,
            BuiltinResponseSlots.Playlist.Summary,
            variables: ["playlist.count", "playlist.nowplaying", "playlist.upcoming"],
            informative:
            [
                "Now playing: {playlist.nowplaying} — up next: {playlist.upcoming} ({playlist.count} queued)",
            ],
            friendly:
            [
                "Now playing {playlist.nowplaying}! Up next: {playlist.upcoming} ({playlist.count} queued)",
            ],
            sassy:
            [
                "Now playing: {playlist.nowplaying}. Up next: {playlist.upcoming}. {playlist.count} queued, none of them your fault.",
            ],
            hype:
            [
                "NOW PLAYING: {playlist.nowplaying}. UP NEXT: {playlist.upcoming} ({playlist.count} QUEUED)!",
            ],
            chill:
            [
                "playing {playlist.nowplaying}. next: {playlist.upcoming} ({playlist.count} queued).",
            ]
        );
    }

    private static void AddMusicSamples(Dictionary<string, string> samples)
    {
        samples["song.name"] = "Never Gonna Give You Up";
        samples["song.artist"] = "Rick Astley";
        samples["song.status"] = "Now playing:";
        samples["track.name"] = "Never Gonna Give You Up";
        samples["track.artist"] = "Rick Astley";
        samples["track.link"] = "https://open.spotify.com/track/4PTG3Z6ehGkBFwjybzWkR8";
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
        samples["request.position"] = "2";
        samples["trust.level"] = "Follower";
        samples["volume.level"] = "60";
        samples["playlist.nowplaying"] = "Never Gonna Give You Up by Rick Astley";
        samples["playlist.upcoming"] = "Take On Me by a-ha, Africa by Toto";
        samples["playlist.count"] = "2";
        samples["song.requester"] = "StreamFan42";
        samples["song.source"] = "request";
        samples["song.provider"] = "spotify";
        samples["song.attribution"] = "(requested by StreamFan42)";
    }
}
