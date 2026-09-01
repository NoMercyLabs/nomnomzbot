// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// Abstraction over music playback services (Spotify, YouTube, etc.).
/// Manages search, playback control, and the request queue per channel.
/// </summary>
public interface IMusicService
{
    /// <summary>Search for tracks by query string.</summary>
    Task<IReadOnlyList<MusicTrack>> SearchAsync(
        string broadcasterId,
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default
    );

    /// <summary>Start or resume playback. Fails <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c> (music-sr.md §3.1).</summary>
    Task<Result> PlayAsync(string broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Pause playback. Fails <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c>.</summary>
    Task<Result> PauseAsync(string broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Skip to the next track in the queue. Fails <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c>.</summary>
    Task<Result> SkipAsync(string broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Provider previous-track. Gated on <c>Previous</c>; fails <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c>.</summary>
    Task<Result> PreviousAsync(string broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Get the current playback queue for a channel.</summary>
    Task<MusicQueue> GetQueueAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Admit a track into the song-request queue — THE admission path every SR flow (command, reward
    /// pipeline, public SR page, script) goes through. Fails <c>VALIDATION_FAILED</c> on a bad channel
    /// id, <c>SERVICE_UNAVAILABLE</c> when no provider is active, and <c>TRACK_BLOCKED</c> when the
    /// resolved track is on the channel's blocklist — refused before it ever reaches the fair queue.
    /// </summary>
    Task<Result> AddToQueueAsync(
        string broadcasterId,
        string trackUri,
        string? requestedBy = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The single !sr / song-request entry point: resolves <paramref name="query"/> — a provider track
    /// link/URI/id OR a free-text search phrase — to one track and admits it into the queue. Tries an
    /// authoritative link/id resolve first, so a pasted Spotify/YouTube link lands the exact track instead
    /// of being run through text search (where it would find nothing); falls back to the provider's search
    /// when the input isn't a resolvable link. Enforces the channel's <c>MusicConfig</c> gate first:
    /// <c>SR_DISABLED</c> when <c>IsEnabled</c> is off; <c>MIN_TRUST_LEVEL</c> when
    /// <paramref name="requesterRoleLevel"/> is supplied and sits below the configured
    /// <c>MinTrustLevel</c> floor — a null <paramref name="requesterRoleLevel"/> (dashboard/public-page/
    /// script callers, which have their own authorization boundary) skips the floor check, never the
    /// <c>IsEnabled</c> check. Fails <c>NOT_FOUND</c> when nothing resolves, plus every
    /// <see cref="AddToQueueAsync"/> failure mode (<c>SERVICE_UNAVAILABLE</c>, <c>TRACK_BLOCKED</c>,
    /// <c>VALIDATION_FAILED</c>). On success, returns the resolved track for the caller's confirmation message.
    /// </summary>
    Task<Result<MusicTrack>> RequestTrackAsync(
        string broadcasterId,
        string query,
        string? requestedBy = null,
        int? requesterRoleLevel = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Set the playback volume (0-100). Gated on <c>Volume</c>; fails <c>VALIDATION_FAILED</c> out of range,
    /// <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c> per music-sr.md §3.1.</summary>
    Task<Result> SetVolumeAsync(
        string broadcasterId,
        int volume,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get the currently playing track, if any.</summary>
    Task<NowPlaying?> GetNowPlayingAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The provider key (<c>spotify</c>, <c>youtube</c>, …) of the channel's active music provider,
    /// resolved exactly as every playback member resolves it; null when none is connected.
    /// </summary>
    Task<string?> GetActiveProviderKeyAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// S003 — the channel's active music provider's live-observed auth state: <c>"needs_reauth"</c> (a
    /// call came back 401 — the token is dead), <c>"forbidden"</c> (a call came back 403 for a reason
    /// other than premium — the grant lacks permission), or null when the connection is healthy (or
    /// nothing has been observed yet, or no provider is connected). Feeds both the integrations status
    /// surface and the Music page, so a broken connection stops looking silently "connected".
    /// </summary>
    Task<string?> GetActiveProviderAuthStatusAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Remove a specific item from the queue by its zero-based position.</summary>
    Task<bool> RemoveFromQueueAsync(
        string broadcasterId,
        int position,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Moves the queued item at <paramref name="position"/> to the front of the queue — a moderator's
    /// "play this one next" dashboard action. Returns false when there is no queue for the channel or
    /// <paramref name="position"/> is out of range.
    /// </summary>
    Task<bool> PromoteToTopAsync(
        string broadcasterId,
        int position,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Bans the queued (or historical) track at <paramref name="position"/> from future song requests —
    /// the dashboard counterpart to <c>!bansong</c> (which only bans whatever is currently playing).
    /// Reuses <see cref="IBlockedTrackService.BlockAsync"/> so the block is enforced by the same
    /// admission gate, then removes the now-blocked entry from the live queue. Fails <c>NOT_FOUND</c>
    /// when <paramref name="position"/> is out of range.
    /// </summary>
    Task<Result<BlockedTrackDto>> BanQueuedTrackAsync(
        string broadcasterId,
        int position,
        string? blockedByUserId = null,
        CancellationToken cancellationToken = default
    );

    // ── Extended remote controls (provider-dependent) ──────────────────────────

    /// <summary>Seek to <paramref name="positionMs"/> in the current track. Fails <c>VALIDATION_FAILED</c> when negative,
    /// <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c> otherwise (music-sr.md §3.1).</summary>
    Task<Result> SeekAsync(
        string broadcasterId,
        int positionMs,
        CancellationToken cancellationToken = default
    );

    /// <summary>Enable or disable shuffle. Fails <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c>.</summary>
    Task<Result> SetShuffleAsync(
        string broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    );

    /// <summary>Set repeat mode: <c>off</c>, <c>track</c>, or <c>context</c>. Fails <c>VALIDATION_FAILED</c> on an
    /// unknown mode, <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c> otherwise.</summary>
    Task<Result> SetRepeatAsync(
        string broadcasterId,
        string mode,
        CancellationToken cancellationToken = default
    );

    /// <summary>Transfer playback to another device. Fails <c>CAPABILITY_UNSUPPORTED</c> / <c>PREMIUM_REQUIRED</c>.</summary>
    Task<Result> TransferPlaybackAsync(
        string broadcasterId,
        string deviceId,
        bool play = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>Return available playback devices. Empty when unsupported.</summary>
    Task<IReadOnlyList<MusicDeviceDto>> GetDevicesAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Return the user's playlists. Empty when unsupported.</summary>
    Task<IReadOnlyList<MusicPlaylistDto>> GetPlaylistsAsync(
        string broadcasterId,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default
    );

    /// <summary>Start playback of a playlist/context URI. Returns false when unsupported.</summary>
    Task<bool> PlayContextAsync(
        string broadcasterId,
        string contextUri,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// A short-lived scoped access token an in-browser SDK player (the OBS playback widget's Spotify Web
    /// Playback SDK) can hold directly, so it can become the active Connect device and stream real audio.
    /// Fails <c>CAPABILITY_UNSUPPORTED</c> when the active provider doesn't declare
    /// <see cref="MusicProviderCapabilities.EmbeddedPlayback"/>, and <c>MISSING_SCOPE</c> when the channel's
    /// connection hasn't granted the streaming scope yet (a pre-feature Spotify connection, or provider
    /// declines).
    /// </summary>
    Task<Result<string>> GetEmbeddedPlaybackTokenAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );
}

public sealed record MusicDeviceDto(
    string Id,
    string Name,
    string Type,
    bool IsActive,
    int VolumePercent
);

public sealed record MusicPlaylistDto(
    string Id,
    string Name,
    string Uri,
    int TrackCount,
    string? ImageUrl
);

/// <summary>A music track from a search result.</summary>
public sealed record MusicTrack(
    string Uri,
    string Name,
    string Artist,
    string? Album,
    string? ImageUrl,
    int DurationMs,
    string Provider
);

/// <summary>Current playback state for a channel. <paramref name="TrackUri"/> is the provider URI/id
/// of the playing track — what <c>song_ban</c> blocks and <c>playlist_add</c> saves.</summary>
public sealed record NowPlaying(
    string? TrackName,
    string? Artist,
    string? Album,
    string? ImageUrl,
    int DurationMs,
    int ProgressMs,
    bool IsPlaying,
    int Volume,
    string? RequestedBy,
    string Provider,
    string? TrackUri = null,
    bool ShuffleEnabled = false,
    MusicRepeatMode RepeatMode = MusicRepeatMode.Off,
    string? ArtistId = null,
    bool CanSetShuffle = true,
    bool CanSetRepeat = true,
    bool CanSkipNext = true,
    bool CanSkipPrevious = true,
    bool CanSeek = true,
    bool CanPause = true,
    bool CanResume = true
);

/// <summary>The full playback queue including the current track.</summary>
public sealed record MusicQueue(NowPlaying? CurrentTrack, IReadOnlyList<MusicQueueItem> Queue);

/// <summary>An item in the music playback queue. <paramref name="Cost"/> is 0 for a free request.</summary>
public sealed record MusicQueueItem(
    string TrackName,
    string Artist,
    string? ImageUrl,
    int DurationMs,
    string? RequestedBy,
    int Cost = 0
);
