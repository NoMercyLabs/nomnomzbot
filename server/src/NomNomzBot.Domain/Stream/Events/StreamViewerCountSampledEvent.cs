// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
