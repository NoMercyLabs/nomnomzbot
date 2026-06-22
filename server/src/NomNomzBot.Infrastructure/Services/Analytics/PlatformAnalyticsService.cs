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
/// SaaS-only cross-tenant platform stats (analytics.md §3.4). Self-gates on the deployment profile: a deployment
/// with no platform IAM principals is self-host, where there is no cross-tenant view, so it returns
/// <c>FEATURE_DISABLED</c>. On SaaS it folds the no-PII channel aggregate (M.8) across every tenant.
/// </summary>
public sealed class PlatformAnalyticsService(IApplicationDbContext db) : IPlatformAnalyticsService
{
    private const int MaxRangeDays = 366;

    public async Task<Result<PlatformAnalyticsDto>> GetPlatformStatsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        if (from > to || to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            return Result.Failure<PlatformAnalyticsDto>(
                "from must be on or before to and the range must not exceed 366 days.",
                "VALIDATION_FAILED"
            );

        // SaaS iff platform IAM is provisioned; self-host has no cross-tenant plane.
        bool isSaas = await db.IamPrincipals.AnyAsync(ct);
        if (!isSaas)
            return Result.Failure<PlatformAnalyticsDto>(
                "Platform analytics is available on the hosted platform only.",
                "FEATURE_DISABLED"
            );

        List<ChannelAnalyticsDaily> rows = await db
            .ChannelAnalyticsDailies.Where(r => r.ActivityDate >= from && r.ActivityDate <= to)
            .ToListAsync(ct);

        long totalMessages = rows.Sum(r => r.TotalMessages);
        long totalRedemptions = rows.Sum(r => r.RedemptionsCount);
        long totalCommands = rows.Sum(r => r.CommandsRun);
        long totalEvents =
            totalMessages
            + totalRedemptions
            + totalCommands
            + rows.Sum(r => (long)r.NewFollowers)
            + rows.Sum(r => (long)r.NewSubscribers);

        return Result.Success(
            new PlatformAnalyticsDto(
                rows.Select(r => r.BroadcasterId).Distinct().Count(),
                rows.Select(r => new { r.BroadcasterId, r.ActivityDate }).Distinct().Count(),
                totalEvents,
                totalMessages,
                totalRedemptions,
                totalCommands
            )
        );
    }
}
