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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>
/// One concurrent-viewer sample from the Helix Get Streams reconciliation poll (every ~2 minutes while
/// the channel is live; never published while offline). The journal thus carries a per-stream viewer-count
/// time series, and <c>ChannelAnalyticsDailyProjection</c> folds the daily maximum into
/// <c>ChannelAnalyticsDaily.PeakViewers</c>.
/// </summary>
public sealed class StreamViewerCountSampledEvent : DomainEventBase
{
    /// <summary>Concurrent viewers reported by Helix at the sample instant.</summary>
    public required int ViewerCount { get; init; }

    /// <summary>The Twitch stream id the sample belongs to (one live session).</summary>
    public required string StreamId { get; init; }
}
