// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Analytics;

/// <summary>One day of the per-channel aggregate (analytics.md §4, schema M.8) — the chart/time-series row.</summary>
public sealed record ChannelAnalyticsDailyDto(
    DateOnly ActivityDate,
    int UniqueChatters,
    long TotalMessages,
    long TotalWatchSeconds,
    int NewFollowers,
    int NewSubscribers,
    long BitsCheered,
    long CommandsRun,
    long RedemptionsCount,
    int SongRequests,
    long CurrencyEarnedTotal,
    long CurrencySpentTotal,
    int GamesPlayed,
    int? PeakViewers
);

/// <summary>% change of each headline metric vs the preceding equal-length window (0 when the prior window is 0).</summary>
public sealed record ChannelAnalyticsDeltasDto(
    double MessagesPct,
    double FollowersPct,
    double SubscribersPct,
    double BitsPct,
    double CommandsPct,
    double RedemptionsPct
);

/// <summary>Headline totals over a range + deltas vs the preceding window + peak viewers (analytics.md §4).</summary>
public sealed record ChannelAnalyticsSummaryDto(
    DateOnly From,
    DateOnly To,
    long TotalMessages,
    int NewFollowers,
    int NewSubscribers,
    long BitsCheered,
    long CommandsRun,
    long RedemptionsCount,
    int SongRequests,
    long CurrencyEarnedTotal,
    long CurrencySpentTotal,
    int? PeakViewers,
    ChannelAnalyticsDeltasDto Deltas
);

/// <summary>A channel's top viewer over a range by the chosen metric (analytics.md §4) — not the economy board.</summary>
public sealed record TopViewerDto(Guid ViewerUserId, string? DisplayName, long MetricValue);

/// <summary>The metric a top-viewers query ranks by (analytics.md §4).</summary>
public enum TopViewerMetric
{
    WatchSeconds,
    Messages,
    Commands,
    Redemptions,
    CurrencyEarned,
}
