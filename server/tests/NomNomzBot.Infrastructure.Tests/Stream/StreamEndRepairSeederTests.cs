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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// Stored Stream rows whose end equals the next stream's start (the old online handler's stamp) are moved to
/// the last proof the stream was live. Each test reads the rows back and checks the stored end and the
/// resulting duration.
/// </summary>
public sealed class StreamEndRepairSeederTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000f1");
    private static readonly Guid Owner = Guid.Parse("0192f000-0000-7000-8000-0000000000f9");
    private static readonly DateTimeOffset First = new(2026, 10, 11, 11, 58, 54, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = new(2026, 10, 13, 11, 58, 19, TimeSpan.Zero);

    private static Channel NewChannel() =>
        new()
        {
            Id = Broadcaster,
            OwnerUserId = Owner,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = "tw-f1",
            TwitchChannelId = "tw-f1",
            Name = "streamer_f1",
            NameNormalized = "streamer_f1",
            IsOnboarded = true,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };

    private static Domain.Stream.Entities.Stream Row(
        string id,
        DateTimeOffset start,
        DateTimeOffset? end
    ) =>
        new()
        {
            Id = id,
            ChannelId = Broadcaster,
            StartedAt = start,
            EndedAt = end,
        };

    private static ChatMessage Chat(string id, DateTimeOffset at) =>
        new()
        {
            Id = id,
            BroadcasterId = Broadcaster,
            UserId = "u1",
            Username = "u1",
            DisplayName = "U1",
            UserType = "viewer",
            Message = "hi",
            CreatedAt = at.UtcDateTime,
        };

    private static StreamEndRepairSeeder Sut(AuthDbContext db, ITwitchVideosApi? videos = null) =>
        new(db, videos ?? Archives(), NullLogger<StreamEndRepairSeeder>.Instance);

    private static async Task<Domain.Stream.Entities.Stream> Reload(AuthDbContext db, string id) =>
        await db.Streams.AsNoTracking().SingleAsync(s => s.Id == id);

    [Fact]
    public async Task A_row_ended_at_the_next_start_gets_the_end_of_its_last_chat_and_a_true_duration()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("old", First, Second));
        db.Streams.Add(Row("next", Second, Second.AddHours(7)));
        db.ChatMessages.Add(Chat("a", First.AddHours(1)));
        db.ChatMessages.Add(Chat("b", First.AddHours(6)));
        db.ChatMessages.Add(Chat("after", Second.AddHours(1)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        Domain.Stream.Entities.Stream old = await Reload(db, "old");
        old.EndedAt.Should().Be(First.AddHours(6));
        (old.EndedAt - old.StartedAt).Should().Be(TimeSpan.FromHours(6));
        Domain.Stream.Entities.Stream next = await Reload(db, "next");
        next.EndedAt.Should().Be(Second.AddHours(7), "a row with a different end is left alone");
    }

    [Fact]
    public async Task A_later_viewer_sample_beats_an_earlier_chat()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("old", First, Second));
        db.Streams.Add(Row("next", Second, null));
        db.ChatMessages.Add(Chat("a", First.AddHours(1)));
        db.EventJournals.Add(
            new EventJournal
            {
                EventId = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                StreamPosition = 1,
                EventType = "StreamViewerCountSampledEvent",
                EventVersion = 1,
                Source = "domain",
                Payload = "{}",
                Metadata = "{}",
                OccurredAt = First.AddHours(8).UtcDateTime,
                RecordedAt = First.AddHours(8).UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await Reload(db, "old")).EndedAt.Should().Be(First.AddHours(8));
    }

    [Fact]
    public async Task With_no_activity_the_end_is_the_rows_own_start_and_a_second_run_changes_nothing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("old", First, Second));
        db.Streams.Add(Row("next", Second, Second.AddHours(7)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();
        await Sut(db).SeedAsync();

        Domain.Stream.Entities.Stream old = await Reload(db, "old");
        old.EndedAt.Should().Be(First, "no proof of live time, so no invented length");
        (old.EndedAt - old.StartedAt).Should().Be(TimeSpan.Zero);
    }

    private static long _position;

    private static EventJournal Sample(DateTimeOffset at) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            StreamPosition = Interlocked.Increment(ref _position),
            EventType = "StreamViewerCountSampledEvent",
            EventVersion = 1,
            Source = "domain",
            Payload = "{}",
            Metadata = "{}",
            OccurredAt = at.UtcDateTime,
            RecordedAt = at.UtcDateTime,
        };

    [Fact]
    public async Task A_row_that_ran_for_days_ends_at_its_last_viewer_sample_not_at_later_offline_chat()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset start = new(2026, 10, 8, 11, 59, 37, TimeSpan.Zero);
        DateTimeOffset nextStart = start.AddDays(3);
        db.Streams.Add(Row("old", start, nextStart.AddMinutes(-1)));
        db.Streams.Add(Row("next", nextStart, nextStart.AddHours(7)));
        db.EventJournals.Add(Sample(start.AddHours(1)));
        db.EventJournals.Add(Sample(start.AddHours(7)));
        db.ChatMessages.Add(Chat("offline-chat", start.AddHours(40)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        Domain.Stream.Entities.Stream old = await Reload(db, "old");
        old.EndedAt.Should().Be(start.AddHours(7));
        (old.EndedAt - old.StartedAt).Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public async Task Without_samples_a_long_row_ends_where_the_first_run_of_chat_goes_quiet()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset start = new(2026, 6, 30, 16, 11, 47, TimeSpan.Zero);
        db.Streams.Add(Row("old", start, start.AddDays(16)));
        db.ChatMessages.Add(Chat("a", start.AddHours(1)));
        db.ChatMessages.Add(Chat("b", start.AddHours(4)));
        db.ChatMessages.Add(Chat("next-day", start.AddHours(30)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();
        await Sut(db).SeedAsync();

        (await Reload(db, "old")).EndedAt.Should().Be(start.AddHours(4));
    }

    [Fact]
    public async Task A_real_24_hour_stream_with_activity_up_to_its_end_is_not_cut()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset start = new(2026, 9, 1, 11, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = start.AddHours(24);
        db.Streams.Add(Row("marathon", start, end));
        for (int hour = 1; hour <= 23; hour++)
            db.EventJournals.Add(Sample(start.AddHours(hour)));
        db.EventJournals.Add(Sample(end.AddMinutes(-3)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await Reload(db, "marathon")).EndedAt.Should().Be(end);
    }

    [Fact]
    public async Task A_real_14_hour_stream_with_samples_to_its_end_is_not_cut()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset start = new(2026, 10, 15, 11, 59, 0, TimeSpan.Zero);
        DateTimeOffset end = start.AddHours(14);
        db.Streams.Add(Row("long", start, end));
        for (int hour = 1; hour <= 13; hour++)
            db.EventJournals.Add(Sample(start.AddHours(hour)));
        db.EventJournals.Add(Sample(end.AddMinutes(-2)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await Reload(db, "long")).EndedAt.Should().Be(end);
    }

    [Fact]
    public async Task A_short_row_whose_end_is_far_past_its_last_sample_ends_at_that_sample()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        db.Streams.Add(Row("short", start, start.AddHours(9)));
        db.EventJournals.Add(Sample(start.AddHours(1)));
        db.EventJournals.Add(Sample(start.AddHours(3)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();
        await Sut(db).SeedAsync();

        (await Reload(db, "short")).EndedAt.Should().Be(start.AddHours(3));
    }

    private static TwitchVideo Vod(DateTimeOffset createdAt, string duration) =>
        new(
            Id: "v1",
            StreamId: "s1",
            UserId: "tw-f1",
            UserLogin: "streamer_f1",
            UserName: "Streamer_F1",
            Title: "t",
            Description: "",
            CreatedAt: createdAt,
            PublishedAt: createdAt,
            Url: "u",
            ThumbnailUrl: "th",
            Viewable: "public",
            ViewCount: 1,
            Language: "en",
            Type: "archive",
            Duration: duration,
            MutedSegments: null
        );

    private static ITwitchVideosApi Archives(params TwitchVideo[] vods)
    {
        ITwitchVideosApi videos = Substitute.For<ITwitchVideosApi>();
        videos
            .GetVideosByTwitchUserIdAsync(
                "tw-f1",
                "archive",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new TwitchPage<TwitchVideo>(vods, null, vods.Length)));
        return videos;
    }

    [Fact]
    public async Task A_row_whose_twitch_video_is_far_shorter_ends_where_the_video_ends()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset start = new(2026, 8, 23, 11, 0, 44, TimeSpan.Zero);
        db.Streams.Add(Row("long", start, start.AddHours(48).AddMinutes(12)));
        for (int hour = 1; hour <= 47; hour++)
            db.EventJournals.Add(Sample(start.AddHours(hour)));
        db.EventJournals.Add(Sample(start.AddHours(48).AddMinutes(11)));
        await db.SaveChangesAsync();
        ITwitchVideosApi videos = Archives(Vod(start.AddSeconds(7), "9h34m50s"));

        await Sut(db, videos).SeedAsync();

        (await Reload(db, "long"))
            .EndedAt.Should()
            .Be(start.AddSeconds(7).AddHours(9).AddMinutes(34).AddSeconds(50));
    }

    [Fact]
    public async Task A_row_whose_twitch_video_matches_its_length_is_not_cut()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset start = new(2026, 10, 15, 11, 59, 43, TimeSpan.Zero);
        DateTimeOffset end = start.AddHours(14).AddSeconds(53);
        db.Streams.Add(Row("real", start, end));
        db.EventJournals.Add(Sample(end.AddMinutes(-2)));
        await db.SaveChangesAsync();
        ITwitchVideosApi videos = Archives(Vod(start.AddSeconds(5), "14h0m51s"));

        await Sut(db, videos).SeedAsync();

        (await Reload(db, "real")).EndedAt.Should().Be(end);
    }

    [Fact]
    public async Task A_twitch_video_that_started_at_another_time_is_not_proof_for_the_row()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset start = new(2026, 8, 23, 11, 0, 44, TimeSpan.Zero);
        DateTimeOffset end = start.AddHours(20);
        db.Streams.Add(Row("long", start, end));
        db.EventJournals.Add(Sample(end.AddMinutes(-2)));
        await db.SaveChangesAsync();
        ITwitchVideosApi videos = Archives(Vod(start.AddHours(5), "1h"));

        await Sut(db, videos).SeedAsync();

        (await Reload(db, "long")).EndedAt.Should().Be(end);
    }

    [Fact]
    public async Task A_real_end_before_the_next_start_is_not_touched()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        DateTimeOffset realEnd = First.AddHours(7).AddMilliseconds(412);
        db.Streams.Add(Row("old", First, realEnd));
        db.Streams.Add(Row("next", Second, null));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await Reload(db, "old")).EndedAt.Should().Be(realEnd);
    }
}
