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
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Stream.Entities;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// An imported stream lost its title. The seeder gives it back from stored facts only: a twin row of the same
/// start that kept the title, then the last title change the journal saw before the stream ended, then the title
/// of Twitch's own recording. A stream no source knows stays empty (the list shows a dash), and a title that is
/// already there is never replaced.
/// </summary>
public sealed class StreamTitleBackfillSeederTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000f1");
    private static readonly Guid Owner = Guid.Parse("0192f000-0000-7000-8000-0000000000f9");
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(5);

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

    private static Domain.Stream.Entities.Stream Row(string id, string? title) =>
        new()
        {
            Id = id,
            ChannelId = Broadcaster,
            StartedAt = Start,
            EndedAt = End,
            Title = title,
        };

    private static EventJournal TitleChange(DateTimeOffset at, string title, long position) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            StreamPosition = position,
            EventType = "ChannelUpdatedEvent",
            EventVersion = 1,
            Source = "domain",
            Payload =
                $"{{\"Provider\":\"twitch\",\"BroadcasterDisplayName\":\"S\",\"NewTitle\":\"{title}\",\"NewGameName\":\"g\"}}",
            Metadata = "{}",
            OccurredAt = at.UtcDateTime,
            RecordedAt = at.UtcDateTime,
        };

    private static TwitchVideo Vod(DateTimeOffset createdAt, string title) =>
        new(
            Id: "v1",
            StreamId: "s1",
            UserId: "tw-f1",
            UserLogin: "streamer_f1",
            UserName: "Streamer_F1",
            Title: title,
            Description: "",
            CreatedAt: createdAt,
            PublishedAt: createdAt,
            Url: "u",
            ThumbnailUrl: "th",
            Viewable: "public",
            ViewCount: 1,
            Language: "en",
            Type: "archive",
            Duration: "5h0m0s",
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

    private static IStreamRecapService Recaps()
    {
        IStreamRecapService recaps = Substitute.For<IStreamRecapService>();
        recaps
            .BuildAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new StreamRecapDto
                    {
                        StreamId = "a",
                        Stats = new() { Stream = new() { Id = "a" } },
                    }
                )
            );
        return recaps;
    }

    private static StreamTitleBackfillSeeder Sut(
        AuthDbContext db,
        ITwitchVideosApi? videos = null,
        IStreamRecapService? recaps = null
    ) =>
        new(
            db,
            videos ?? Archives(),
            recaps ?? Recaps(),
            NullLogger<StreamTitleBackfillSeeder>.Instance
        );

    private static async Task<string?> TitleOf(AuthDbContext db, string id) =>
        (await db.Streams.AsNoTracking().SingleAsync(s => s.Id == id)).Title;

    [Fact]
    public async Task A_row_without_a_title_takes_the_title_of_its_twin_with_the_same_start()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("a", null));
        db.Streams.Add(Row("b", "Twin title"));
        db.StreamRecaps.Add(
            new StreamRecap
            {
                ChannelId = Broadcaster,
                StreamId = "a",
                FromAt = Start.UtcDateTime,
                ToAt = End.UtcDateTime,
                DataJson = "{}",
            }
        );
        await db.SaveChangesAsync();
        IStreamRecapService recaps = Recaps();

        await Sut(db, recaps: recaps).SeedAsync();

        (await TitleOf(db, "a")).Should().Be("Twin title");
        (await TitleOf(db, "b")).Should().Be("Twin title");
        await recaps
            .Received(1)
            .BuildAsync(
                Broadcaster,
                "a",
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_row_without_a_title_takes_the_last_title_change_before_it_ended()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("a", ""));
        db.EventJournals.Add(TitleChange(Start.AddHours(-30), "Old title", 1));
        db.EventJournals.Add(TitleChange(Start.AddHours(1), "Midstream title", 2));
        db.EventJournals.Add(TitleChange(End.AddHours(1), "Next stream title", 3));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await TitleOf(db, "a")).Should().Be("Midstream title");
    }

    [Fact]
    public async Task A_row_with_no_stored_title_takes_the_title_of_its_twitch_recording()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("a", null));
        await db.SaveChangesAsync();
        ITwitchVideosApi videos = Archives(
            Vod(Start.AddHours(-20), "Another day"),
            Vod(Start.AddSeconds(9), "Recorded title")
        );

        await Sut(db, videos).SeedAsync();

        (await TitleOf(db, "a")).Should().Be("Recorded title");
    }

    [Fact]
    public async Task A_row_no_source_knows_stays_empty_and_is_not_rebuilt()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("a", null));
        await db.SaveChangesAsync();
        IStreamRecapService recaps = Recaps();

        await Sut(db, Archives(Vod(Start.AddHours(-20), "Another day")), recaps).SeedAsync();

        (await TitleOf(db, "a")).Should().BeNullOrEmpty();
        await recaps
            .DidNotReceive()
            .BuildAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_title_that_is_already_stored_is_never_replaced_and_a_second_run_changes_nothing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("a", "Kept title"));
        db.Streams.Add(Row("c", null));
        db.EventJournals.Add(TitleChange(Start.AddHours(1), "Journal title", 1));
        await db.SaveChangesAsync();
        IStreamRecapService recaps = Recaps();
        StreamTitleBackfillSeeder sut = Sut(db, recaps: recaps);

        await sut.SeedAsync();
        await sut.SeedAsync();

        (await TitleOf(db, "a")).Should().Be("Kept title");
        (await TitleOf(db, "c")).Should().Be("Kept title");
    }
}
