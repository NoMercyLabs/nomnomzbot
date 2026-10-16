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
using NomNomzBot.Application.Contracts.Music;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// The variables every <c>!banger</c> reply slot can use, shared by the built-in command and the
/// <c>playlist_add</c> pipeline action so both name the same things: <c>{user}</c>, <c>{track.name}</c>,
/// <c>{track.artist}</c> and <c>{playlist.name}</c>.
/// </summary>
internal static class BangerReplyVariables
{
    /// <summary>Shown only when the provider cannot give the playlist's name back.</summary>
    internal const string FallbackPlaylistName = "Bangers";

    internal static Dictionary<string, string> Build(
        string user,
        string trackName,
        string? trackArtist,
        string playlistName
    ) =>
        new()
        {
            ["user"] = user,
            ["track.name"] = trackName,
            ["track.artist"] = string.IsNullOrWhiteSpace(trackArtist)
                ? "Unknown artist"
                : trackArtist,
            ["playlist.name"] = playlistName,
        };

    /// <summary>Reads the playlist's display name from the provider; a lookup failure never blocks the reply.</summary>
    internal static async Task<string> PlaylistNameAsync(
        IMusicProviderManageApi playlists,
        Guid broadcasterId,
        string provider,
        string playlistId,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<MusicPlaylistDto>> listed = await playlists.ListPlaylistsAsync(
            broadcasterId,
            provider,
            ct
        );
        if (listed is not { IsSuccess: true })
            return FallbackPlaylistName;

        MusicPlaylistDto? match = listed.Value.FirstOrDefault(p => p.Id == playlistId);
        return string.IsNullOrWhiteSpace(match?.Name) ? FallbackPlaylistName : match.Name;
    }
}
