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
/// Skip action: skips the current track.
///
/// Usage example:
///   { "type": "song_skip" }
/// </summary>
public sealed class SongSkipAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IChatProvider _chat;
    private readonly ILogger<SongSkipAction> _logger;

    public string ActionType => "song_skip";

    public SongSkipAction(IMusicService music, IChatProvider chat, ILogger<SongSkipAction> logger)
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
        bool skipped = await _music.SkipAsync(ctx.BroadcasterId.ToString(), ctx.CancellationToken);
        if (!skipped)
            return ActionResult.Failure("skip failed");

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            "Skipped to the next track.",
            ctx.CancellationToken
        );
        return ActionResult.Success("skipped");
    }
}
