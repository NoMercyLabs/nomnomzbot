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

/// <summary>One stream in the channel's history list, newest first — the per-stream entry point.</summary>
public sealed record StreamListItemDto(
    string StreamId,
    string? Title,
    string? GameName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    long? DurationSeconds,
    int? PeakViewers
);

/// <summary>
/// One stream's per-stream aggregates — the "stream by stream, not all-time" analytics view. Counts are
/// window-folded from the raw activity between <see cref="StartedAt"/> and <see cref="EndedAt"/> (or now,
/// while the stream is still live); <see cref="PeakViewers"/> is the fold the status poller stamps.
/// </summary>
public sealed record StreamAnalyticsDto(
    string StreamId,
    string? Title,
    string? GameName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    long? DurationSeconds,
    int? PeakViewers,
    long TotalMessages,
    int UniqueChatters,
    int NewFollowers,
    int NewSubscribers,
    int CheersCount,
    long CommandsRun,
    long RedemptionsCount
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

/// <summary>One viewer's aggregate profile for a channel (analytics.md §4, M.1).</summary>
public sealed record ViewerProfileDto(
    Guid ViewerUserId,
    string ViewerTwitchUserId,
    string? DisplayName,
    DateTime? FirstSeenAt,
    DateTime? LastSeenAt,
    long TotalWatchSeconds,
    long TotalMessages,
    long TotalCommandsUsed,
    long TotalRedemptions,
    long TotalSongRequests,
    bool IsFollower,
    bool IsSubscriber,
    string? SubTier,
    bool IsAnalyticsOptedOut
);

/// <summary>A row in the ranked/filtered viewer list (analytics.md §4, M.1).</summary>
public sealed record ViewerProfileListItemDto(
    Guid ViewerUserId,
    string? DisplayName,
    long TotalWatchSeconds,
    long TotalMessages,
    DateTime? LastSeenAt
);

/// <summary>One viewer's daily engagement roll-up (analytics.md §4, M.7).</summary>
public sealed record ViewerEngagementDailyDto(
    DateOnly ActivityDate,
    long WatchSeconds,
    int MessageCount,
    int CommandCount,
    int RedemptionCount,
    int SongRequestCount,
    long CurrencyEarned,
    long CurrencySpent,
    int GamesPlayed
);

/// <summary>One viewer's attendance streak (analytics.md §4, M.3).</summary>
public sealed record WatchStreakDto(int CurrentStreak, int MaxStreak, DateOnly LastSeenDate);

/// <summary>How the viewer list is sorted (analytics.md §4).</summary>
public enum ViewerProfileSort
{
    Watch,
    Messages,
    Commands,
    Redemptions,
    LastSeen,
}

/// <summary>Filter/sort over the viewer list (analytics.md §4).</summary>
public sealed record ViewerProfileQuery(
    string? Search = null,
    ViewerProfileSort Sort = ViewerProfileSort.LastSeen,
    bool? FollowersOnly = null,
    bool? SubscribersOnly = null
);

/// <summary>Body for the viewer analytics opt-out toggle (analytics.md §5).</summary>
public sealed record SetAnalyticsOptOutRequest(bool OptedOut);

/// <summary>SaaS-only cross-tenant platform stats (analytics.md §4) — no per-tenant identity crosses the boundary.</summary>
public sealed record PlatformAnalyticsDto(
    int ActiveChannels,
    int DailyActiveChannels,
    long TotalEventsProcessed,
    long TotalMessages,
    long TotalRedemptions,
    long TotalCommandsRun
);
