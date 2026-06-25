// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// Extended playback controls not supported by all providers.
/// Implemented by providers that offer seek, shuffle, repeat, device transfer, and playlist management
/// (currently Spotify). Check <c>is IMusicRemoteProvider</c> before calling.
/// </summary>
public interface IMusicRemoteProvider
{
    /// <summary>Seeks to <paramref name="positionMs"/> milliseconds in the current track.</summary>
    Task SeekAsync(
        string broadcasterId,
        int positionMs,
        CancellationToken cancellationToken = default
    );

    /// <summary>Enables or disables shuffle mode.</summary>
    Task SetShuffleAsync(
        string broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sets the repeat mode: <c>off</c>, <c>track</c>, or <c>context</c>.</summary>
    Task SetRepeatAsync(
        string broadcasterId,
        string mode,
        CancellationToken cancellationToken = default
    );

    /// <summary>Transfers playback to <paramref name="deviceId"/>.</summary>
    Task TransferPlaybackAsync(
        string broadcasterId,
        string deviceId,
        bool play = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all available devices for the authenticated user.</summary>
    Task<IReadOnlyList<MusicDevice>> GetDevicesAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the user's playlists.</summary>
    Task<IReadOnlyList<MusicPlaylist>> GetPlaylistsAsync(
        string broadcasterId,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default
    );

    /// <summary>Starts playback of a playlist or context URI.</summary>
    Task PlayContextAsync(
        string broadcasterId,
        string contextUri,
        CancellationToken cancellationToken = default
    );
}

public sealed class MusicDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool IsActive { get; init; }
    public int VolumePercent { get; init; }
}

public sealed class MusicPlaylist
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Uri { get; init; }
    public int TrackCount { get; init; }
    public string? ImageUrl { get; init; }
}
