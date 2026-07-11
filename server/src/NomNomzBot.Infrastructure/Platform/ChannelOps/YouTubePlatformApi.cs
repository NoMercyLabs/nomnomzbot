// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat.YouTube;

namespace NomNomzBot.Infrastructure.Platform.ChannelOps;

/// <summary>
/// The YouTube half of the channel-ops seam. A stream title lives on the ACTIVE broadcast
/// (<c>liveBroadcasts.update</c> on the streamer's own token), so retitling requires being live and the
/// token resolves through the live session's PRIMARY channel — the same auth pair the chat sends use.
/// YouTube live streams have no Twitch-style category or tags: a request carrying them is REJECTED
/// (<c>VALIDATION_FAILED</c>), never silently half-applied.
/// </summary>
public sealed class YouTubePlatformApi : IPlatformApi
{
    private readonly IYouTubeLiveChatSessionRegistry _sessions;
    private readonly IYouTubeAccessTokenProvider _tokens;
    private readonly IYouTubeLiveChatClient _client;

    public YouTubePlatformApi(
        IYouTubeLiveChatSessionRegistry sessions,
        IYouTubeAccessTokenProvider tokens,
        IYouTubeLiveChatClient client
    )
    {
        _sessions = sessions;
        _tokens = tokens;
        _client = client;
    }

    public string Provider => AuthEnums.Platform.YouTube;

    public async Task<Result<PlatformStreamInfoApplied>> UpdateStreamInfoAsync(
        Guid broadcasterId,
        PlatformStreamInfoUpdate update,
        CancellationToken cancellationToken = default
    )
    {
        if (update.CategoryName is not null || update.Tags is { Count: > 0 })
            return Result.Failure<PlatformStreamInfoApplied>(
                "YouTube live streams have no category or tags to set — only the title can be changed.",
                "VALIDATION_FAILED"
            );
        if (update.Title is null)
            return Result.Failure<PlatformStreamInfoApplied>(
                "Nothing to update.",
                "VALIDATION_FAILED"
            );

        YouTubeLiveChatSession? session = _sessions.Get(broadcasterId);
        if (session is null)
            return Result.Failure<PlatformStreamInfoApplied>(
                "The channel is not live — YouTube titles are set on the active broadcast.",
                "NOT_FOUND"
            );

        string? accessToken = await _tokens.GetAccessTokenAsync(
            session.PrimaryBroadcasterId,
            cancellationToken
        );
        if (accessToken is null)
            return Result.Failure<PlatformStreamInfoApplied>(
                "No usable YouTube token on the primary channel.",
                "MISSING_SCOPE"
            );

        Result<string> applied = await _client.UpdateActiveBroadcastTitleAsync(
            accessToken,
            update.Title,
            cancellationToken
        );
        if (applied.IsFailure)
            return Result.Failure<PlatformStreamInfoApplied>(
                applied.ErrorMessage!,
                applied.ErrorCode,
                applied.ErrorDetail
            );

        return Result.Success(
            new PlatformStreamInfoApplied(applied.Value, CategoryName: null, Tags: null)
        );
    }
}
