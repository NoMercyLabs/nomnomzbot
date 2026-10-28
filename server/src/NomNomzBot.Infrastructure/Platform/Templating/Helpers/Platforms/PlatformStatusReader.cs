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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.YouTube;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>
/// Reads what one connection shows right now, each through the service of its platform. A platform whose
/// service gives no answer (no token, a failed read) falls back to the live flag on the channel row, with no
/// viewer count: a count that cannot be read is never invented.
/// </summary>
internal static class PlatformStatusReader
{
    private const string YouTubeWatch = "https://youtube.com/watch?v=";

    public static Task<PlatformStatus> ReadAsync(
        IServiceProvider services,
        PlatformLink link,
        ChannelContext? own,
        CancellationToken ct
    ) =>
        PlatformNames.Find(link.Provider)?.Key switch
        {
            AuthEnums.Platform.Twitch => Task.FromResult(ReadTwitch(link, own)),
            AuthEnums.Platform.Kick => ReadKickAsync(services, link, ct),
            AuthEnums.Platform.YouTube => ReadYouTubeAsync(services, link, ct),
            _ => Task.FromResult(Offline()),
        };

    private static PlatformStatus ReadTwitch(PlatformLink link, ChannelContext? own) =>
        own is { IsLive: true }
            ? new(true, own.ViewerCount, own.CurrentTitle, PlatformNames.Url(link))
            : Fallback(link);

    private static async Task<PlatformStatus> ReadKickAsync(
        IServiceProvider services,
        PlatformLink link,
        CancellationToken ct
    )
    {
        KickAccess? access = await services
            .GetRequiredService<IKickAccessTokenProvider>()
            .GetAsync(link.ChannelId, ct);
        if (access is null)
            return Fallback(link);

        Result<KickChannel> read = await services
            .GetRequiredService<IKickApiClient>()
            .GetChannelAsync(access.AccessToken, access.BroadcasterUserId, ct);
        if (read.IsFailure)
            return Fallback(link);

        KickChannel channel = read.Value;
        return channel.IsLive
            ? new(true, channel.ViewerCount, channel.StreamTitle, PlatformNames.Url(link))
            : Offline();
    }

    private static async Task<PlatformStatus> ReadYouTubeAsync(
        IServiceProvider services,
        PlatformLink link,
        CancellationToken ct
    )
    {
        YouTubeLiveChatSession? session = services
            .GetRequiredService<IYouTubeLiveChatSessionRegistry>()
            .Get(link.ChannelId);
        if (session is null)
            return Offline();

        string? token = await services
            .GetRequiredService<IYouTubeAccessTokenProvider>()
            .GetAccessTokenAsync(session.PrimaryBroadcasterId, ct);
        if (token is null)
            return new(true, null, null, null);

        Result<IReadOnlyList<YouTubeActiveChat>> active = await services
            .GetRequiredService<IYouTubeLiveChatClient>()
            .GetActiveLiveChatsAsync(token, ct);
        return active.IsSuccess && active.Value.Count > 0
            ? FromBroadcasts(active.Value)
            : new(true, null, null, null);
    }

    private static PlatformStatus FromBroadcasts(IReadOnlyList<YouTubeActiveChat> broadcasts)
    {
        long[] counts =
        [
            .. broadcasts
                .Where(b => b.ConcurrentViewers.HasValue)
                .Select(b => b.ConcurrentViewers!.Value),
        ];
        int? viewers = counts.Length == 0 ? null : (int)Math.Min(counts.Sum(), int.MaxValue);
        return new(true, viewers, broadcasts[0].Title, YouTubeWatch + broadcasts[0].BroadcastId);
    }

    private static PlatformStatus Fallback(PlatformLink link) =>
        new(link.IsLive, null, null, link.IsLive ? PlatformNames.Url(link) : null);

    private static PlatformStatus Offline() => new(false, null, null, null);
}
