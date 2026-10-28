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
        if (update.ContentLabels is not null || update.IsBrandedContent is not null)
            return Result.Failure<PlatformStreamInfoApplied>(
                "YouTube live streams have no Twitch-style content labels to set.",
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
