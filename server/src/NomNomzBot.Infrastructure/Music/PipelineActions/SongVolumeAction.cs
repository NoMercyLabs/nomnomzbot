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
/// Volume action: sets the playback volume.
///
/// Parameters:
///   volume — integer 0-100 (required). Supports {variable} substitution.
///
/// Usage example:
///   { "type": "song_volume", "volume": 50 }
/// </summary>
public sealed class SongVolumeAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IChatProvider _chat;
    private readonly ILogger<SongVolumeAction> _logger;

    public string ActionType => "song_volume";

    public SongVolumeAction(
        IMusicService music,
        IChatProvider chat,
        ILogger<SongVolumeAction> logger
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
        string? volumeStr = action.GetString("volume");
        int volume;

        if (volumeStr is not null && volumeStr.StartsWith('{') && volumeStr.EndsWith('}'))
        {
            ctx.Variables.TryGetValue(volumeStr[1..^1], out string? resolved);
            if (!int.TryParse(resolved, out volume))
                return ActionResult.Failure("song_volume: 'volume' could not be parsed as integer");
        }
        else
        {
            volume = action.GetInt("volume", -1);
        }

        if (volume is < 0 or > 100)
            return ActionResult.Failure("song_volume: 'volume' must be between 0 and 100");

        bool set = await _music.SetVolumeAsync(ctx.BroadcasterId, volume, ctx.CancellationToken);
        if (!set)
            return ActionResult.Failure("song_volume: failed to set volume");

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            $"Volume set to {volume}%.",
            ctx.CancellationToken
        );
        return ActionResult.Success($"volume set to {volume}");
    }
}
