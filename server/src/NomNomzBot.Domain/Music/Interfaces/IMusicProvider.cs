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
/// Abstraction for music playback providers (Spotify, YouTube Music, etc.).
/// </summary>
public interface IMusicProvider
{
    Task PlayAsync(string broadcasterId, CancellationToken cancellationToken = default);

    Task PauseAsync(string broadcasterId, CancellationToken cancellationToken = default);

    Task SkipAsync(string broadcasterId, CancellationToken cancellationToken = default);

    Task<TrackInfo?> GetCurrentTrackAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<TrackInfo>> SearchAsync(
        string broadcasterId,
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default
    );

    Task<bool> AddToQueueAsync(
        string broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    );
}

public class TrackInfo
{
    public required string TrackName { get; init; }
    public required string Artist { get; init; }
    public required string Album { get; init; }
    public required string TrackUri { get; init; }
    public string? AlbumArtUrl { get; init; }
    public int DurationMs { get; init; }
    public required string Provider { get; init; }

    // Only meaningful on a GetCurrentTrackAsync result (the provider's "currently playing" read) — a
    // SearchAsync hit is not "playing" anything, so it is left at the default (false / 0) there.
    public bool IsPlaying { get; init; }
    public int ProgressMs { get; init; }
}
