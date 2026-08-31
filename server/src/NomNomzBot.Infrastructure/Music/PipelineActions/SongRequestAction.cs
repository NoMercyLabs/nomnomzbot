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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Song-request action: searches for the query and adds the best match to the queue.
///
/// Parameters:
///   query — search query (required). Supports {variable} substitution.
///
/// Usage example:
///   { "type": "song_request", "query": "{args}" }
/// </summary>
public sealed class SongRequestAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IChatProvider _chat;
    private readonly ILogger<SongRequestAction> _logger;

    public string ActionType => "song_request";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_request.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "query",
                PipelineActionFieldKind.Text,
                Required: true,
                Description: new("pipeline.song_request.query.help")
            ),
        ];

    public SongRequestAction(
        IMusicService music,
        IChatProvider chat,
        ILogger<SongRequestAction> logger
    )
    {
        _music = music;
        _chat = chat;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string query = ResolveParam(action.GetString("query") ?? string.Empty, ctx.Variables);
        if (string.IsNullOrWhiteSpace(query))
            return ActionResult.Failure("song_request requires a non-empty 'query'");

        // One resolve: a track link lands on its exact track, a search phrase falls through to the
        // provider's search — then straight into the fair queue (music-sr.md §3.9).
        // The pipeline's own user_role condition reads this same variable — see UserRoleCondition.
        int requesterRoleLevel = ChatRole
            .Parse(ctx.Variables.GetValueOrDefault("user.role", "viewer"))
            .ToLevelValue();
        Result<MusicTrack> requested = await _music.RequestTrackAsync(
            ctx.BroadcasterId.ToString(),
            query,
            ctx.TriggeredByDisplayName,
            requesterRoleLevel,
            ctx.CancellationToken
        );

        if (requested.IsFailure)
        {
            if (requested.ErrorCode == "NOT_FOUND")
            {
                await _chat.SendMessageAsync(
                    ctx.BroadcasterId,
                    $"@{ctx.TriggeredByDisplayName} No tracks found for \"{query}\".",
                    ctx.CancellationToken
                );
                return ActionResult.Failure($"no tracks found for query: {query}");
            }

            // Each refusal reason gets its own honest chat wording — "no provider" and "provider
            // erroring" are not the same as "your specific song was refused" (TRACK_BLOCKED).
            string chatMessage = requested.ErrorCode switch
            {
                "SR_DISABLED" => $"@{ctx.TriggeredByDisplayName} {requested.ErrorMessage}",
                "MIN_TRUST_LEVEL" => $"@{ctx.TriggeredByDisplayName} {requested.ErrorMessage}",
                "TRACK_BLOCKED" => $"@{ctx.TriggeredByDisplayName} {requested.ErrorMessage}",
                "SERVICE_UNAVAILABLE" =>
                    $"@{ctx.TriggeredByDisplayName} Song requests aren't set up for this channel yet.",
                // A real playlist/album/episode/show/artist link — never a search miss.
                "UNSUPPORTED_CONTENT_TYPE" =>
                    $"@{ctx.TriggeredByDisplayName} {requested.ErrorMessage}",
                _ =>
                    $"@{ctx.TriggeredByDisplayName} Couldn't reach the music service — try again in a moment.",
            };
            await _chat.SendMessageAsync(ctx.BroadcasterId, chatMessage, ctx.CancellationToken);
            return ActionResult.Failure(requested.ErrorMessage ?? "failed to add track to queue");
        }

        MusicTrack track = requested.Value;

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            $"@{ctx.TriggeredByDisplayName} Added to queue: {track.Name} by {track.Artist}",
            ctx.CancellationToken
        );
        return ActionResult.Success($"queued: {track.Name}");
    }

    private static string ResolveParam(string value, Dictionary<string, string> vars)
    {
        if (value.StartsWith('{') && value.EndsWith('}'))
            vars.TryGetValue(value[1..^1], out value!);
        return value;
    }
}
