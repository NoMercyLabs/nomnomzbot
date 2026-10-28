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

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Domain.Stream.Entities;
using NomNomzBot.Infrastructure.Dashboard.Stats;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using StreamEntity = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Infrastructure.Tests.Dashboard;

/// <summary>Every past stream with live time gets its recap; the stored recap carries the stream's real length.</summary>
public sealed class StreamRecapBackfillSeederTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000b1");
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset First = new(2026, 10, 13, 11, 58, 19, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = new(2026, 10, 15, 11, 59, 43, TimeSpan.Zero);

    private static StreamEntity Row(
        string id,
        DateTimeOffset start,
        DateTimeOffset? end,
        int? peak = null
    ) =>
        new()
        {
            Id = id,
            ChannelId = Broadcaster,
            StartedAt = start,
            EndedAt = end,
            PeakViewers = peak,
            Title = "title " + id,
        };

    private static StreamRecapBackfillSeeder Sut(AuthDbContext db)
    {
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(Now);
        IStreamMetric[] metrics = [new DurationMetric(), new PeakViewersMetric(db)];
        StreamRecapService recaps = new(db, new StreamStatsService(db, clock, metrics), clock);
        return new StreamRecapBackfillSeeder(
            db,
            recaps,
            NullLogger<StreamRecapBackfillSeeder>.Instance
        );
    }

    private static long? DurationOf(StreamRecap row) =>
        JsonSerializer.Deserialize<StreamStatsDto>(row.DataJson)!.Duration;

    [Fact]
    public async Task A_past_stream_without_a_recap_gets_one_with_its_real_length()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(Row("old", First, First.AddHours(8), 30));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        StreamRecap kept = await db.StreamRecaps.AsNoTracking().SingleAsync();
        kept.StreamId.Should().Be("old");
        kept.FromAt.Should().Be(First.UtcDateTime);
        kept.ToAt.Should().Be(First.AddHours(8).UtcDateTime);
        DurationOf(kept).Should().Be(8 * 3600);
    }

    [Fact]
    public async Task A_recap_built_while_live_is_rebuilt_to_the_end_of_the_finished_stream()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(Row("old", First, First.AddHours(8)));
        db.StreamRecaps.Add(
            new StreamRecap
            {
                ChannelId = Broadcaster,
                StreamId = "old",
                FromAt = First.UtcDateTime,
                ToAt = First.AddHours(5).UtcDateTime,
                DataJson = "{}",
            }
        );
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        StreamRecap kept = await db.StreamRecaps.AsNoTracking().SingleAsync();
        kept.ToAt.Should().Be(First.AddHours(8).UtcDateTime);
        DurationOf(kept).Should().Be(8 * 3600);
    }

    [Fact]
    public async Task A_recap_built_before_the_metrics_were_revised_is_rebuilt_even_when_its_end_matches()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(Row("old", First, First.AddHours(8), 30));
        StreamRecap stale = new()
        {
            ChannelId = Broadcaster,
            StreamId = "old",
            FromAt = First.UtcDateTime,
            ToAt = First.AddHours(8).UtcDateTime,
            DataJson = "{}",
        };
        db.StreamRecaps.Add(stale);
        await db.SaveChangesAsync();
        db.Entry(stale).Property(r => r.UpdatedAt).CurrentValue = new DateTime(
            2026,
            10,
            18,
            0,
            0,
            0,
            DateTimeKind.Utc
        );
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        StreamRecap kept = await db.StreamRecaps.AsNoTracking().SingleAsync();
        DurationOf(kept).Should().Be(8 * 3600, "the empty snapshot was replaced by a fresh build");
    }

    [Fact]
    public async Task A_stream_that_never_ran_and_a_duplicate_row_of_one_start_get_no_extra_recap()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(Row("never-ran", First, First));
        db.Streams.Add(Row("twin-a", Second, Second.AddHours(6)));
        db.Streams.Add(Row("twin-b", Second, Second.AddHours(6), 21));
        db.Streams.Add(Row("live", Second.AddDays(2), null));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        List<string> built = await db.StreamRecaps.Select(r => r.StreamId).ToListAsync();
        built
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("twin-b", "the twin that kept the peak wins");
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Streams.Add(Row("old", First, First.AddHours(8)));
        await db.SaveChangesAsync();
        await Sut(db).SeedAsync();
        DateTime stamp = (await db.StreamRecaps.AsNoTracking().SingleAsync()).UpdatedAt;

        await Sut(db).SeedAsync();

        StreamRecap kept = await db.StreamRecaps.AsNoTracking().SingleAsync();
        kept.UpdatedAt.Should()
            .Be(stamp, "a recap that already ends where its stream ends is left alone");
    }
}
