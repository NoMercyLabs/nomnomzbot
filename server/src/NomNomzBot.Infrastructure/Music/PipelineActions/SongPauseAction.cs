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
/// Pause action: pauses playback on the channel's active music provider.
///
/// Usage example:
///   { "type": "song_pause" }
/// </summary>
public sealed class SongPauseAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly ILogger<SongPauseAction> _logger;

    public string ActionType => "song_pause";
    public string Category => "music";
    public string Description => "Pause music playback";

    public SongPauseAction(IMusicService music, ILogger<SongPauseAction> logger)
    {
        _music = music;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result paused = await _music.PauseAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        if (paused.IsFailure)
            return ActionResult.Failure(paused.ErrorMessage ?? "pause failed");

        return ActionResult.Success("paused");
    }
}
