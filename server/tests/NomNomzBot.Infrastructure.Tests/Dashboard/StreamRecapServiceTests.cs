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
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Domain.Stream.Entities;
using NomNomzBot.Infrastructure.Dashboard.Stats;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using StreamEntity = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Infrastructure.Tests.Dashboard;

/// <summary>Every stream's recap is kept: one row per stream, read back newest first, same data live or rebuilt.</summary>
public sealed class StreamRecapServiceTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000a1");
    private static readonly DateTimeOffset Started = new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ended = new(2026, 10, 16, 21, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 21, 8, 46, TimeSpan.Zero);

    private static StreamEntity NewStream(
        string id,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt
    ) =>
        new()
        {
            Id = id,
            ChannelId = Broadcaster,
            StartedAt = startedAt,
            EndedAt = endedAt,
            PeakViewers = 87,
            Title = "Celeste night",
            GameName = "Celeste",
        };

    private static StreamRecapService NewService(AuthDbContext db)
    {
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(Now);
        IStreamMetric[] metrics = [new DurationMetric(), new PeakViewersMetric()];
        StreamStatsService stats = new(db, clock, metrics);
        return new StreamRecapService(db, stats, clock);
    }

    [Fact]
    public async Task A_recap_is_stored_with_its_window_and_the_widget_data()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(NewStream("s-1", Started, null));
        await db.SaveChangesAsync();

        Result<StreamRecapDto> built = await NewService(db).BuildAsync(Broadcaster, null, null);

        built.IsSuccess.Should().BeTrue();
        StreamRecap row = db.StreamRecaps.Single();
        row.StreamId.Should().Be("s-1");
        row.ChannelId.Should().Be(Broadcaster);
        row.FromAt.Should().Be(Started.UtcDateTime);
        row.ToAt.Should()
            .Be(Now.UtcDateTime, "a live stream is counted up to the moment of the build");
        built.Value.Stats.PeakViewers.Should().Be(87);
        row.DataJson.Should().Contain("\"peakViewers\":87");
    }

    [Fact]
    public async Task A_second_raid_in_the_same_stream_updates_that_streams_row()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        StreamEntity stream = NewStream("s-1", Started, null);
        db.Streams.Add(stream);
        await db.SaveChangesAsync();
        StreamRecapService service = NewService(db);
        await service.BuildAsync(Broadcaster, null, null);

        stream.PeakViewers = 120;
        await db.SaveChangesAsync();
        await service.BuildAsync(Broadcaster, null, null);

        StreamRecap row = db.StreamRecaps.Single();
        row.DataJson.Should().Contain("\"peakViewers\":120").And.NotContain("\"peakViewers\":87");
    }

    [Fact]
    public async Task The_list_is_newest_stream_first_and_paged()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.AddRange(
            NewStream("old", Started.AddDays(-2), Ended.AddDays(-2)),
            NewStream("new", Started, Ended),
            NewStream("mid", Started.AddDays(-1), Ended.AddDays(-1))
        );
        await db.SaveChangesAsync();
        StreamRecapService service = NewService(db);
        // Built in an order that differs from the stream order: the list follows the stream start.
        await service.BuildAsync(Broadcaster, "mid", null);
        await service.BuildAsync(Broadcaster, "new", null);
        await service.BuildAsync(Broadcaster, "old", null);

        Result<PagedList<StreamRecapDto>> page1 = await service.ListAsync(Broadcaster, new(1, 2));
        Result<PagedList<StreamRecapDto>> page2 = await service.ListAsync(Broadcaster, new(2, 2));

        page1.Value.Items.Select(r => r.StreamId).Should().Equal("new", "mid");
        page1.Value.TotalCount.Should().Be(3);
        page2.Value.Items.Select(r => r.StreamId).Should().Equal("old");
    }

    [Fact]
    public async Task A_past_stream_built_afterwards_has_the_same_data_as_the_live_path()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(NewStream("s-1", Started, Ended));
        await db.SaveChangesAsync();
        StreamRecapService service = NewService(db);

        Result<StreamRecapDto> live = await service.BuildAsync(Broadcaster, null, null);
        string liveJson = db.StreamRecaps.Single().DataJson;
        Result<StreamRecapDto> backfill = await service.BuildAsync(Broadcaster, "s-1", null);

        backfill.Value.Stats.Should().BeEquivalentTo(live.Value.Stats);
        db.StreamRecaps.Single().DataJson.Should().Be(liveJson);
        backfill.Value.ToAt.Should().Be(Ended, "an ended stream is counted to its end");
        Result<StreamRecapDto> read = await service.GetAsync(Broadcaster, "s-1");
        read.Value.Stats.Should().BeEquivalentTo(live.Value.Stats);
    }
}
