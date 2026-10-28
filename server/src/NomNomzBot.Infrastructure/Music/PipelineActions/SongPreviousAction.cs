// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Localization;
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

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_previous.description");

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
