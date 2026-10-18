// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>Reply slots of the music built-ins (<c>!song</c>, <c>!queue</c>, <c>!sr</c>, <c>!skip</c>, …).</summary>
public static partial class BuiltinResponseSlots
{
    /// <summary><c>!song</c> — the currently playing track.</summary>
    public static class Song
    {
        public const string Key = "song";

        /// <summary>A track is playing; <c>{song.name}</c>/<c>{song.artist}</c>/<c>{song.status}</c>/<c>{song.link}</c> are set.</summary>
        public const string Playing = "playing";

        /// <summary>Nothing is playing.</summary>
        public const string Nothing = "nothing";
    }

    /// <summary><c>!queue</c> — the upcoming song queue.</summary>
    public static class Queue
    {
        public const string Key = "queue";

        /// <summary>Queue has tracks; <c>{queue.list}</c>/<c>{queue.count}</c>/<c>{queue.next}</c>/<c>{queue.more}</c> are set.</summary>
        public const string List = "list";

        /// <summary>Queue is empty.</summary>
        public const string Empty = "empty";

        /// <summary>
        /// The subject (the named viewer, or the caller) has requests queued; <c>{user}</c>/<c>{queue.mine}</c>/
        /// <c>{queue.mine.count}</c> are set. <c>{queue.mine}</c> gives each request's position and rough wait.
        /// </summary>
        public const string Mine = "mine";

        /// <summary>The named viewer has no request in the queue; <c>{user}</c> is set.</summary>
        public const string None = "none";
    }

    /// <summary><c>!sr</c> — request a song.</summary>
    public static class SongRequest
    {
        public const string Key = "sr";

        /// <summary>Track added; <c>{track.name}</c>/<c>{track.artist}</c>/<c>{user}</c> are set.</summary>
        public const string Added = "added";

        /// <summary>No track matched the query; <c>{query}</c>/<c>{user}</c> are set.</summary>
        public const string NotFound = "notfound";

        /// <summary>
        /// The track is ALREADY pending in the queue; <c>{track.name}</c>/<c>{track.artist}</c>/<c>{requested.by}</c>/
        /// <c>{user}</c> are set.
        /// </summary>
        public const string Duplicate = "duplicate";

        /// <summary>The track is playing RIGHT NOW; <c>{track.name}</c>/<c>{track.artist}</c>/<c>{user}</c> are set.</summary>
        public const string AlreadyPlaying = "alreadyplaying";

        /// <summary>No song or link was given — no variables.</summary>
        public const string Usage = "usage";

        /// <summary>Song requests are switched off in the channel — no variables.</summary>
        public const string RequestsOff = "requestsoff";

        /// <summary>The queue is closed to new requests (<c>!closequeue</c>) — no variables.</summary>
        public const string QueueClosed = "queueclosed";

        /// <summary>The requester's role is below the channel's minimum; <c>{trust.level}</c> is set.</summary>
        public const string MinTrust = "mintrust";

        /// <summary>The track is on the channel's blocklist; <c>{track.name}</c> is set.</summary>
        public const string TrackBlocked = "trackblocked";

        /// <summary>Nothing plays on any device, so the track could not be queued; <c>{track.name}</c> is set.</summary>
        public const string NoActiveDevice = "noactivedevice";

        /// <summary>A YouTube track cannot play because the audio overlay is not open in OBS; <c>{track.name}</c> is set.</summary>
        public const string NoOverlayPlayer = "nooverlayplayer";

        /// <summary>The music service needs a Premium account for this; <c>{track.name}</c> is set.</summary>
        public const string PremiumRequired = "premiumrequired";

        /// <summary>The track is longer than the channel's longest-song limit; <c>{track.name}</c>/<c>{track.max}</c>/<c>{user}</c> are set.</summary>
        public const string TrackTooLong = "tracktoolong";

        /// <summary>The track is explicit and the channel blocks explicit songs; <c>{track.name}</c> is set.</summary>
        public const string TrackExplicit = "trackexplicit";

        /// <summary>The video's uploader does not allow it on other sites, so the overlay player can never play it; <c>{track.name}</c> is set.</summary>
        public const string NotEmbeddable = "notembeddable";

        /// <summary>The video is age-restricted, which YouTube never plays in an embedded player; <c>{track.name}</c> is set.</summary>
        public const string AgeRestricted = "agerestricted";

        /// <summary>The link is a live stream or a premiere, which has no fixed track to queue — no variables.</summary>
        public const string LiveBroadcast = "livebroadcast";

        /// <summary>The linked video was deleted or is private — no variables.</summary>
        public const string TrackMissing = "trackmissing";

        /// <summary>The text is a link for this channel's music service, but no song could be read from it — no variables.</summary>
        public const string UnreadableLink = "unreadablelink";

        /// <summary>The video reports no readable length, so the length limit cannot be checked — no variables.</summary>
        public const string LengthUnknown = "lengthunknown";

        /// <summary>The linked video is playable but not a music video — no variables.</summary>
        public const string NotMusic = "notmusic";

        /// <summary>The music service's daily request limit is used up; <c>{retry.time}</c> is when it resets.</summary>
        public const string QuotaExhausted = "quotaexhausted";

        /// <summary>The requester used up their requests for this stream; <c>{request.limit}</c> is set.</summary>
        public const string PerStreamLimit = "perstreamlimit";

        /// <summary>
        /// Same refusal on the reward path, where the redeemed points go back; <c>{track.name}</c>/<c>{track.max}</c>/<c>{user}</c> are set.
        /// </summary>
        public const string TrackTooLongRefunded = "tracktoolongrefunded";

        /// <summary>
        /// Track added by a reward or pipeline, with the request's speakable code for <c>!wrongsong</c>;
        /// <c>{track.name}</c>/<c>{track.artist}</c>/<c>{request.code}</c>/<c>{user}</c> are set.
        /// </summary>
        public const string AddedWithCode = "addedwithcode";

        /// <summary>The music connection lost its login and must be reconnected; <c>{track.name}</c> is set.</summary>
        public const string AuthFailed = "authfailed";

        /// <summary>The music connection is not allowed to do this; <c>{track.name}</c> is set.</summary>
        public const string Forbidden = "forbidden";

        /// <summary>The queue holds its maximum; <c>{queue.max}</c> is set.</summary>
        public const string QueueFull = "queuefull";

        /// <summary>The requester already has their maximum in the queue; <c>{request.limit}</c> is set.</summary>
        public const string PerUserLimit = "peruserlimit";

        /// <summary>The music connection needs to be reconnected before search works — no variables.</summary>
        public const string MissingScope = "missingscope";

        /// <summary>The bot owner has not set up the provider's app-level credential (YouTube API key) — no variables.</summary>
        public const string NotConfigured = "notconfigured";

        /// <summary>The music service is having an outage — no variables.</summary>
        public const string ProviderUnavailable = "providerunavailable";

        /// <summary>The music service rate-limited this channel; <c>{retry.minutes}</c> is set.</summary>
        public const string RateLimited = "ratelimited";

        /// <summary>The link is a playlist, album, show or artist, not a track — no variables.</summary>
        public const string UnsupportedContent = "unsupportedcontent";

        /// <summary>The link belongs to another site and its title could not be read — no variables.</summary>
        public const string UnsupportedLink = "unsupportedlink";

        /// <summary>The link belongs to a music service this channel has switched off — no variables.</summary>
        public const string SourceNotAllowed = "sourcenotallowed";

        /// <summary>The requester's role is below the level this channel set for that music service — no variables.</summary>
        public const string SourceNeedsRole = "sourceneedsrole";

        /// <summary>The track cannot play in the streamer's country — no variables.</summary>
        public const string NotPlayable = "notplayable";

        /// <summary>Any other failure reaching the music service; <c>{query}</c> is set.</summary>
        public const string Unreachable = "unreachable";

        /// <summary>No music service is connected and the caller is the broadcaster — no variables.</summary>
        public const string NoProviderBroadcaster = "noproviderbroadcaster";

        /// <summary>No music service is connected and the caller is a moderator — no variables.</summary>
        public const string NoProviderModerator = "noprovidermoderator";

        /// <summary>
        /// The requester banned 10 or more tracks, so their song requests are refused (the old bot's
        /// strike rule) — no variables.
        /// </summary>
        public const string Revoked = "revoked";
    }

    /// <summary><c>!skip</c> — skip the current track (mods+).</summary>
    public static class Skip
    {
        public const string Key = "skip";

        /// <summary>A track was skipped.</summary>
        public const string Skipped = "skipped";

        /// <summary>The skip failed or there was nothing to skip — no variables.</summary>
        public const string Failed = "failed";

        /// <summary>No music service is connected — no variables.</summary>
        public const string NoProvider = "noprovider";

        /// <summary>The music service needs a Premium account to skip — no variables.</summary>
        public const string PremiumRequired = "premiumrequired";

        /// <summary><c>!skip N</c> got a value that is not a positive number — no variables.</summary>
        public const string Usage = "usage";

        /// <summary>The caller has no request at that position; <c>{user}</c>/<c>{request.position}</c> are set.</summary>
        public const string NoRequest = "norequest";

        /// <summary>The caller's request could not be removed; <c>{user}</c> is set.</summary>
        public const string RemoveFailed = "removefailed";

        /// <summary>A viewer skipped the song they requested themselves — no variables.</summary>
        public const string SkippedOwn = "skippedown";

        /// <summary>A viewer tried to skip a song they did not request — no variables.</summary>
        public const string NotYours = "notyours";

        /// <summary>A viewer tried to skip while nothing is playing — no variables.</summary>
        public const string NothingPlaying = "nothingplaying";

        /// <summary>The overlay Audio widget isn't open — no variables.</summary>
        public const string NoPlayer = PlaybackControl.NoPlayer;

        /// <summary>No Spotify device is playing — no variables.</summary>
        public const string NoSpotifyDevice = PlaybackControl.NoSpotifyDevice;

        /// <summary>The caller's own request was removed; <c>{user}</c>/<c>{track.name}</c>/<c>{track.artist}</c> are set.</summary>
        public const string Removed = "removed";
    }

    /// <summary><c>!bansong</c> — usage/error tone slots (S069h).</summary>
    public static class BanSong
    {
        public const string Key = "bansong";

        /// <summary>Nothing is currently playing, so there is no track to ban — no variables.</summary>
        public const string Nothing = "nothing";

        /// <summary>The block write itself failed with no service-supplied reason — no variables.</summary>
        public const string CouldNotBan = "couldnotban";

        /// <summary>The playing track was banned; <c>{user}</c>/<c>{track.name}</c> are set.</summary>
        public const string Banned = "banned";

        /// <summary>
        /// Appended to <see cref="Banned"/> when the moderator has banned 6 to 10 tracks (the old bot's
        /// warning) — no variables.
        /// </summary>
        public const string StrikeWarning = "strikewarning";

        /// <summary>
        /// Appended to <see cref="Banned"/> when the moderator has banned 11 or more tracks (the old bot's
        /// revoke notice) — no variables.
        /// </summary>
        public const string StrikeRevoked = "strikerevoked";
    }

    /// <summary><c>!closequeue</c> — close the song queue to new requests (mods+).</summary>
    public static class CloseQueue
    {
        public const string Key = "closequeue";

        /// <summary>The queue is now closed — no variables.</summary>
        public const string Closed = "closed";

        /// <summary>The queue was already closed — no variables.</summary>
        public const string AlreadyClosed = "alreadyclosed";

        /// <summary>The change could not be saved — no variables.</summary>
        public const string Failed = "failed";
    }

    /// <summary><c>!clearsongs &lt;user&gt;</c> — remove every queued song of one requester (mods+).</summary>
    public static class ClearSongs
    {
        public const string Key = "clearsongs";

        /// <summary>No user name was given — no variables.</summary>
        public const string Usage = "usage";

        /// <summary>The songs were removed — variables: <c>count</c>, <c>target</c>.</summary>
        public const string Cleared = "cleared";

        /// <summary>That user had nothing queued — variables: <c>target</c>.</summary>
        public const string NothingQueued = "nothingqueued";

        /// <summary>The removal failed — variables: <c>target</c>.</summary>
        public const string Failed = "failed";
    }

    /// <summary><c>!openqueue</c> — open the song queue to new requests again (mods+).</summary>
    public static class OpenQueue
    {
        public const string Key = "openqueue";

        /// <summary>The queue is now open — no variables.</summary>
        public const string Opened = "opened";

        /// <summary>The queue was already open — no variables.</summary>
        public const string AlreadyOpen = "alreadyopen";

        /// <summary>The change could not be saved — no variables.</summary>
        public const string Failed = "failed";
    }

    /// <summary><c>!banger</c> — add the playing track to the channel's bangers playlist.</summary>
    public static class Banger
    {
        public const string Key = "banger";

        /// <summary>Nothing is currently playing — no variables.</summary>
        public const string Nothing = "nothing";

        /// <summary>The track was added; <c>{user}</c>/<c>{track.name}</c> are set.</summary>
        public const string Added = "added";

        /// <summary>The track is already in the playlist; <c>{user}</c>/<c>{track.name}</c> are set.</summary>
        public const string AlreadyThere = "alreadythere";

        /// <summary>No playlist is chosen and auto-create is off — no variables.</summary>
        public const string NoPlaylist = "noplaylist";

        /// <summary>The provider could not check, create or add; <c>{user}</c> is set.</summary>
        public const string Failed = "failed";
    }

    /// <summary><c>!volume</c> — usage/error tone slots (S069h).</summary>
    public static class Volume
    {
        public const string Key = "volume";

        /// <summary>An unparsable argument was given — no variables.</summary>
        public const string Usage = "usage";

        /// <summary>The current volume genuinely cannot be read (nothing playing) — no variables.</summary>
        public const string CannotRead = "cannotread";

        /// <summary>The current volume was read; <c>{volume.level}</c> is set.</summary>
        public const string Current = "current";

        /// <summary>The volume was changed; <c>{volume.level}</c> is set.</summary>
        public const string Set = "set";

        /// <summary>The volume change failed — no variables.</summary>
        public const string SetFailed = "setfailed";

        /// <summary>No music service is connected — no variables.</summary>
        public const string NoProvider = "noprovider";

        /// <summary>The music service needs a Premium account to change the volume — no variables.</summary>
        public const string PremiumRequired = "premiumrequired";
    }

    /// <summary>
    /// The failure slots <c>!pause</c> and <c>!resume</c> share: same failure codes, same slot names, so one
    /// mapping from a playback-control error code serves both commands and their pipeline-action twins.
    /// </summary>
    public static class PlaybackControl
    {
        /// <summary>The overlay Audio widget isn't open (YouTube, or any provider that plays there) — no variables.</summary>
        public const string NoPlayer = "noplayer";

        /// <summary>Spotify has no device playing — no variables.</summary>
        public const string NoSpotifyDevice = "nospotifydevice";

        /// <summary>No music service is set up for the channel — no variables.</summary>
        public const string NoProvider = "noprovider";

        /// <summary>The music service needs a Premium account — no variables.</summary>
        public const string PremiumRequired = "premiumrequired";

        /// <summary>Any other failure — no variables.</summary>
        public const string Failed = "failed";

        /// <summary>
        /// The slot that phrases a failed playback-control call, from its result error code. A missing player
        /// splits by provider (<see cref="NoActivePlayerRefusal"/>): Spotify needs a Spotify device, every other
        /// provider plays in the overlay Audio widget — so a YouTube-only channel never hears about Spotify.
        /// </summary>
        public static string FailureSlotFor(string? errorCode, object? errorData) =>
            errorCode switch
            {
                "NO_ACTIVE_DEVICE"
                    when errorData is NoActivePlayerRefusal { Provider: "spotify" } =>
                    NoSpotifyDevice,
                "NO_ACTIVE_DEVICE" => NoPlayer,
                "SERVICE_UNAVAILABLE" => NoProvider,
                "PREMIUM_REQUIRED" => PremiumRequired,
                _ => Failed,
            };
    }

    /// <summary><c>!pause</c> — pauses the music (mods+). Failure slots: <see cref="PlaybackControl"/>.</summary>
    public static class Pause
    {
        public const string Key = "pause";

        /// <summary>The music was paused — no variables.</summary>
        public const string Paused = "paused";
    }

    /// <summary><c>!resume</c> — resumes the music (mods+). Failure slots: <see cref="PlaybackControl"/>.</summary>
    public static class Resume
    {
        public const string Key = "resume";

        /// <summary>The music was resumed — no variables.</summary>
        public const string Resumed = "resumed";
    }

    /// <summary><c>!sr</c> — additional usage/error tone slot (S069i), beyond the personality slots above.</summary>
    public static class SongRequestErrors
    {
        /// <summary>Song requests are disabled and the caller is a plain viewer — no variables.</summary>
        public const string Disabled = "disabled";
    }

    /// <summary><c>!playlist</c> — the link to the channel's bangers playlist.</summary>
    public static class Playlist
    {
        public const string Key = "playlist";

        /// <summary>The bangers playlist link; <c>{playlist.url}</c> is set.</summary>
        public const string Link = "link";

        /// <summary>No bangers playlist is configured — no variables.</summary>
        public const string NoPlaylist = "noplaylist";

        /// <summary>The configured playlist has no public link on its provider — no variables.</summary>
        public const string NotFound = "notfound";
    }
}
