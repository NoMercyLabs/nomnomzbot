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
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;

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

        IReadOnlyList<MusicTrack> results = await _music.SearchAsync(
            ctx.BroadcasterId.ToString(),
            query,
            1,
            ctx.CancellationToken
        );
        if (results.Count == 0)
        {
            await _chat.SendMessageAsync(
                ctx.BroadcasterId,
                $"@{ctx.TriggeredByDisplayName} No tracks found for \"{query}\".",
                ctx.CancellationToken
            );
            return ActionResult.Failure($"no tracks found for query: {query}");
        }

        MusicTrack track = results[0];
        Result added = await _music.AddToQueueAsync(
            ctx.BroadcasterId.ToString(),
            track.Uri,
            ctx.TriggeredByDisplayName,
            ctx.CancellationToken
        );

        if (added.IsFailure)
        {
            if (added.ErrorCode == "TRACK_BLOCKED")
                await _chat.SendMessageAsync(
                    ctx.BroadcasterId,
                    $"@{ctx.TriggeredByDisplayName} {added.ErrorMessage}",
                    ctx.CancellationToken
                );
            return ActionResult.Failure(added.ErrorMessage ?? "failed to add track to queue");
        }

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
        return value ?? string.Empty;
    }
}
