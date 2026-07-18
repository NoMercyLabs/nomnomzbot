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
/// Wrong-song action (the legacy <c>!wrongsong</c>): removes the TRIGGERING user's most recent
/// still-queued request from the song-request queue. Requests are attributed by display name —
/// exactly how <c>song_request</c> enqueues them.
///
/// Usage example:
///   { "type": "song_wrong" }
/// </summary>
public sealed class SongWrongAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IChatProvider _chat;
    private readonly ILogger<SongWrongAction> _logger;

    public string ActionType => "song_wrong";
    public string Category => "music";
    public string Description => "Remove the requester's most recent queued song";

    public SongWrongAction(IMusicService music, IChatProvider chat, ILogger<SongWrongAction> logger)
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
        string broadcasterId = ctx.BroadcasterId.ToString();
        MusicQueue queue = await _music.GetQueueAsync(broadcasterId, ctx.CancellationToken);

        // The queue snapshot is position-ordered; the caller's LAST entry is their newest request.
        int position = -1;
        MusicQueueItem? item = null;
        for (int i = 0; i < queue.Queue.Count; i++)
        {
            if (
                string.Equals(
                    queue.Queue[i].RequestedBy,
                    ctx.TriggeredByDisplayName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                position = i;
                item = queue.Queue[i];
            }
        }

        if (item is null)
        {
            await _chat.SendMessageAsync(
                ctx.BroadcasterId,
                $"@{ctx.TriggeredByDisplayName} You have no queued requests to remove.",
                ctx.CancellationToken
            );
            return ActionResult.Failure("no queued request for the triggering user");
        }

        bool removed = await _music.RemoveFromQueueAsync(
            broadcasterId,
            position,
            ctx.CancellationToken
        );
        if (!removed)
            return ActionResult.Failure("failed to remove the request from the queue");

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            $"@{ctx.TriggeredByDisplayName} Removed your request: {item.TrackName} by {item.Artist}",
            ctx.CancellationToken
        );
        return ActionResult.Success($"removed: {item.TrackName}");
    }
}
