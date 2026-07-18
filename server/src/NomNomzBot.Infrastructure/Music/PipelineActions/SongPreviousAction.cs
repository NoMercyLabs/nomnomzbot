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

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Previous action: jumps back to the previous track on the channel's active music provider.
///
/// Usage example:
///   { "type": "song_previous" }
/// </summary>
public sealed class SongPreviousAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly ILogger<SongPreviousAction> _logger;

    public string ActionType => "song_previous";
    public string Category => "music";
    public string Description => "Jump back to the previous track";

    public SongPreviousAction(IMusicService music, ILogger<SongPreviousAction> logger)
    {
        _music = music;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result previous = await _music.PreviousAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        if (previous.IsFailure)
            return ActionResult.Failure(previous.ErrorMessage ?? "previous failed");

        return ActionResult.Success("previous");
    }
}
