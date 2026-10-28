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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;

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
    private readonly IChatProvider _chat;
    private readonly ILogger<SongPauseAction> _logger;

    public string ActionType => "song_pause";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_pause.description");

    public SongPauseAction(IMusicService music, IChatProvider chat, ILogger<SongPauseAction> logger)
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
        Result paused = await _music.PauseAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        if (paused.IsFailure)
        {
            // A success stays quiet (automations pause/resume on go-live and raids); a failure is said out
            // loud, or a closed Audio widget looks like the bot ignoring the command.
            await PlaybackControlFailureReply.SendAsync(
                _chat,
                ctx,
                BuiltinResponseSlots.Pause.Key,
                paused
            );
            return ActionResult.Failure(paused.ErrorMessage ?? "pause failed");
        }

        return ActionResult.Success("paused");
    }
}
