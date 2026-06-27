// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Services.Analytics;

/// <summary>Per-channel analytics reads (analytics.md §3.3) over the projected M.8 / M.7 roll-ups.</summary>
public sealed class ChannelAnalyticsService(IApplicationDbContext db) : IChannelAnalyticsService
{
    private const int MaxRangeDays = 366;

    public async Task<Result<IReadOnlyList<ChannelAnalyticsDailyDto>>> GetDailySeriesAsync(
        Guid broadcasterId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        if (!IsValidRange(from, to))
            return Result.Failure<IReadOnlyList<ChannelAnalyticsDailyDto>>(
                "from must be on or before to and the range must not exceed 366 days.",
                "VALIDATION_FAILED"
            );

        List<ChannelAnalyticsDailyDto> series = await db
            .ChannelAnalyticsDailies.Where(r =>
                r.BroadcasterId == broadcasterId && r.ActivityDate >= from && r.ActivityDate <= to
            )
            .OrderBy(r => r.ActivityDate)
            .Select(r => new ChannelAnalyticsDailyDto(
                r.ActivityDate,
                r.UniqueChatters,
                r.TotalMessages,
                r.TotalWatchSeconds,
                r.NewFollowers,
                r.NewSubscribers,
                r.BitsCheered,
                r.CommandsRun,
                r.RedemptionsCount,
                r.SongRequests,
                r.CurrencyEarnedTotal,
                r.CurrencySpentTotal,
                r.GamesPlayed,
                r.PeakViewers
            ))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ChannelAnalyticsDailyDto>>(series);
    }

    public async Task<Result<ChannelAnalyticsSummaryDto>> GetSummaryAsync(
        Guid broadcasterId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        if (!IsValidRange(from, to))
            return Result.Failure<ChannelAnalyticsSummaryDto>(
                "from must be on or before to and the range must not exceed 366 days.",
                "VALIDATION_FAILED"
            );

        List<ChannelAnalyticsDaily> current = await RangeRowsAsync(broadcasterId, from, to, ct);

        int spanDays = to.DayNumber - from.DayNumber + 1;
        DateOnly prevTo = from.AddDays(-1);
        DateOnly prevFrom = prevTo.AddDays(-(spanDays - 1));
        List<ChannelAnalyticsDaily> previous = await RangeRowsAsync(
            broadcasterId,
            prevFrom,
            prevTo,
            ct
        );

        ChannelAnalyticsDeltasDto deltas = new(
            Pct(current.Sum(r => r.TotalMessages), previous.Sum(r => r.TotalMessages)),
            Pct(current.Sum(r => (long)r.NewFollowers), previous.Sum(r => (long)r.NewFollowers)),
            Pct(
                current.Sum(r => (long)r.NewSubscribers),
                previous.Sum(r => (long)r.NewSubscribers)
            ),
            Pct(current.Sum(r => r.BitsCheered), previous.Sum(r => r.BitsCheered)),
            Pct(current.Sum(r => r.CommandsRun), previous.Sum(r => r.CommandsRun)),
            Pct(current.Sum(r => r.RedemptionsCount), previous.Sum(r => r.RedemptionsCount))
        );

        ChannelAnalyticsSummaryDto summary = new(
            from,
            to,
            current.Sum(r => r.TotalMessages),
            current.Sum(r => r.NewFollowers),
            current.Sum(r => r.NewSubscribers),
            current.Sum(r => r.BitsCheered),
            current.Sum(r => r.CommandsRun),
            current.Sum(r => r.RedemptionsCount),
            current.Sum(r => r.SongRequests),
            current.Sum(r => r.CurrencyEarnedTotal),
            current.Sum(r => r.CurrencySpentTotal),
            current.Count == 0 ? null : current.Max(r => r.PeakViewers),
            deltas
        );
        return Result.Success(summary);
    }

    public async Task<Result<IReadOnlyList<TopViewerDto>>> GetTopViewersAsync(
        Guid broadcasterId,
        TopViewerMetric metric,
        DateOnly from,
        DateOnly to,
        int top,
        CancellationToken ct = default
    )
    {
        if (!IsValidRange(from, to) || top is < 1 or > 1000)
            return Result.Failure<IReadOnlyList<TopViewerDto>>(
                "Invalid range or top out of [1, 1000].",
                "VALIDATION_FAILED"
            );

        List<ViewerEngagementDaily> rows = await db
            .ViewerEngagementDailies.Where(e =>
                e.BroadcasterId == broadcasterId && e.ActivityDate >= from && e.ActivityDate <= to
            )
            .ToListAsync(ct);

        // Deduplicate before building the dictionary — a viewer can have >1 profile row when
        // the projection replayed after a schema migration (idempotency guard missed the upsert).
        Dictionary<Guid, ViewerProfile> profileByUser = (
            await db.ViewerProfiles.Where(p => p.BroadcasterId == broadcasterId).ToListAsync(ct)
        )
            .GroupBy(p => p.ViewerUserId)
            .ToDictionary(g => g.Key, g => g.First());

        List<TopViewerDto> ranked =
        [
            .. rows.Where(e =>
                    !(
                        profileByUser.TryGetValue(e.ViewerUserId, out ViewerProfile? p)
                        && p.IsAnalyticsOptedOut
                    )
                )
                .GroupBy(e => e.ViewerUserId)
                .Select(g => new TopViewerDto(
                    g.Key,
                    profileByUser.TryGetValue(g.Key, out ViewerProfile? p)
                        ? p.DisplayNameSnapshot
                        : null,
                    g.Sum(e => MetricValue(e, metric))
                ))
                .OrderByDescending(d => d.MetricValue)
                .Take(top),
        ];
        return Result.Success<IReadOnlyList<TopViewerDto>>(ranked);
    }

    private async Task<List<ChannelAnalyticsDaily>> RangeRowsAsync(
        Guid broadcasterId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct
    ) =>
        await db
            .ChannelAnalyticsDailies.Where(r =>
                r.BroadcasterId == broadcasterId && r.ActivityDate >= from && r.ActivityDate <= to
            )
            .ToListAsync(ct);

    private static long MetricValue(ViewerEngagementDaily e, TopViewerMetric metric) =>
        metric switch
        {
            TopViewerMetric.WatchSeconds => e.WatchSeconds,
            TopViewerMetric.Messages => e.MessageCount,
            TopViewerMetric.Commands => e.CommandCount,
            TopViewerMetric.Redemptions => e.RedemptionCount,
            TopViewerMetric.CurrencyEarned => e.CurrencyEarned,
            _ => 0,
        };

    private static double Pct(long current, long previous) =>
        previous == 0 ? 0 : Math.Round((current - previous) / (double)previous * 100, 2);

    private static bool IsValidRange(DateOnly from, DateOnly to) =>
        from <= to && to.DayNumber - from.DayNumber + 1 <= MaxRangeDays;
}
