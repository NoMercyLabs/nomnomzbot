// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

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

    private static (ChannelAnalyticsService Sut, AuthDbContext Db) Build(TimeProvider? clock = null)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new ChannelAnalyticsService(db, clock ?? TimeProvider.System), db);
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

    // ── Per-stream views ("stream by stream, not all-time") ──────────────────

    private static readonly DateTimeOffset StreamStart = new(2026, 7, 15, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StreamEnd = new(2026, 7, 15, 21, 0, 0, TimeSpan.Zero);

    private static NomNomzBot.Domain.Stream.Entities.Stream StreamRow(
        string id,
        DateTimeOffset? started,
        DateTimeOffset? ended,
        int? peak = null
    ) =>
        new()
        {
            Id = id,
            ChannelId = Channel,
            Title = $"stream {id}",
            GameName = "Just Chatting",
            StartedAt = started,
            EndedAt = ended,
            PeakViewers = peak,
        };

    [Fact]
    public async Task ListStreams_returns_newest_first_with_duration_and_peak()
    {
        (ChannelAnalyticsService sut, AuthDbContext db) = Build();
        db.Streams.AddRange(
            StreamRow("s-old", StreamStart.AddDays(-2), StreamStart.AddDays(-2).AddHours(1), 5),
            StreamRow("s-new", StreamStart, StreamEnd, peak: 42),
            StreamRow("s-live", StreamStart.AddDays(1), ended: null, peak: 12)
        );
        await db.SaveChangesAsync();

        PagedList<StreamListItemDto> page = (
            await sut.ListStreamsAsync(Channel, new PaginationParams(1, 25, null, null))
        ).Value;

        page.TotalCount.Should().Be(3);
        page.Items.Select(s => s.StreamId).Should().Equal("s-live", "s-new", "s-old");
        StreamListItemDto ended = page.Items.Single(s => s.StreamId == "s-new");
        ended.DurationSeconds.Should().Be(3 * 3600);
        ended.PeakViewers.Should().Be(42);
        page.Items.Single(s => s.StreamId == "s-live")
            .DurationSeconds.Should()
            .BeNull("a live stream has no duration yet");
    }

    [Fact]
    public async Task GetStream_folds_only_the_activity_inside_the_streams_window()
    {
        (ChannelAnalyticsService sut, AuthDbContext db) = Build();
        db.Streams.Add(StreamRow("s-1", StreamStart, StreamEnd, peak: 42));

        // Chat: two in-window lines from the same viewer + one from another; one line BEFORE the stream.
        db.ChatMessages.AddRange(
            ChatLine("m-1", "viewer-a", StreamStart.AddMinutes(5)),
            ChatLine("m-2", "viewer-a", StreamStart.AddMinutes(10)),
            ChatLine("m-3", "viewer-b", StreamStart.AddMinutes(15)),
            ChatLine("m-out", "viewer-a", StreamStart.AddMinutes(-30))
        );

        // Events: in-window follow/sub/cheer/redemption + an out-of-window follow.
        db.ChannelEvents.AddRange(
            EventRow("e-1", "channel.follow", StreamStart.AddMinutes(6)),
            EventRow("e-2", "channel.subscribe", StreamStart.AddMinutes(7)),
            EventRow("e-3", "channel.cheer", StreamStart.AddMinutes(8)),
            EventRow(
                "e-4",
                "channel.channel_points_custom_reward_redemption.add",
                StreamStart.AddMinutes(9)
            ),
            EventRow("e-out", "channel.follow", StreamEnd.AddMinutes(30))
        );

        // Commands: one successful in-window, one failed in-window, one successful after the end.
        db.CommandUsages.AddRange(
            UsageRow(1, successful: true, StreamStart.AddMinutes(20)),
            UsageRow(2, successful: false, StreamStart.AddMinutes(21)),
            UsageRow(3, successful: true, StreamEnd.AddMinutes(10))
        );
        await db.SaveChangesAsync();

        StreamAnalyticsDto stats = (await sut.GetStreamAsync(Channel, "s-1")).Value;

        stats.TotalMessages.Should().Be(3, "the pre-stream line is outside the window");
        stats.UniqueChatters.Should().Be(2);
        stats.NewFollowers.Should().Be(1, "the post-stream follow is outside the window");
        stats.NewSubscribers.Should().Be(1);
        stats.CheersCount.Should().Be(1);
        stats.RedemptionsCount.Should().Be(1);
        stats.CommandsRun.Should().Be(1, "only successful in-window commands count");
        stats.PeakViewers.Should().Be(42);
        stats.DurationSeconds.Should().Be(3 * 3600);
    }

    [Fact]
    public async Task GetStream_on_a_live_stream_folds_up_to_now()
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(StreamStart.AddHours(1));
        (ChannelAnalyticsService sut, AuthDbContext db) = Build(clock);
        db.Streams.Add(StreamRow("s-live", StreamStart, ended: null));
        db.ChatMessages.Add(ChatLine("m-1", "viewer-a", StreamStart.AddMinutes(30)));
        await db.SaveChangesAsync();

        StreamAnalyticsDto stats = (await sut.GetStreamAsync(Channel, "s-live")).Value;

        stats.TotalMessages.Should().Be(1);
        stats.EndedAt.Should().BeNull();
        stats.DurationSeconds.Should().BeNull();
    }

    [Fact]
    public async Task GetStream_for_an_unknown_or_foreign_stream_is_not_found()
    {
        (ChannelAnalyticsService sut, AuthDbContext db) = Build();
        db.Streams.Add(
            new NomNomzBot.Domain.Stream.Entities.Stream
            {
                Id = "s-foreign",
                ChannelId = Guid.NewGuid(),
                StartedAt = StreamStart,
            }
        );
        await db.SaveChangesAsync();

        (await sut.GetStreamAsync(Channel, "nope")).ErrorCode.Should().Be("NOT_FOUND");
        (await sut.GetStreamAsync(Channel, "s-foreign"))
            .ErrorCode.Should()
            .Be("NOT_FOUND", "another channel's stream must never resolve");
    }

    private static NomNomzBot.Domain.Chat.Entities.ChatMessage ChatLine(
        string id,
        string userId,
        DateTimeOffset at
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = Channel,
            UserId = userId,
            Username = userId,
            DisplayName = userId,
            UserType = "viewer",
            Message = "hi",
            CreatedAt = at.UtcDateTime,
        };

    private static NomNomzBot.Domain.Identity.Entities.ChannelEvent EventRow(
        string id,
        string type,
        DateTimeOffset at
    ) =>
        new()
        {
            Id = id,
            ChannelId = Channel,
            Type = type,
            CreatedAt = at.UtcDateTime,
        };

    private static NomNomzBot.Domain.Commands.Entities.CommandUsage UsageRow(
        long id,
        bool successful,
        DateTimeOffset at
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = Channel,
            CommandNameSnapshot = "!hello",
            ViewerProfileId = Guid.CreateVersion7(),
            ViewerUserId = Guid.CreateVersion7(),
            WasSuccessful = successful,
            CreatedAt = at.UtcDateTime,
        };
}
