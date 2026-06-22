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
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Services.Analytics;

/// <summary>Per-viewer analytics reads + opt-out (analytics.md §3.2) over the projected profile / engagement.</summary>
public sealed class ViewerAnalyticsService(IApplicationDbContext db) : IViewerAnalyticsService
{
    public async Task<Result<ViewerProfileDto>> GetProfileAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        ViewerProfile? profile = await db.ViewerProfiles.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.ViewerUserId == viewerUserId
                && p.DeletedAt == null,
            ct
        );
        return profile is null
            ? Result.Failure<ViewerProfileDto>("Viewer profile not found.", "NOT_FOUND")
            : Result.Success(ToDto(profile));
    }

    public async Task<Result<PagedList<ViewerProfileListItemDto>>> ListProfilesAsync(
        Guid broadcasterId,
        ViewerProfileQuery query,
        PaginationParams paging,
        CancellationToken ct = default
    )
    {
        IQueryable<ViewerProfile> q = db.ViewerProfiles.Where(p =>
            p.BroadcasterId == broadcasterId && p.DeletedAt == null
        );

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.ToLower();
            q = q.Where(p =>
                p.DisplayNameSnapshot != null && p.DisplayNameSnapshot.ToLower().StartsWith(search)
            );
        }
        if (query.FollowersOnly == true)
            q = q.Where(p => p.IsFollower);
        if (query.SubscribersOnly == true)
            q = q.Where(p => p.IsSubscriber);

        q = query.Sort switch
        {
            ViewerProfileSort.Watch => q.OrderByDescending(p => p.TotalWatchSeconds),
            ViewerProfileSort.Messages => q.OrderByDescending(p => p.TotalMessages),
            ViewerProfileSort.Commands => q.OrderByDescending(p => p.TotalCommandsUsed),
            ViewerProfileSort.Redemptions => q.OrderByDescending(p => p.TotalRedemptions),
            _ => q.OrderByDescending(p => p.LastSeenAt),
        };

        int total = await q.CountAsync(ct);
        List<ViewerProfileListItemDto> items = await q.Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .Select(p => new ViewerProfileListItemDto(
                p.ViewerUserId,
                p.DisplayNameSnapshot,
                p.TotalWatchSeconds,
                p.TotalMessages,
                p.LastSeenAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<ViewerProfileListItemDto>(items, paging.Page, paging.PageSize, total)
        );
    }

    public async Task<Result<IReadOnlyList<ViewerEngagementDailyDto>>> GetEngagementSeriesAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        if (from > to || to.DayNumber - from.DayNumber + 1 > 366)
            return Result.Failure<IReadOnlyList<ViewerEngagementDailyDto>>(
                "from must be on or before to and the range must not exceed 366 days.",
                "VALIDATION_FAILED"
            );

        List<ViewerEngagementDailyDto> series = await db
            .ViewerEngagementDailies.Where(e =>
                e.BroadcasterId == broadcasterId
                && e.ViewerUserId == viewerUserId
                && e.ActivityDate >= from
                && e.ActivityDate <= to
            )
            .OrderBy(e => e.ActivityDate)
            .Select(e => new ViewerEngagementDailyDto(
                e.ActivityDate,
                e.WatchSeconds,
                e.MessageCount,
                e.CommandCount,
                e.RedemptionCount,
                e.SongRequestCount,
                e.CurrencyEarned,
                e.CurrencySpent,
                e.GamesPlayed
            ))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ViewerEngagementDailyDto>>(series);
    }

    public async Task<Result<WatchStreakDto>> GetStreakAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        // M.3 WatchStreak keys the viewer by Twitch id (string); resolve the internal viewer Guid to it.
        string? twitchUserId = await db
            .Users.Where(u => u.Id == viewerUserId)
            .Select(u => u.TwitchUserId)
            .FirstOrDefaultAsync(ct);
        if (twitchUserId is null)
            return Result.Failure<WatchStreakDto>("Viewer not found.", "NOT_FOUND");

        WatchStreak? streak = await db.WatchStreaks.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.UserId == twitchUserId,
            ct
        );
        return streak is null
            ? Result.Failure<WatchStreakDto>("Viewer has no streak yet.", "NOT_FOUND")
            : Result.Success(
                new WatchStreakDto(streak.CurrentStreak, streak.MaxStreak, streak.LastSeenDate)
            );
    }

    public async Task<Result> SetAnalyticsOptOutAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        bool optedOut,
        CancellationToken ct = default
    )
    {
        ViewerProfile? profile = await db.ViewerProfiles.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.ViewerUserId == viewerUserId
                && p.DeletedAt == null,
            ct
        );
        if (profile is null)
            return Result.Failure("Viewer profile not found.", "NOT_FOUND");

        profile.IsAnalyticsOptedOut = optedOut;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static ViewerProfileDto ToDto(ViewerProfile p) =>
        new(
            p.ViewerUserId,
            p.ViewerTwitchUserId,
            p.DisplayNameSnapshot,
            p.FirstSeenAt,
            p.LastSeenAt,
            p.TotalWatchSeconds,
            p.TotalMessages,
            p.TotalCommandsUsed,
            p.TotalRedemptions,
            p.TotalSongRequests,
            p.IsFollower,
            p.IsSubscriber,
            p.SubTier,
            p.IsAnalyticsOptedOut
        );
}
