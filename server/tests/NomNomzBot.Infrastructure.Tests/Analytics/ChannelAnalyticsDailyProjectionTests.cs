// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the channel-daily projection (analytics.md §3.1, M.8): it folds the journal into the per-channel daily
/// aggregate (chat → messages, follow → new followers, sub/gift → new subscribers, cheer → bits, successful
/// command/reward/song-request/game → counts, currency credit/debit → earned/spent totals), ignores
/// directory-level events with no channel, and a reset clears the read model for replay.
/// </summary>
public sealed class ChannelAnalyticsDailyProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly DateTime Now = new(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);
    private static int _seq;

    private static (ChannelAnalyticsDailyProjection Sut, AuthDbContext Db) Build(
        string? coveringStreamId = "stream-1"
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ILiveWindowResolver liveWindow = Substitute.For<ILiveWindowResolver>();
        liveWindow
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(coveringStreamId);
        return (new(db, liveWindow), db);
    }

    private static EventRecord Event(
        string eventType,
        Guid? broadcaster,
        string payload = "{}",
        DateTime? at = null
    ) =>
        new(
            ++_seq,
            Guid.NewGuid(),
            broadcaster,
            _seq,
            eventType,
            1,
            "domain",
            payload,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}",
            at ?? Now,
            at ?? Now
        );

    [Fact]
    public async Task Folds_activity_into_the_per_channel_daily_aggregate()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel));
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel));
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel));
        // The live translator's FollowEvent and the pre-canonicalization legacy-import
        // NewFollowerEvent both count — a rebuild over a mixed journal must not undercount.
        await sut.ApplyAsync(Event("FollowEvent", Channel));
        await sut.ApplyAsync(Event("NewFollowerEvent", Channel));
        await sut.ApplyAsync(Event("NewSubscriptionEvent", Channel));
        // A resub is sub activity too — previously dropped entirely, so renewals showed 0 subscribers.
        await sut.ApplyAsync(Event("ResubscriptionEvent", Channel));
        await sut.ApplyAsync(Event("GiftSubscriptionEvent", Channel));
        await sut.ApplyAsync(Event("CommandExecutedEvent", Channel, "{\"Succeeded\":true}"));
        await sut.ApplyAsync(Event("RewardRedeemedEvent", Channel));
        await sut.ApplyAsync(Event("CheerEvent", Channel, "{\"Bits\":100}"));
        await sut.ApplyAsync(Event("SongRequestedEvent", Channel));
        await sut.ApplyAsync(Event("CurrencyCreditedEvent", Channel, "{\"Amount\":50}"));
        await sut.ApplyAsync(Event("CurrencyCreditedEvent", Channel, "{\"Amount\":25}"));
        await sut.ApplyAsync(Event("CurrencyDebitedEvent", Channel, "{\"Amount\":-30}"));
        await sut.ApplyAsync(Event("GamePlayedEvent", Channel));

        ChannelAnalyticsDaily row = db.ChannelAnalyticsDailies.Single();
        row.BroadcasterId.Should().Be(Channel);
        row.ActivityDate.Should().Be(new(2026, 6, 22));
        row.TotalMessages.Should().Be(3);
        row.NewFollowers.Should().Be(2); // live FollowEvent + legacy NewFollowerEvent
        row.NewSubscribers.Should().Be(3); // new sub + resub + gift
        row.CommandsRun.Should().Be(1);
        row.RedemptionsCount.Should().Be(1);
        row.BitsCheered.Should().Be(100);
        row.SongRequests.Should().Be(1);
        row.CurrencyEarnedTotal.Should().Be(75);
        row.CurrencySpentTotal.Should().Be(30); // debit Amount travels negative — magnitude folds
        row.GamesPlayed.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_command_run_does_not_count_as_executed()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Event("CommandExecutedEvent", Channel, "{\"Succeeded\":false}"));
        await sut.ApplyAsync(Event("CommandExecutedEvent", Channel, "{\"Succeeded\":true}"));

        db.ChannelAnalyticsDailies.Single().CommandsRun.Should().Be(1);
    }

    [Fact]
    public async Task Ignores_a_directory_level_event_with_no_channel()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", broadcaster: null));

        db.ChannelAnalyticsDailies.Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_clears_the_aggregate_and_the_chatter_day_anchor_for_replay()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1")));
        db.ChannelAnalyticsDailies.Should().ContainSingle();
        db.ChannelChatterDays.Should().ContainSingle();

        await sut.ResetAsync(Channel);

        db.ChannelAnalyticsDailies.Should().BeEmpty();
        // Without this a replay would see every chatter as already counted and fold UniqueChatters as 0.
        db.ChannelChatterDays.Should().BeEmpty();
    }

    [Fact]
    public async Task Unique_chatters_counts_each_viewer_once_per_day_and_only_when_they_chat()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1")));
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1")));
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel, ChatPayload("u2")));
        // A redemption-only viewer is presence, not a chatter…
        await sut.ApplyAsync(Event("RewardRedeemedEvent", Channel, ChatPayload("u3")));

        ChannelAnalyticsDaily row = db.ChannelAnalyticsDailies.Single();
        row.UniqueChatters.Should().Be(2);
        row.TotalMessages.Should().Be(3);

        // …until they chat later the same day — then they count exactly once.
        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel, ChatPayload("u3")));
        db.ChannelAnalyticsDailies.Single().UniqueChatters.Should().Be(3);
    }

    [Fact]
    public async Task Watch_seconds_accrue_between_presence_events_inside_the_same_stream()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();

        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1"), Now));
        await sut.ApplyAsync(
            Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1"), Now.AddSeconds(300))
        );
        // A second viewer's span adds independently.
        await sut.ApplyAsync(
            Event("ChatMessageReceivedEvent", Channel, ChatPayload("u2"), Now.AddSeconds(100))
        );
        await sut.ApplyAsync(
            Event("RewardRedeemedEvent", Channel, ChatPayload("u2"), Now.AddSeconds(220))
        );

        db.ChannelAnalyticsDailies.Single().TotalWatchSeconds.Should().Be(300 + 120);
    }

    [Fact]
    public async Task Watch_seconds_never_accrue_offline_or_across_streams()
    {
        // No covering live window at all: chatters still count, watch time stays zero.
        (ChannelAnalyticsDailyProjection offlineSut, AuthDbContext offlineDb) = Build(
            coveringStreamId: null
        );
        await offlineSut.ApplyAsync(
            Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1"), Now)
        );
        await offlineSut.ApplyAsync(
            Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1"), Now.AddSeconds(500))
        );
        ChannelAnalyticsDaily offlineRow = offlineDb.ChannelAnalyticsDailies.Single();
        offlineRow.UniqueChatters.Should().Be(1);
        offlineRow.TotalWatchSeconds.Should().Be(0);

        // Two different streams the same day: the gap BETWEEN them must not count as watch time.
        AuthDbContext db = AuthTestBuilder.NewContext();
        ILiveWindowResolver liveWindow = Substitute.For<ILiveWindowResolver>();
        liveWindow
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
                callInfo.ArgAt<DateTime>(1) < Now.AddHours(2) ? "stream-1" : "stream-2"
            );
        ChannelAnalyticsDailyProjection sut = new(db, liveWindow);

        await sut.ApplyAsync(Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1"), Now));
        await sut.ApplyAsync(
            Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1"), Now.AddSeconds(60))
        );
        // Next presence lands in stream-2 — the 3h gap is between streams, not watching.
        await sut.ApplyAsync(
            Event("ChatMessageReceivedEvent", Channel, ChatPayload("u1"), Now.AddHours(3))
        );
        await sut.ApplyAsync(
            Event(
                "ChatMessageReceivedEvent",
                Channel,
                ChatPayload("u1"),
                Now.AddHours(3).AddSeconds(40)
            )
        );

        db.ChannelAnalyticsDailies.Single().TotalWatchSeconds.Should().Be(60 + 40);
    }

    [Fact]
    public async Task Peak_viewers_folds_the_daily_maximum_of_the_sampled_counts()
    {
        (ChannelAnalyticsDailyProjection sut, AuthDbContext db) = Build();

        db.ChannelAnalyticsDailies.Should().BeEmpty();
        await sut.ApplyAsync(
            Event("StreamViewerCountSampledEvent", Channel, "{\"ViewerCount\":5}")
        );
        await sut.ApplyAsync(
            Event("StreamViewerCountSampledEvent", Channel, "{\"ViewerCount\":12}")
        );
        await sut.ApplyAsync(
            Event("StreamViewerCountSampledEvent", Channel, "{\"ViewerCount\":8}")
        );

        db.ChannelAnalyticsDailies.Single().PeakViewers.Should().Be(12);
    }

    /// <summary>A chat-shaped payload carrying the viewer identity the presence fold parses.</summary>
    private static string ChatPayload(string userId) =>
        $"{{\"UserId\":\"{userId}\",\"UserLogin\":\"{userId}\",\"UserDisplayName\":\"{userId}\"}}";

    [Fact]
    public async Task Two_concurrent_folds_of_the_same_chatter_collide_silently_and_still_land_correctly()
    {
        // Two independent contexts over the SAME shared-cache SQLite store — the real-DB analogue of two
        // API instances racing on the same viewer's first message of the day. Both wired to one capturing
        // logger factory so every EF log entry from either connection is visible to the assertion.
        CapturingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = new LoggerFactory(new[] { logs });
        string dbName = $"chatterday-race-{Guid.NewGuid()}";
        using SqliteConnection keepAlive = OpenSharedCacheKeepAlive(dbName);
        using AuthDbContext dbA = NewLoggingContext(dbName, loggerFactory);
        using AuthDbContext dbB = NewLoggingContext(dbName, loggerFactory);

        ILiveWindowResolver liveWindowA = Substitute.For<ILiveWindowResolver>();
        liveWindowA
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("stream-1");
        ILiveWindowResolver liveWindowB = Substitute.For<ILiveWindowResolver>();
        liveWindowB
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("stream-1");

        ChannelAnalyticsDailyProjection sutA = new(dbA, liveWindowA);
        ChannelAnalyticsDailyProjection sutB = new(dbB, liveWindowB);

        DateTime earlier = Now;
        DateTime later = Now.AddSeconds(90);
        EventRecord first = Event(
            "ChatMessageReceivedEvent",
            Channel,
            ChatPayload("racer"),
            earlier
        );
        EventRecord second = Event(
            "ChatMessageReceivedEvent",
            Channel,
            ChatPayload("racer"),
            later
        );

        await Task.WhenAll(sutA.ApplyAsync(first), sutB.ApplyAsync(second));

        logs.Entries.Where(e => e.Level >= LogLevel.Error)
            .Should()
            .BeEmpty(
                "the anchor unique-index collision is an expected, handled race — it must never surface as an error-level log"
            );

        ChannelChatterDay anchor = dbA
            .ChannelChatterDays.AsNoTracking()
            .Single(a =>
                a.BroadcasterId == Channel && a.ChatterHash == ChatterIdentityHashFor("racer")
            );
        anchor.Chatted.Should().BeTrue();
        anchor.LastSeenAt.Should().Be(later); // the later of the two racing timestamps wins

        ChannelAnalyticsDaily row = dbA
            .ChannelAnalyticsDailies.AsNoTracking()
            .Single(r => r.BroadcasterId == Channel);
        row.UniqueChatters.Should().Be(1); // one viewer, counted exactly once despite the race
        row.TotalMessages.Should().Be(2);
        row.TotalWatchSeconds.Should().Be(90); // first→last span still folds across the two racing folds
    }

    [Fact]
    public async Task First_fold_of_the_day_mints_the_daily_row_without_an_error_level_log()
    {
        // A first fold on a channel with no daily row yet goes through EnsureDailyRowExistsAsync's
        // insert path. Even on a single, uncontested writer this must never touch the tracked-insert
        // fallback that lets a duplicate-key violation reach EF's relational diagnostics logger.
        CapturingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = new LoggerFactory(new[] { logs });
        string dbName = $"dailyrow-first-fold-{Guid.NewGuid()}";
        using SqliteConnection keepAlive = OpenSharedCacheKeepAlive(dbName);
        using AuthDbContext db = NewLoggingContext(dbName, loggerFactory);
        ILiveWindowResolver liveWindow = Substitute.For<ILiveWindowResolver>();
        liveWindow
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("stream-1");
        ChannelAnalyticsDailyProjection sut = new(db, liveWindow);

        await sut.ApplyAsync(Event("CheerEvent", Channel, "{\"Bits\":100}"));

        logs.Entries.Where(e => e.Level >= LogLevel.Error)
            .Should()
            .BeEmpty("minting the first daily row of the day is normal operation, not a failure");

        ChannelAnalyticsDaily row = db.ChannelAnalyticsDailies.AsNoTracking().Single();
        row.BroadcasterId.Should().Be(Channel);
        row.ActivityDate.Should().Be(DateOnly.FromDateTime(Now));
        row.BitsCheered.Should().Be(100);
    }

    [Fact]
    public async Task Two_concurrent_first_folds_of_the_day_collide_silently_and_still_land_correctly()
    {
        // Two independent contexts over the SAME shared-cache SQLite store, both racing to mint the SAME
        // (BroadcasterId, ActivityDate) daily row on their first fold of the day — the real-DB analogue of
        // two API instances processing the first events of the day concurrently.
        CapturingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = new LoggerFactory(new[] { logs });
        string dbName = $"dailyrow-race-{Guid.NewGuid()}";
        using SqliteConnection keepAlive = OpenSharedCacheKeepAlive(dbName);
        using AuthDbContext dbA = NewLoggingContext(dbName, loggerFactory);
        using AuthDbContext dbB = NewLoggingContext(dbName, loggerFactory);

        ILiveWindowResolver liveWindowA = Substitute.For<ILiveWindowResolver>();
        liveWindowA
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("stream-1");
        ILiveWindowResolver liveWindowB = Substitute.For<ILiveWindowResolver>();
        liveWindowB
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("stream-1");

        ChannelAnalyticsDailyProjection sutA = new(dbA, liveWindowA);
        ChannelAnalyticsDailyProjection sutB = new(dbB, liveWindowB);

        await Task.WhenAll(
            sutA.ApplyAsync(Event("CheerEvent", Channel, "{\"Bits\":100}")),
            sutB.ApplyAsync(Event("SongRequestedEvent", Channel))
        );

        logs.Entries.Where(e => e.Level >= LogLevel.Error)
            .Should()
            .BeEmpty(
                "the daily-row unique-index collision is an expected, handled race — it must never surface as an error-level log"
            );

        ChannelAnalyticsDaily row = dbA
            .ChannelAnalyticsDailies.AsNoTracking()
            .Single(r => r.BroadcasterId == Channel);
        row.BitsCheered.Should().Be(100);
        row.SongRequests.Should().Be(1);
    }

    [Fact]
    public async Task A_genuine_save_failure_on_the_context_is_still_logged_at_error_level()
    {
        CapturingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = new LoggerFactory(new[] { logs });
        string dbName = $"chatterday-genuine-failure-{Guid.NewGuid()}";
        using SqliteConnection keepAlive = OpenSharedCacheKeepAlive(dbName);
        using AuthDbContext db = NewLoggingContext(dbName, loggerFactory);

        // Seed one anchor directly (bypassing the projection's conflict-tolerant path entirely), then try to
        // insert a SECOND row with the identical (BroadcasterId, ActivityDate, ChatterHash) key through the
        // ordinary tracked Add + SaveChangesAsync path every other write in this context still uses. This is
        // a genuine, un-tolerated constraint violation — proving the safety net was not silenced globally.
        DateOnly day = DateOnly.FromDateTime(Now);
        db.ChannelChatterDays.Add(
            new()
            {
                BroadcasterId = Channel,
                ActivityDate = day,
                ChatterHash = "dup-hash",
                Chatted = true,
                FirstSeenAt = Now,
                LastSeenAt = Now,
            }
        );
        await db.SaveChangesAsync();

        db.ChannelChatterDays.Add(
            new()
            {
                BroadcasterId = Channel,
                ActivityDate = day,
                ChatterHash = "dup-hash",
                Chatted = true,
                FirstSeenAt = Now,
                LastSeenAt = Now,
            }
        );

        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        logs.Entries.Should()
            .Contain(
                e => e.Level == LogLevel.Error,
                "a real, un-tolerated constraint violation must still surface at error level"
            );
    }

    private static string ChatterIdentityHashFor(string userId) =>
        NomNomzBot.Infrastructure.Analytics.ChatterIdentityHash.Compute("twitch", userId);

    private static SqliteConnection OpenSharedCacheKeepAlive(string dbName)
    {
        SqliteConnection connection = new(SharedCacheConnectionString(dbName));
        connection.Open();
        return connection;
    }

    private static AuthDbContext NewLoggingContext(string dbName, ILoggerFactory loggerFactory)
    {
        AuthDbContext db = new(
            new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlite(SharedCacheConnectionString(dbName))
                .UseLoggerFactory(loggerFactory)
                .Options
        );
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        db.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 5000;");
        db.Database.EnsureCreated();
        return db;
    }

    private static string SharedCacheConnectionString(string dbName) =>
        $"Data Source=file:{dbName}?mode=memory&cache=shared";

    /// <summary>Captures every log entry EF emits through the attached <see cref="ILoggerFactory"/>.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Category, string Message)> Entries { get; } =
            new();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName, Entries);

        public void Dispose() { }

        private sealed class CapturingLogger(
            string category,
            ConcurrentQueue<(LogLevel Level, string Category, string Message)> entries
        ) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => entries.Enqueue((logLevel, category, formatter(state, exception)));
        }
    }
}
