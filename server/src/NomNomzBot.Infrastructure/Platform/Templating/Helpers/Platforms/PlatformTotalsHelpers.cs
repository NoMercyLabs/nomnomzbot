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

using NomNomzBot.Application.Contracts.Analytics;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>Per-platform totals: followers, and the chat of this stream.</summary>
internal sealed class PlatformTotalsHelpers()
    : PlatformHelperGroup(
        PlatformRow.Custom(
            "platform.followers.twitch",
            "1,234",
            PlatformSources.Followers | PlatformSources.Links,
            async call =>
                await call.Data.TwitchFollowersAsync() is { } followers
                    ? PlatformText.Count(call, followers)
                    : PlatformText.Nothing
        ),
        StreamTotal("platform.messages.stream", "120", s => s.TotalMessages),
        StreamTotal("platform.chatters.stream", "6", s => s.UniqueChatters)
    )
{
    /// <summary>
    /// A total since the stream started. A connected platform with no chat in the summary counts 0; an offline
    /// channel, or a platform that is not connected, prints nothing.
    /// </summary>
    private static PlatformRow StreamTotal(
        string key,
        string sample,
        Func<ChannelAnalyticsPlatformSummaryDto, long> pick
    ) =>
        PlatformRow.OfPlatform(
            key,
            sample,
            PlatformSources.Summary | PlatformSources.Links,
            async (call, platform) =>
            {
                if (!call.Data.HasStreamWindow || await call.Data.LinkAsync(platform.Key) is null)
                    return PlatformText.Nothing;

                ChannelAnalyticsPlatformSummaryDto? summary = await call.Data.SummaryAsync(
                    platform.Key
                );
                return PlatformText.Count(call, summary is null ? 0 : pick(summary));
            }
        );
}
