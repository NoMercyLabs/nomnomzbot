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

/// <summary>
/// Per-channel analytics reads (analytics.md §3.3) over the projected M.8 / M.7 roll-ups, plus the
/// per-stream views ("stream by stream, not all-time") window-folded from the raw activity tables.
/// </summary>
public sealed class ChannelAnalyticsService(IApplicationDbContext db, TimeProvider clock)
    : IChannelAnalyticsService
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

    public async Task<Result<PagedList<StreamListItemDto>>> ListStreamsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<Domain.Stream.Entities.Stream> query = db
            .Streams.Where(s => s.ChannelId == broadcasterId && s.StartedAt != null)
            .OrderByDescending(s => s.StartedAt);

        int total = await query.CountAsync(ct);
        List<StreamListItemDto> items = (
            await query
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToListAsync(ct)
        )
            .Select(s => new StreamListItemDto(
                s.Id,
                s.Title,
                s.GameName,
                s.StartedAt,
                s.EndedAt,
                DurationSeconds(s),
                s.PeakViewers
            ))
            .ToList();

        return Result.Success(
            new PagedList<StreamListItemDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<StreamAnalyticsDto>> GetStreamAsync(
        Guid broadcasterId,
        string streamId,
        CancellationToken ct = default
    )
    {
        Domain.Stream.Entities.Stream? stream = await db.Streams.FirstOrDefaultAsync(
            s => s.ChannelId == broadcasterId && s.Id == streamId,
            ct
        );
        if (stream is null || stream.StartedAt is null)
            return Errors.NotFound<StreamAnalyticsDto>("Stream", streamId);

        // The stream's activity window: start → end, or "now" while it is still live.
        DateTime windowStart = stream.StartedAt.Value.UtcDateTime;
        DateTime windowEnd = (stream.EndedAt ?? clock.GetUtcNow()).UtcDateTime;

        long totalMessages = await db.ChatMessages.LongCountAsync(
            m =>
                m.BroadcasterId == broadcasterId
                && m.CreatedAt >= windowStart
                && m.CreatedAt < windowEnd,
            ct
        );
        int uniqueChatters = await db
            .ChatMessages.Where(m =>
                m.BroadcasterId == broadcasterId
                && m.CreatedAt >= windowStart
                && m.CreatedAt < windowEnd
            )
            .Select(m => m.UserId)
            .Distinct()
            .CountAsync(ct);

        // The activity-feed rows the alert handlers + projection log — counted by their canonical keys.
        Dictionary<string, int> eventCounts = await db
            .ChannelEvents.Where(e =>
                e.ChannelId == broadcasterId
                && e.CreatedAt >= windowStart
                && e.CreatedAt < windowEnd
            )
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Type, g => g.Count, ct);

        long commandsRun = await db.CommandUsages.LongCountAsync(
            u =>
                u.BroadcasterId == broadcasterId
                && u.WasSuccessful
                && u.CreatedAt >= windowStart
                && u.CreatedAt < windowEnd,
            ct
        );

        StreamAnalyticsDto dto = new(
            stream.Id,
            stream.Title,
            stream.GameName,
            stream.StartedAt,
            stream.EndedAt,
            DurationSeconds(stream),
            stream.PeakViewers,
            totalMessages,
            uniqueChatters,
            eventCounts.GetValueOrDefault("channel.follow"),
            eventCounts.GetValueOrDefault("channel.subscribe"),
            eventCounts.GetValueOrDefault("channel.cheer"),
            commandsRun,
            eventCounts.GetValueOrDefault("channel.channel_points_custom_reward_redemption.add")
        );
        return Result.Success(dto);
    }

    /// <summary>Whole seconds from start to end — null while the stream is live or never stamped.</summary>
    private static long? DurationSeconds(Domain.Stream.Entities.Stream s) =>
        s.StartedAt is { } started && s.EndedAt is { } ended
            ? (long)(ended - started).TotalSeconds
            : null;

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
