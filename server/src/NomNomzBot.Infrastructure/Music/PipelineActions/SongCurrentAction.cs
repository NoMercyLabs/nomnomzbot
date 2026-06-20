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
/// Now-playing action: posts the current song to chat.
///
/// Usage example:
///   { "type": "song_current" }
/// </summary>
public sealed class SongCurrentAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IChatProvider _chat;
    private readonly ILogger<SongCurrentAction> _logger;

    public string ActionType => "song_current";

    public SongCurrentAction(
        IMusicService music,
        IChatProvider chat,
        ILogger<SongCurrentAction> logger
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
        NowPlaying? now = await _music.GetNowPlayingAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        if (now is null || string.IsNullOrWhiteSpace(now.TrackName))
        {
            await _chat.SendMessageAsync(
                ctx.BroadcasterId,
                "Nothing is playing right now.",
                ctx.CancellationToken
            );
            return ActionResult.Success("nothing playing");
        }

        string msg = $"Now playing: {now.TrackName} by {now.Artist}";
        if (now.RequestedBy is not null)
            msg += $" (requested by {now.RequestedBy})";

        await _chat.SendMessageAsync(ctx.BroadcasterId, msg, ctx.CancellationToken);
        return ActionResult.Success(msg);
    }
}
