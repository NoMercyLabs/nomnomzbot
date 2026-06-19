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
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Queue action: posts the upcoming queue to chat.
///
/// Parameters:
///   max — maximum number of tracks to show (default: 5).
///
/// Usage example:
///   { "type": "song_queue", "max": 5 }
/// </summary>
public sealed class SongQueueAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IChatProvider _chat;
    private readonly ILogger<SongQueueAction> _logger;

    public string ActionType => "song_queue";

    public SongQueueAction(IMusicService music, IChatProvider chat, ILogger<SongQueueAction> logger)
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
        int max = action.GetInt("max", 5);
        MusicQueue queue = await _music.GetQueueAsync(ctx.BroadcasterId, ctx.CancellationToken);

        if (queue.Queue.Count == 0)
        {
            await _chat.SendMessageAsync(
                ctx.BroadcasterId,
                "The queue is empty.",
                ctx.CancellationToken
            );
            return ActionResult.Success("queue empty");
        }

        IEnumerable<string> entries = queue
            .Queue.Take(max)
            .Select(
                (t, i) =>
                    t.RequestedBy is not null
                        ? $"{i + 1}. {t.TrackName} by {t.Artist} ({t.RequestedBy})"
                        : $"{i + 1}. {t.TrackName} by {t.Artist}"
            );

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            "Queue: " + string.Join(" | ", entries),
            ctx.CancellationToken
        );
        return ActionResult.Success($"showed {queue.Queue.Count} tracks");
    }
}
