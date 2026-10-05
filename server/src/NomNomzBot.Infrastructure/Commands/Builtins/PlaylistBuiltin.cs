// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!playlist</c> (legacy parity) — answers with the link to the channel's bangers playlist, the one
/// <see cref="BangerBuiltin"/> adds to. No provider returns a web URL for a playlist
/// (<see cref="MusicPlaylistDto"/> has none), so the link is built from the stored playlist id for each
/// provider with a known public form. The playing queue is <c>!queue</c>'s job, not this command's.
/// </summary>
public sealed class PlaylistBuiltin(
    IMusicService music,
    IMusicConfigService config,
    IBuiltinResponseComposer composer
) : IBuiltinCommand
{
    private const string SpotifyPlaylistUrl = "https://open.spotify.com/playlist/";
    private const string YouTubePlaylistUrl = "https://www.youtube.com/playlist?list=";

    public string BuiltinKey => "playlist";
    public int DefaultCooldownSeconds => 10;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string broadcasterId = context.BroadcasterId.ToString();
        Result<MusicConfigDto> loaded = await config.GetConfigAsync(broadcasterId, ct);
        string? playlistId = loaded.IsSuccess ? loaded.Value.BangerPlaylistId : null;
        if (string.IsNullOrWhiteSpace(playlistId))
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Playlist.NoPlaylist,
                "No playlist ID configured.",
                [],
                ct
            );

        string? provider =
            loaded.Value.BangerPlaylistProvider
            ?? await music.GetActiveProviderKeyAsync(broadcasterId, ct);
        string? url = ToWebLink(provider, playlistId);
        if (url is null)
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Playlist.NotFound,
                "Playlist not found.",
                [],
                ct
            );

        return await ReplyAsync(
            context,
            BuiltinResponseSlots.Playlist.Link,
            "The bangers playlist is: {playlist.url}",
            new Dictionary<string, string> { ["playlist.url"] = url },
            ct
        );
    }

    private static string? ToWebLink(string? provider, string playlistId) =>
        provider?.ToLowerInvariant() switch
        {
            "spotify" => SpotifyPlaylistUrl + playlistId,
            "youtube" => YouTubePlaylistUrl + playlistId,
            _ => null,
        };

    private async Task<Result<string>> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string fallback,
        Dictionary<string, string> variables,
        CancellationToken ct
    ) =>
        Result.Success(
            await composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Playlist.Key,
                slot,
                fallback,
                variables,
                ct
            )
        );
}
