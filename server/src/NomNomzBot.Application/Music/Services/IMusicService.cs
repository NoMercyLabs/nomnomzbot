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

    /// <summary>Remove a specific item from the queue by its zero-based position.</summary>
    Task<bool> RemoveFromQueueAsync(
        string broadcasterId,
        int position,
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
    MusicRepeatMode RepeatMode = MusicRepeatMode.Off
);

/// <summary>The full playback queue including the current track.</summary>
public sealed record MusicQueue(NowPlaying? CurrentTrack, IReadOnlyList<MusicQueueItem> Queue);

/// <summary>An item in the music playback queue.</summary>
public sealed record MusicQueueItem(
    string TrackName,
    string Artist,
    string? ImageUrl,
    int DurationMs,
    string? RequestedBy
);
