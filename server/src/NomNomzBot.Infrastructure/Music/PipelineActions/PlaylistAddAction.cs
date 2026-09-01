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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
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
///   message     — chat response template (optional; defaults to <see cref="DefaultMessageTemplate"/>).
///                 Streamer-editable, resolved through the shared <see cref="ITemplateResolver"/>
///                 grammar, so it can reference {track_name} and {playlist_id} instead of hardcoding
///                 the raw provider id.
///
/// Usage example:
///   { "type": "playlist_add", "playlist_id": "37i9dQZF1DXcBWIGoYBM5M", "message": "Added {track_name} to the bangers playlist!" }
/// </summary>
public sealed class PlaylistAddAction : ICommandAction
{
    /// <summary>Used when the streamer has not overridden the "message" field — never exposes the raw playlist/track id.</summary>
    public const string DefaultMessageTemplate = "Added {track_name} to the playlist.";

    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manage;
    private readonly IChatProvider _chat;
    private readonly ITemplateResolver _resolver;
    private readonly ILogger<PlaylistAddAction> _logger;

    public string ActionType => "playlist_add";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.playlist_add.description");

    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "playlist_id",
                PipelineActionFieldKind.ResourceId,
                Required: true,
                Description: new("pipeline.playlist_add.playlist_id.help")
            ),
            new(
                "track_uri",
                PipelineActionFieldKind.ResourceId,
                Description: new("pipeline.playlist_add.track_uri.help")
            ),
            new(
                "message",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.playlist_add.message.help")
            ),
        ];

    public PlaylistAddAction(
        IMusicService music,
        IMusicProviderManageApi manage,
        IChatProvider chat,
        ITemplateResolver resolver,
        ILogger<PlaylistAddAction> logger
    )
    {
        _music = music;
        _manage = manage;
        _chat = chat;
        _resolver = resolver;
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

        string template = action.GetString("message") ?? DefaultMessageTemplate;
        Dictionary<string, string> seed = new(ctx.Variables, StringComparer.OrdinalIgnoreCase)
        {
            ["playlist_id"] = playlistId,
            ["track_uri"] = trackUri,
            ["track_name"] = trackName ?? trackUri,
        };
        string resolved = await _resolver.ResolveAsync(
            template,
            seed,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        await _chat.SendMessageAsync(ctx.BroadcasterId, resolved, ctx.CancellationToken);
        return ActionResult.Success(resolved);
    }

    private static string ResolveParam(string value, Dictionary<string, string> vars)
    {
        if (value.StartsWith('{') && value.EndsWith('}'))
            vars.TryGetValue(value[1..^1], out value!);
        return value ?? string.Empty;
    }
}
