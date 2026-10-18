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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Playlist-add action (the legacy <c>!banger</c>): appends a track to one of the broadcaster's
/// playlists on the channel's active music provider.
///
/// Parameters:
///   playlist_id — provider playlist id (required). Supports {variable} substitution.
///   track_uri   — track to add (optional; defaults to the CURRENTLY PLAYING track).
///   message     — chat response template (optional; when absent the reply comes from the channel's
///                 personality slot <c>banger.added</c>, the same one the !banger built-in uses).
///                 Streamer-editable, resolved through the shared <see cref="ITemplateResolver"/>
///                 grammar, so it can reference {track_name} and {playlist_id} instead of hardcoding
///                 the raw provider id.
///   already_message — chat template sent instead when the track is already in the playlist
///                 (optional; absent uses the <c>banger.alreadythere</c> slot, empty sends nothing).
///                 Same variables as <c>message</c>.
///
/// Usage example:
///   { "type": "playlist_add", "playlist_id": "37i9dQZF1DXcBWIGoYBM5M", "message": "Added {track_name} to the bangers playlist!" }
/// </summary>
public sealed class PlaylistAddAction : ICommandAction
{
    private const string NeutralAdded =
        "@{user} added \"{track.name}\" by {track.artist} to {playlist.name}.";
    private const string NeutralAlreadyThere =
        "@{user}, \"{track.name}\" by {track.artist} is already in {playlist.name}.";
    private const string NeutralFailed =
        "@{user}, {playlist.name} could not be updated with \"{track.name}\" by {track.artist}.";
    private const string NeutralNothing = "Nothing is playing right now.";

    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manage;
    private readonly IChatProvider _chat;
    private readonly ITemplateResolver _resolver;
    private readonly IBuiltinResponseComposer _composer;
    private readonly IChannelRegistry _registry;
    private readonly ILogger<PlaylistAddAction> _logger;

    public string ActionType => "playlist_add";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.playlist_add.description");

    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "playlist_id",
                PipelineActionFieldKind.MusicPlaylist,
                Required: true,
                Description: new("pipeline.playlist_add.playlist_id.help")
            ),
            new(
                "track_uri",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.playlist_add.track_uri.help")
            ),
            new(
                "message",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.playlist_add.message.help")
            ),
            new(
                "already_message",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.playlist_add.already_message.help")
            ),
        ];

    public PlaylistAddAction(
        IMusicService music,
        IMusicProviderManageApi manage,
        IChatProvider chat,
        ITemplateResolver resolver,
        IBuiltinResponseComposer composer,
        IChannelRegistry registry,
        ILogger<PlaylistAddAction> logger
    )
    {
        _music = music;
        _manage = manage;
        _chat = chat;
        _resolver = resolver;
        _composer = composer;
        _registry = registry;
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
        string? trackArtist = null;
        if (string.IsNullOrWhiteSpace(trackUri))
        {
            NowPlaying? now = await _music.GetNowPlayingAsync(broadcasterId, ctx.CancellationToken);
            if (now?.TrackUri is null)
            {
                await SendSlotAsync(
                    ctx,
                    BuiltinResponseSlots.Banger.Nothing,
                    NeutralNothing,
                    new Dictionary<string, string>()
                );
                return ActionResult.Failure("nothing is currently playing");
            }

            trackUri = now.TrackUri;
            trackName = now.TrackName;
            trackArtist = now.Artist;
        }

        Dictionary<string, string> seed = new(ctx.Variables, StringComparer.OrdinalIgnoreCase)
        {
            ["playlist_id"] = playlistId,
            ["track_uri"] = trackUri,
            ["track_name"] = trackName ?? trackUri,
        };
        Dictionary<string, string> slotVariables = BangerReplyVariables.Build(
            ctx.TriggeredByChatName,
            trackName ?? trackUri,
            trackArtist,
            await BangerReplyVariables.PlaylistNameAsync(
                _manage,
                ctx.BroadcasterId,
                provider,
                playlistId,
                ctx.CancellationToken
            )
        );

        Result<bool> present = await _manage.IsTrackInPlaylistAsync(
            ctx.BroadcasterId,
            provider,
            playlistId,
            trackUri,
            ctx.CancellationToken
        );
        if (present.IsFailure)
        {
            await SendSlotAsync(
                ctx,
                BuiltinResponseSlots.Banger.Failed,
                NeutralFailed,
                slotVariables
            );
            return ActionResult.Failure(
                present.ErrorMessage ?? "failed to check the playlist for the track"
            );
        }

        if (present.Value)
        {
            string? alreadyTemplate = action.GetString("already_message");
            if (alreadyTemplate is null)
                return await SendSlotAsync(
                    ctx,
                    BuiltinResponseSlots.Banger.AlreadyThere,
                    NeutralAlreadyThere,
                    slotVariables
                );
            if (string.IsNullOrWhiteSpace(alreadyTemplate))
                return ActionResult.Success();
            return await SendResolvedAsync(ctx, alreadyTemplate, seed);
        }

        Result added = await _manage.AddPlaylistTracksAsync(
            ctx.BroadcasterId,
            provider,
            playlistId,
            [trackUri],
            ctx.CancellationToken
        );
        if (added.IsFailure)
        {
            await SendSlotAsync(
                ctx,
                BuiltinResponseSlots.Banger.Failed,
                NeutralFailed,
                slotVariables
            );
            return ActionResult.Failure(added.ErrorMessage ?? "failed to add track to playlist");
        }

        string? addedTemplate = action.GetString("message");
        if (addedTemplate is null)
            return await SendSlotAsync(
                ctx,
                BuiltinResponseSlots.Banger.Added,
                NeutralAdded,
                slotVariables
            );
        return await SendResolvedAsync(ctx, addedTemplate, seed);
    }

    /// <summary>Phrases a reply through the channel's personality slots, so it matches the <c>!banger</c> built-in.</summary>
    private async Task<ActionResult> SendSlotAsync(
        PipelineExecutionContext ctx,
        string slot,
        string neutralFallback,
        Dictionary<string, string> variables
    )
    {
        string text = await _composer.ComposeAsync(
            new BuiltinResponseRequest
            {
                BroadcasterId = ctx.BroadcasterId,
                Personality = PersonalityTone.Normalize(
                    _registry.Get(ctx.BroadcasterId)?.Personality
                ),
                BuiltinKey = BuiltinResponseSlots.Banger.Key,
                Slot = slot,
                NeutralFallback = neutralFallback,
                Variables = variables,
            },
            ctx.CancellationToken
        );
        ctx.RepliedToChat |= await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            text,
            ctx.CancellationToken
        );
        return ActionResult.Success(text);
    }

    private async Task<ActionResult> SendResolvedAsync(
        PipelineExecutionContext ctx,
        string template,
        Dictionary<string, string> seed
    )
    {
        string resolved = await _resolver.ResolveAsync(
            template,
            ChatMentionName.ForChat(seed),
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        ctx.RepliedToChat |= await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            resolved,
            ctx.CancellationToken
        );
        return ActionResult.Success(resolved);
    }

    private static string ResolveParam(string value, Dictionary<string, string> vars)
    {
        if (value.StartsWith('{') && value.EndsWith('}'))
            return vars.TryGetValue(value[1..^1], out string? resolved) ? resolved : string.Empty;
        return value;
    }
}
