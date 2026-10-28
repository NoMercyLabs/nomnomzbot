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

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_current.description");

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
            ctx.RepliedToChat |= await _chat.SendMessageAsync(
                ctx.BroadcasterId,
                "Nothing is playing right now.",
                ctx.CancellationToken
            );
            return ActionResult.Success("nothing playing");
        }

        string msg = $"Now playing: {now.TrackName} by {now.Artist}";
        if (now.RequestedBy is not null)
            msg += $" (requested by {now.RequestedBy})";

        ctx.RepliedToChat |= await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            msg,
            ctx.CancellationToken
        );
        return ActionResult.Success(msg);
    }
}
