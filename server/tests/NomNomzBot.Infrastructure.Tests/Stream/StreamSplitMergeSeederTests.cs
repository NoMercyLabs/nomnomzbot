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
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Stream.Entities;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// One real stream stored as two rows (the 2026-02-24 24 hour stream was imported as 22:57 to 06:21 and
/// 06:22 to 23:01) is one row again: the first row keeps its start and takes the later end, the facts of the
/// second row move to it, the second row goes, and the recap is rebuilt for the merged row.
/// </summary>
public sealed class StreamSplitMergeSeederTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000e1");
    private static readonly Guid Owner = Guid.Parse("0192f000-0000-7000-8000-0000000000e9");
    private static readonly Guid Viewer = Guid.Parse("0192f000-0000-7000-8000-0000000000d1");
    private static readonly Guid OnlyLater = Guid.Parse("0192f000-0000-7000-8000-0000000000d2");
    private static readonly DateTimeOffset FirstStart = new(2026, 2, 24, 22, 57, 27, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstEnd = new(2026, 2, 25, 6, 21, 45, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondStart = new(2026, 2, 25, 6, 22, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondEnd = new(2026, 2, 25, 23, 1, 34, TimeSpan.Zero);

    private static Channel NewChannel() =>
        new()
        {
            Id = Broadcaster,
            OwnerUserId = Owner,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = "tw-e1",
            TwitchChannelId = "tw-e1",
            Name = "streamer_e1",
            NameNormalized = "streamer_e1",
            IsOnboarded = true,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };

    private static Domain.Stream.Entities.Stream Row(
        string id,
        DateTimeOffset start,
        DateTimeOffset end
    ) =>
        new()
        {
            Id = id,
            ChannelId = Broadcaster,
            StartedAt = start,
            EndedAt = end,
        };

    private static ChatMessage Chat(string id, DateTimeOffset at, string? streamId = null) =>
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
            StreamId = streamId,
        };

    private static WatchSession Watch(
        Guid viewer,
        string streamId,
        DateTimeOffset start,
        DateTimeOffset end,
        int messages
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            ViewerProfileId = viewer,
            ViewerUserId = viewer,
            StreamId = streamId,
            StartedAt = start.UtcDateTime,
            EndedAt = end.UtcDateTime,
            DurationSeconds = (long)(end - start).TotalSeconds,
            PresenceConfirmed = true,
            MessageCountInSession = messages,
        };

    private static StreamRecap Recap(string streamId, DateTimeOffset from, DateTimeOffset to) =>
        new()
        {
            ChannelId = Broadcaster,
            StreamId = streamId,
            FromAt = from.UtcDateTime,
            ToAt = to.UtcDateTime,
            DataJson = "{}",
        };

    private static IStreamRecapService RecapService()
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
                        StreamId = "first",
                        Stats = new() { Stream = new() { Id = "first" } },
                    }
                )
            );
        return recaps;
    }

    private static StreamSplitMergeSeeder Sut(
        AuthDbContext db,
        IStreamRecapService? recaps = null
    ) => new(db, recaps ?? RecapService(), NullLogger<StreamSplitMergeSeeder>.Instance);

    private static async Task<AuthDbContext> SplitStreamAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("first", FirstStart, FirstEnd));
        db.Streams.Add(Row("second", SecondStart, SecondEnd));
        db.ChatMessages.Add(Chat("c1", FirstEnd.AddMinutes(-2)));
        db.ChatMessages.Add(Chat("c2", SecondStart.AddMinutes(3), "second"));
        db.WatchSessions.Add(Watch(Viewer, "first", FirstStart, FirstEnd, 10));
        db.WatchSessions.Add(Watch(Viewer, "second", SecondStart, SecondEnd, 5));
        db.WatchSessions.Add(Watch(OnlyLater, "second", SecondStart, SecondEnd, 2));
        db.StreamRecaps.Add(Recap("first", FirstStart, FirstEnd));
        db.StreamRecaps.Add(Recap("second", SecondStart, SecondEnd));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Two_rows_with_chat_across_a_short_gap_become_one_row_with_the_first_start_and_the_last_end()
    {
        AuthDbContext db = await SplitStreamAsync();

        await Sut(db).SeedAsync();

        List<Domain.Stream.Entities.Stream> rows = await db
            .Streams.AsNoTracking()
            .OrderBy(s => s.StartedAt)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be("first");
        rows[0].StartedAt.Should().Be(FirstStart);
        rows[0].EndedAt.Should().Be(SecondEnd);
        (rows[0].EndedAt - rows[0].StartedAt).Should().BeGreaterThan(TimeSpan.FromHours(23));
    }

    [Fact]
    public async Task The_facts_of_the_second_row_move_to_the_first_and_a_shared_viewer_session_is_summed()
    {
        AuthDbContext db = await SplitStreamAsync();

        await Sut(db).SeedAsync();

        (await db.ChatMessages.AsNoTracking().SingleAsync(m => m.Id == "c2"))
            .StreamId.Should()
            .Be("first");
        List<WatchSession> sessions = await db
            .WatchSessions.AsNoTracking()
            .OrderBy(w => w.ViewerUserId)
            .ToListAsync();
        sessions.Should().OnlyContain(w => w.StreamId == "first");
        sessions.Should().HaveCount(2);
        WatchSession shared = sessions.Single(w => w.ViewerUserId == Viewer);
        shared.MessageCountInSession.Should().Be(15);
        shared.StartedAt.Should().Be(FirstStart.UtcDateTime);
        shared.EndedAt.Should().Be(SecondEnd.UtcDateTime);
        shared
            .DurationSeconds.Should()
            .Be(
                (long)(FirstEnd - FirstStart).TotalSeconds
                    + (long)(SecondEnd - SecondStart).TotalSeconds
            );
    }

    [Fact]
    public async Task The_recap_of_the_second_row_goes_and_the_merged_row_gets_a_rebuilt_recap()
    {
        AuthDbContext db = await SplitStreamAsync();
        IStreamRecapService recaps = RecapService();

        await Sut(db, recaps).SeedAsync();

        (await db.StreamRecaps.AsNoTracking().Select(r => r.StreamId).ToListAsync())
            .Should()
            .BeEquivalentTo(["first"]);
        await recaps
            .Received(1)
            .BuildAsync(
                Broadcaster,
                "first",
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        AuthDbContext db = await SplitStreamAsync();
        IStreamRecapService recaps = RecapService();
        await Sut(db, recaps).SeedAsync();

        await Sut(db, recaps).SeedAsync();

        (await db.Streams.CountAsync()).Should().Be(1);
        (await db.WatchSessions.SingleAsync(w => w.ViewerUserId == Viewer))
            .MessageCountInSession.Should()
            .Be(15);
        await recaps
            .Received(1)
            .BuildAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_gap_of_ten_minutes_or_more_stays_two_streams()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("first", FirstStart, FirstEnd));
        db.Streams.Add(Row("second", FirstEnd.AddMinutes(10), SecondEnd));
        db.ChatMessages.Add(Chat("c1", FirstEnd.AddMinutes(-2)));
        db.ChatMessages.Add(Chat("c2", FirstEnd.AddMinutes(12)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await db.Streams.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_short_gap_without_chat_or_samples_on_both_sides_stays_two_streams()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(NewChannel());
        db.Streams.Add(Row("first", FirstStart, FirstEnd));
        db.Streams.Add(Row("second", SecondStart, SecondEnd));
        db.ChatMessages.Add(Chat("c1", FirstEnd.AddMinutes(-2)));
        await db.SaveChangesAsync();

        await Sut(db).SeedAsync();

        (await db.Streams.CountAsync()).Should().Be(2);
        (await db.Streams.AsNoTracking().SingleAsync(s => s.Id == "first"))
            .EndedAt.Should()
            .Be(FirstEnd);
    }
}
