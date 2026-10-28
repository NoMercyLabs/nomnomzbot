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

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_skip.description");

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
        Result skipped = await _music.SkipAsync(
            ctx.BroadcasterId.ToString(),
            ctx.TriggeredByUserId,
            ctx.TriggeredByPlatform,
            ctx.CancellationToken
        );
        if (skipped.IsFailure)
            return ActionResult.Failure(skipped.ErrorMessage ?? "skip failed");

        ctx.RepliedToChat |= await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            "Skipped to the next track.",
            ctx.CancellationToken
        );
        return ActionResult.Success("skipped");
    }
}
