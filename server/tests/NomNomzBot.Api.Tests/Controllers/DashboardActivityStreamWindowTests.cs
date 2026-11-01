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

using FluentAssertions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Domain.Identity.Entities;
using StreamRow = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the stream-scoped activity window: the home panel shows every event of the current stream, and the
/// Events page shows this stream and the one before it. A stream's window opens when the Nth newest stream started,
/// so the count of events is bounded by the streams, never by a fixed row count.
/// </summary>
public sealed class DashboardActivityStreamWindowTests
{
    private static readonly DateTime Day = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

    private static StreamRow StreamAt(Guid channel, int startHour, int? endHour) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = channel,
            StartedAt = new DateTimeOffset(Day.AddHours(startHour)),
            EndedAt = endHour is null ? null : new DateTimeOffset(Day.AddHours(endHour.Value)),
        };

    private static ChannelEvent Row(Guid channel, int hour, string type = "channel.follow") =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = channel,
            Type = type,
            Data = "{}",
            CreatedAt = Day.AddHours(hour),
        };

    [Fact]
    public void One_stream_opens_the_window_at_the_newest_streams_start()
    {
        Guid channel = Guid.NewGuid();
        List<StreamRow> streams = [StreamAt(channel, 1, 3), StreamAt(channel, 10, null)];

        DateTime? since = DashboardController.StreamWindowStart(streams.AsQueryable(), channel, 1);

        since.Should().Be(Day.AddHours(10));
    }

    [Fact]
    public void Two_streams_open_the_window_at_the_start_of_the_stream_before()
    {
        Guid channel = Guid.NewGuid();
        List<StreamRow> streams =
        [
            StreamAt(channel, 1, 3),
            StreamAt(channel, 10, 12),
            StreamAt(channel, 20, null),
        ];

        DateTime? since = DashboardController.StreamWindowStart(streams.AsQueryable(), channel, 2);

        since.Should().Be(Day.AddHours(10));
    }

    [Fact]
    public void Fewer_streams_than_asked_open_the_window_at_the_oldest_stream()
    {
        Guid channel = Guid.NewGuid();
        List<StreamRow> streams = [StreamAt(channel, 5, 6)];

        DashboardController
            .StreamWindowStart(streams.AsQueryable(), channel, 2)
            .Should()
            .Be(Day.AddHours(5));
    }

    [Fact]
    public void A_channel_with_no_stream_has_no_window_start()
    {
        Guid channel = Guid.NewGuid();
        List<StreamRow> others = [StreamAt(Guid.NewGuid(), 5, 6)];

        DashboardController.StreamWindowStart(others.AsQueryable(), channel, 1).Should().BeNull();
    }

    [Fact]
    public void A_stream_that_never_recorded_a_start_is_skipped()
    {
        Guid channel = Guid.NewGuid();
        StreamRow noStart = StreamAt(channel, 8, null);
        noStart.StartedAt = null;
        List<StreamRow> streams = [StreamAt(channel, 2, 4), noStart];

        DashboardController
            .StreamWindowStart(streams.AsQueryable(), channel, 1)
            .Should()
            .Be(Day.AddHours(2));
    }

    [Fact]
    public void The_window_keeps_only_events_since_the_start_and_not_the_row_cap()
    {
        Guid channel = Guid.NewGuid();
        List<ChannelEvent> journal = [];
        for (int hour = 0; hour < 30; hour++)
            journal.Add(Row(channel, hour));

        List<ChannelEvent> window = DashboardController
            .RecentActivityWindow(journal.AsQueryable(), channel, null, 1000, Day.AddHours(10))
            .ToList();

        window.Should().HaveCount(20);
        window.Min(e => e.CreatedAt).Should().Be(Day.AddHours(10));
        window.Max(e => e.CreatedAt).Should().Be(Day.AddHours(29));
    }

    [Fact]
    public void Without_a_start_the_window_is_the_newest_rows_as_before()
    {
        Guid channel = Guid.NewGuid();
        List<ChannelEvent> journal = [];
        for (int hour = 0; hour < 30; hour++)
            journal.Add(Row(channel, hour));

        DashboardController
            .RecentActivityWindow(journal.AsQueryable(), channel, null, 5)
            .Should()
            .HaveCount(5);
    }
}
