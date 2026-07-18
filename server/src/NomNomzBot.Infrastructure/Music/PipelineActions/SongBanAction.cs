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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Ban-song action (the legacy <c>!bansong</c>): blocks the CURRENTLY PLAYING track from future
/// song requests, then skips it. The block lands on the channel's blocklist, which the admission
/// path enforces on every request flow.
///
/// Parameters:
///   reason — optional human reason recorded with the block.
///
/// Usage example:
///   { "type": "song_ban", "reason": "not stream-safe" }
/// </summary>
public sealed class SongBanAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IBlockedTrackService _blockedTracks;
    private readonly IChatProvider _chat;
    private readonly ILogger<SongBanAction> _logger;

    public string ActionType => "song_ban";
    public string Category => "music";
    public string Description => "Block the currently playing track from requests and skip it";

    public SongBanAction(
        IMusicService music,
        IBlockedTrackService blockedTracks,
        IChatProvider chat,
        ILogger<SongBanAction> logger
    )
    {
        _music = music;
        _blockedTracks = blockedTracks;
        _chat = chat;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string broadcasterId = ctx.BroadcasterId.ToString();
        NowPlaying? now = await _music.GetNowPlayingAsync(broadcasterId, ctx.CancellationToken);
        if (now?.TrackUri is null)
            return ActionResult.Failure("nothing is currently playing");

        Result<BlockedTrackDto> blocked = await _blockedTracks.BlockAsync(
            ctx.BroadcasterId,
            new BlockTrackRequest(
                now.Provider,
                now.TrackUri,
                now.TrackName ?? now.TrackUri,
                action.GetString("reason"),
                ctx.TriggeredByUserId
            ),
            ctx.CancellationToken
        );
        if (blocked.IsFailure)
            return ActionResult.Failure(blocked.ErrorMessage ?? "failed to block the track");

        // The banned track should stop playing too — skip is best-effort on top of the block.
        Result skipped = await _music.SkipAsync(broadcasterId, ctx.CancellationToken);
        if (skipped.IsFailure)
            _logger.LogWarning(
                "song_ban blocked '{Track}' for {BroadcasterId} but the skip failed: {Error}",
                now.TrackName,
                broadcasterId,
                skipped.ErrorMessage
            );

        await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            $"Blocked from requests: {now.TrackName ?? now.TrackUri}",
            ctx.CancellationToken
        );
        return ActionResult.Success($"banned: {now.TrackUri}");
    }
}
