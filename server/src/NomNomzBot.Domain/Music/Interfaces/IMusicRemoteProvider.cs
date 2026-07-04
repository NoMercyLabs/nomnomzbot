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
/// Residual provider surface not (yet) part of the unified music-sr.md seams. Seek/shuffle/repeat/
/// device members moved onto <see cref="IMusicProvider"/> (§3.5); the two members left here back the
/// live paged-playlists and play-context endpoints until their spec homes land: playlist listing is
/// absorbed by the §3.10 manage surface (<c>IMusicProviderManageApi</c>) with the §5 provider-scoped
/// endpoints, and play-context by the §3.5.2 sequencer's context playback. Consumers gate on
/// <see cref="IMusicProvider.Capabilities"/> (Playlists / PlaybackControl) before casting to this.
/// </summary>
public interface IMusicRemoteProvider
{
    /// <summary>Returns the user's playlists, paged. Gated on <see cref="MusicProviderCapabilities.Playlists"/>.</summary>
    Task<IReadOnlyList<MusicPlaylist>> GetPlaylistsAsync(
        Guid broadcasterId,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default
    );

    /// <summary>Starts playback of a playlist or context URI. Gated on <see cref="MusicProviderCapabilities.PlaybackControl"/>.</summary>
    Task PlayContextAsync(
        Guid broadcasterId,
        string contextUri,
        CancellationToken cancellationToken = default
    );
}

public sealed class MusicPlaylist
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Uri { get; init; }
    public int TrackCount { get; init; }
    public string? ImageUrl { get; init; }
}
