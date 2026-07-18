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
/// Resume action: starts or resumes playback on the channel's active music provider.
///
/// Usage example:
///   { "type": "song_resume" }
/// </summary>
public sealed class SongResumeAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly ILogger<SongResumeAction> _logger;

    public string ActionType => "song_resume";
    public string Category => "music";
    public string Description => "Resume music playback";

    public SongResumeAction(IMusicService music, ILogger<SongResumeAction> logger)
    {
        _music = music;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result resumed = await _music.PlayAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        if (resumed.IsFailure)
            return ActionResult.Failure(resumed.ErrorMessage ?? "resume failed");

        return ActionResult.Success("resumed");
    }
}
