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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;

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
    private readonly IChatProvider _chat;
    private readonly ILogger<SongResumeAction> _logger;

    public string ActionType => "song_resume";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_resume.description");

    public SongResumeAction(IMusicService music, IChatProvider chat, ILogger<SongResumeAction> logger)
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
        Result resumed = await _music.PlayAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        if (resumed.IsFailure)
        {
            // A success stays quiet (automations pause/resume on go-live and raids); a failure is said out
            // loud, or a closed Audio widget looks like the bot ignoring the command.
            await PlaybackControlFailureReply.SendAsync(
                _chat,
                ctx,
                BuiltinResponseSlots.Resume.Key,
                resumed
            );
            return ActionResult.Failure(resumed.ErrorMessage ?? "resume failed");
        }

        return ActionResult.Success("resumed");
    }
}
