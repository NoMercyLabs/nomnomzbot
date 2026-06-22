// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Infrastructure.Services.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the per-channel analytics reads (analytics.md §3.3): the daily series returns the in-range rows ordered;
/// the summary sums the window and computes deltas vs the preceding equal window; top-viewers ranks by the chosen
/// metric and excludes analytics-opted-out viewers; an inverted range is rejected.
/// </summary>
public sealed class ChannelAnalyticsServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000001001");
    private static readonly Guid ViewerA = Guid.Parse("0192a000-0000-7000-8000-00000000aaaa");
    private static readonly Guid ViewerB = Guid.Parse("0192a000-0000-7000-8000-00000000bbbb");

    private static (ChannelAnalyticsService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new ChannelAnalyticsService(db), db);
    }

    private static async Task SeedDailyAsync(AuthDbContext db, DateOnly date, long messages)
    {
        db.ChannelAnalyticsDailies.Add(
            new ChannelAnalyticsDaily
            {
                BroadcasterId = Channel,
                ActivityDate = date,
                TotalMessages = messages,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetDailySeries_returns_the_in_range_rows_ordered()
    {
        (ChannelAnalyticsService sut, AuthDbContext db) = Build();
        await SeedDailyAsync(db, new DateOnly(2026, 6, 22), 30);
        await SeedDailyAsync(db, new DateOnly(2026, 6, 20), 10);
        await SeedDailyAsync(db, new DateOnly(2026, 6, 21), 20);

        IReadOnlyList<ChannelAnalyticsDailyDto> series = (
            await sut.GetDailySeriesAsync(
                Channel,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 22)
            )
        ).Value;

        series.Select(s => s.TotalMessages).Should().Equal(10, 20, 30);
    }

    [Fact]
    public async Task GetSummary_sums_the_window_and_computes_deltas_vs_the_preceding_window()
    {
        (ChannelAnalyticsService sut, AuthDbContext db) = Build();
        // Current window 6/20–6/22 = 60; preceding equal window 6/17–6/19 = 10.
        await SeedDailyAsync(db, new DateOnly(2026, 6, 20), 10);
        await SeedDailyAsync(db, new DateOnly(2026, 6, 21), 20);
        await SeedDailyAsync(db, new DateOnly(2026, 6, 22), 30);
        await SeedDailyAsync(db, new DateOnly(2026, 6, 18), 10);

        ChannelAnalyticsSummaryDto summary = (
            await sut.GetSummaryAsync(Channel, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 22))
        ).Value;

        summary.TotalMessages.Should().Be(60);
        summary.Deltas.MessagesPct.Should().Be(500); // (60-10)/10*100
    }

    [Fact]
    public async Task GetTopViewers_ranks_by_metric_and_excludes_opted_out_viewers()
    {
        (ChannelAnalyticsService sut, AuthDbContext db) = Build();
        db.ViewerEngagementDailies.AddRange(
            new ViewerEngagementDaily
            {
                BroadcasterId = Channel,
                ViewerUserId = ViewerA,
                ActivityDate = new DateOnly(2026, 6, 21),
                MessageCount = 50,
            },
            new ViewerEngagementDaily
            {
                BroadcasterId = Channel,
                ViewerUserId = ViewerB,
                ActivityDate = new DateOnly(2026, 6, 21),
                MessageCount = 100,
            }
        );
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                BroadcasterId = Channel,
                ViewerUserId = ViewerB,
                ViewerTwitchUserId = "b",
                IsAnalyticsOptedOut = true,
            }
        );
        await db.SaveChangesAsync();

        IReadOnlyList<TopViewerDto> top = (
            await sut.GetTopViewersAsync(
                Channel,
                TopViewerMetric.Messages,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 22),
                10
            )
        ).Value;

        top.Should().ContainSingle();
        top[0].ViewerUserId.Should().Be(ViewerA);
        top[0].MetricValue.Should().Be(50);
    }

    [Fact]
    public async Task An_inverted_range_is_rejected()
    {
        (ChannelAnalyticsService sut, _) = Build();

        Result<IReadOnlyList<ChannelAnalyticsDailyDto>> result = await sut.GetDailySeriesAsync(
            Channel,
            new DateOnly(2026, 6, 22),
            new DateOnly(2026, 6, 20)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
