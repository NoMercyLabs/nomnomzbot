// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Playlist-add action (the legacy <c>!banger</c>): appends a track to one of the broadcaster's
/// playlists on the channel's active music provider.
///
/// Parameters:
///   playlist_id — provider playlist id (required). Supports {variable} substitution.
///   track_uri   — track to add (optional; defaults to the CURRENTLY PLAYING track).
///
/// Usage example:
///   { "type": "playlist_add", "playlist_id": "37i9dQZF1DXcBWIGoYBM5M" }
/// </summary>
public sealed class PlaylistAddAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manage;
    private readonly IChatProvider _chat;
    private readonly ILogger<PlaylistAddAction> _logger;

    public string ActionType => "playlist_add";
    public string Category => "music";
    public string Description => "Add the current (or a given) track to a playlist";

    public PlaylistAddAction(
        IMusicService music,
        IMusicProviderManageApi manage,
        IChatProvider chat,
        ILogger<PlaylistAddAction> logger
    )
    {
        _music = music;
        _manage = manage;
        _chat = chat;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string playlistId = ResolveParam(
            action.GetString("playlist_id") ?? string.Empty,
            ctx.Variables
        );
        if (string.IsNullOrWhiteSpace(playlistId))
            return ActionResult.Failure("playlist_add requires a non-empty 'playlist_id'");

        string broadcasterId = ctx.BroadcasterId.ToString();
        string? provider = await _music.GetActiveProviderKeyAsync(
            broadcasterId,
            ctx.CancellationToken
        );
        if (provider is null)
            return ActionResult.Failure("no active music provider");

        string? trackUri = action.GetString("track_uri");
        string? trackName = null;
        if (string.IsNullOrWhiteSpace(trackUri))
        {
            NowPlaying? now = await _music.GetNowPlayingAsync(broadcasterId, ctx.CancellationToken);
            if (now?.TrackUri is null)
                return ActionResult.Failure("nothing is currently playing");
            trackUri = now.TrackUri;
            trackName = now.TrackName;
        }

        Result added = await _manage.AddPlaylistTracksAsync(
            ctx.BroadcasterId,
            provider,
            playlistId,
            [trackUri],
            ctx.CancellationToken
        );
        if (added.IsFailure)
            return ActionResult.Failure(added.ErrorMessage ?? "failed to add track to playlist");

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            trackName is null
                ? "Track added to the playlist."
                : $"Added {trackName} to the playlist.",
            ctx.CancellationToken
        );
        return ActionResult.Success($"playlist_add: {trackUri}");
    }

    private static string ResolveParam(string value, Dictionary<string, string> vars)
    {
        if (value.StartsWith('{') && value.EndsWith('}'))
            vars.TryGetValue(value[1..^1], out value!);
        return value ?? string.Empty;
    }
}
