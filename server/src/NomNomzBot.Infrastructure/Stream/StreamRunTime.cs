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

using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// The one place that turns "when the channel went live" into "how long the stream ran". Both platforms'
/// offline signals (Twitch stream.offline, Kick livestream.status.updated) call it, so the published
/// <c>ChannelOfflineEvent.StreamDuration</c> has a single source.
/// </summary>
internal static class StreamRunTime
{
    /// <summary>
    /// The channel's recorded go-live moment: <c>WentLiveAt</c>, or <c>LastStreamStartedAt</c> when the status
    /// poll already saw the stream end. Null when neither is known. It names the stream that is ending.
    /// </summary>
    public static DateTimeOffset? StartOf(IChannelRegistry registry, Guid broadcasterId)
    {
        ChannelContext? ctx = registry.Get(broadcasterId);
        return ctx?.WentLiveAt ?? ctx?.LastStreamStartedAt;
    }

    /// <summary>
    /// Time from the channel's recorded go-live moment to <paramref name="offlineAt"/>. The moment is
    /// <c>WentLiveAt</c>, or <c>LastStreamStartedAt</c> when the status poll already saw the stream end.
    /// Zero when the go-live moment is unknown (channel not in the registry, or no anchor yet) or lies in the future.
    /// </summary>
    public static TimeSpan Between(
        IChannelRegistry registry,
        Guid broadcasterId,
        DateTimeOffset offlineAt
    )
    {
        DateTimeOffset? wentLiveAt = StartOf(registry, broadcasterId);
        if (wentLiveAt is null)
            return TimeSpan.Zero;

        TimeSpan elapsed = offlineAt - wentLiveAt.Value;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}
