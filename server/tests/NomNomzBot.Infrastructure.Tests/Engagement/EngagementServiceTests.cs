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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Engagement.Dtos;
using NomNomzBot.Domain.Engagement.Entities;
using NomNomzBot.Domain.Engagement.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Engagement;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using DomainStream = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Infrastructure.Tests.Engagement;

/// <summary>
/// Proves the engagement detect→fire state machine (engagement.md §6): a first-ever message fires
/// FirstTimeChatterDetectedEvent exactly once (creating the state), a first message in a new stream fires
/// ReturningChatterDetectedEvent with the right days-since and bumps the streak, consecutive streams raise
/// the streak and hit configured milestones while a missed stream resets it, greet-dedup + the per-channel
/// cooldown suppress repeats (state still updates), and a disabled trigger fires nothing.
/// </summary>
public sealed class EngagementServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-00000000c001");
    private static readonly Guid Viewer = Guid.Parse("0192c000-0000-7000-8000-00000000a001");
    private static readonly Guid Viewer2 = Guid.Parse("0192c000-0000-7000-8000-00000000a002");

    private sealed class RecordingBus : IEventBus
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : class, IDomainEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Published.Add(@event);
    }

    private sealed record Harness(
        EngagementService Sut,
        EngagementTestDbContext Db,
        RecordingBus Bus,
        FakeTimeProvider Clock
    );

    private static Harness Build()
    {
        EngagementTestDbContext db = EngagementTestDbContext.New();
        db.Users.Add(
            new User
            {
                Id = Viewer,
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        db.Users.Add(
            new User
            {
                Id = Viewer2,
                TwitchUserId = "222",
                Username = "bob",
                UsernameNormalized = "bob",
                DisplayName = "Bob",
            }
        );
        db.SaveChanges();
        RecordingBus bus = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));
        EngagementService sut = new(db, bus, new CooldownManager(clock));
        return new Harness(sut, db, bus, clock);
    }

    private static void SeedConfig(
        EngagementTestDbContext db,
        bool firstTime = false,
        bool returning = false,
        bool streak = false,
        int[]? milestones = null,
        int cooldown = 5
    )
    {
        db.EngagementConfigs.Add(
            new EngagementConfig
            {
                BroadcasterId = Channel,
                FirstTimeChatterEnabled = firstTime,
                ReturningChatterEnabled = returning,
                WatchStreakEnabled = streak,
                StreakMilestonesJson = milestones is null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(milestones),
                GreetCooldownSeconds = cooldown,
            }
        );
        db.SaveChanges();
    }

    private static void SeedStream(EngagementTestDbContext db, string id, DateTimeOffset startedAt)
    {
        db.Streams.Add(
            new DomainStream
            {
                Id = id,
                ChannelId = Channel,
                StartedAt = startedAt,
            }
        );
        db.SaveChanges();
    }

    private static EngagementSignal Signal(
        DateTime at,
        string session,
        Guid? viewer = null,
        string external = "111",
        string display = "Alice"
    ) => new(viewer ?? Viewer, external, display, session, at);

    [Fact]
    public async Task FirstEverMessage_FiresFirstTimeOnce_CreatesState_SecondMessageFiresNothing()
    {
        Harness h = Build();
        SeedConfig(h.Db, firstTime: true);
        DateTime at = h.Clock.GetUtcNow().UtcDateTime;

        await h.Sut.OnChatActivityAsync(Channel, Signal(at, "s1"));
        await h.Sut.OnChatActivityAsync(Channel, Signal(at.AddSeconds(30), "s1"));

        h.Bus.Published.OfType<FirstTimeChatterDetectedEvent>().Should().ContainSingle();
        h.Bus.Published.Should().HaveCount(1);
        ViewerEngagementState state = await h.Db.ViewerEngagementStates.SingleAsync();
        state.ViewerUserId.Should().Be(Viewer);
        state.ConsecutiveStreams.Should().Be(1);
        state.LastGreetedStreamSessionId.Should().Be("s1");
        // PII-hashed, never the raw external id.
        state.ViewerTwitchUserId.Should().NotBe("111").And.HaveLength(32);
    }

    [Fact]
    public async Task NewStreamAfterAPriorStream_FiresReturning_WithDaysSinceLastSeen_AndBumpsStreak()
    {
        Harness h = Build();
        SeedConfig(h.Db, returning: true);
        SeedStream(h.Db, "s1", h.Clock.GetUtcNow());
        DateTime day1 = h.Clock.GetUtcNow().UtcDateTime;
        await h.Sut.OnChatActivityAsync(Channel, Signal(day1, "s1"));

        // Next stream, three days later.
        SeedStream(h.Db, "s2", h.Clock.GetUtcNow().AddDays(3));
        DateTime day4 = day1.AddDays(3);
        await h.Sut.OnChatActivityAsync(Channel, Signal(day4, "s2"));

        ReturningChatterDetectedEvent evt = h
            .Bus.Published.OfType<ReturningChatterDetectedEvent>()
            .Should()
            .ContainSingle()
            .Which;
        evt.DaysSinceLastSeen.Should().Be(3);
        ViewerEngagementState state = await h.Db.ViewerEngagementStates.SingleAsync();
        state.ConsecutiveStreams.Should().Be(2);
        state.LastSeenStreamSessionId.Should().Be("s2");
    }

    [Fact]
    public async Task ConsecutiveStreams_RaiseStreak_MilestoneFires_MissedStreamResetsToOne()
    {
        Harness h = Build();
        SeedConfig(h.Db, streak: true, milestones: [3]);

        // Three back-to-back streams — the viewer chats in each.
        DateTime start = h.Clock.GetUtcNow().UtcDateTime;
        for (int i = 1; i <= 3; i++)
        {
            SeedStream(h.Db, $"s{i}", h.Clock.GetUtcNow().AddDays(i));
            await h.Sut.OnChatActivityAsync(Channel, Signal(start.AddDays(i), $"s{i}"));
        }

        ViewerEngagementState afterThree = await h.Db.ViewerEngagementStates.SingleAsync();
        afterThree.ConsecutiveStreams.Should().Be(3);
        h.Bus.Published.OfType<WatchStreakMilestoneEvent>()
            .Should()
            .ContainSingle()
            .Which.StreakCount.Should()
            .Be(3);

        // Now a stream the viewer misses (s4 exists but they don't chat), then s5 where they return.
        SeedStream(h.Db, "s4", h.Clock.GetUtcNow().AddDays(4));
        SeedStream(h.Db, "s5", h.Clock.GetUtcNow().AddDays(5));
        await h.Sut.OnChatActivityAsync(Channel, Signal(start.AddDays(5), "s5"));

        ViewerEngagementState afterMiss = await h.Db.ViewerEngagementStates.SingleAsync();
        // s4 was the immediately-previous session and they did not chat in it → streak resets to 1.
        afterMiss.ConsecutiveStreams.Should().Be(1);
    }

    [Fact]
    public async Task SameStream_DoesNotRefire_ButStillUpdatesLastChat()
    {
        Harness h = Build();
        SeedConfig(h.Db, firstTime: true, returning: true);
        DateTime at = h.Clock.GetUtcNow().UtcDateTime;

        await h.Sut.OnChatActivityAsync(Channel, Signal(at, "s1"));
        await h.Sut.OnChatActivityAsync(Channel, Signal(at.AddMinutes(10), "s1"));
        await h.Sut.OnChatActivityAsync(Channel, Signal(at.AddMinutes(20), "s1"));

        h.Bus.Published.Should().HaveCount(1); // only the first-time greeting
        ViewerEngagementState state = await h.Db.ViewerEngagementStates.SingleAsync();
        state.LastChatAt.Should().Be(at.AddMinutes(20)); // state kept current
    }

    [Fact]
    public async Task GreetCooldown_SuppressesASecondViewersGreetingWithinTheWindow_StateStillUpdates()
    {
        Harness h = Build();
        SeedConfig(h.Db, firstTime: true, cooldown: 60);
        DateTime at = h.Clock.GetUtcNow().UtcDateTime;

        await h.Sut.OnChatActivityAsync(Channel, Signal(at, "s1"));
        // A different first-timer 5 seconds later — inside the 60s channel cooldown.
        await h.Sut.OnChatActivityAsync(
            Channel,
            Signal(at.AddSeconds(5), "s1", Viewer2, "222", "Bob")
        );

        h.Bus.Published.Should().HaveCount(1); // Bob's greeting was rate-limited
        // ...but Bob's state row was still created (state always updates).
        ViewerEngagementState bob = await h.Db.ViewerEngagementStates.SingleAsync(s =>
            s.ViewerUserId == Viewer2
        );
        bob.Should().NotBeNull();
        bob.LastGreetedStreamSessionId.Should().BeNull(); // never greeted

        // After the cooldown elapses, a third first-timer greeting fires again.
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        await h.Sut.OnChatActivityAsync(
            Channel,
            Signal(
                h.Clock.GetUtcNow().UtcDateTime,
                "s1",
                Guid.Parse("0192c000-0000-7000-8000-00000000a003"),
                "333",
                "Cara"
            )
        );
        // Cara is not a seeded User, but the service does not require it — first-time creates the row.
        h.Bus.Published.Should().HaveCount(2);
    }

    [Fact]
    public async Task DisabledTrigger_FiresNothing_ButStateStillUpdates()
    {
        // Only the streak trigger is on. A first-timer's disabled first-time greeting fires nothing, but
        // their state row IS created so the streak can be tracked from their very first stream.
        Harness h = Build();
        SeedConfig(h.Db, firstTime: false, returning: false, streak: true);
        DateTime at = h.Clock.GetUtcNow().UtcDateTime;
        SeedStream(h.Db, "s1", h.Clock.GetUtcNow());

        await h.Sut.OnChatActivityAsync(Channel, Signal(at, "s1"));

        h.Bus.Published.Should().BeEmpty(); // no first-time event (that trigger is off; streak is 1, no milestone)
        ViewerEngagementState state = await h.Db.ViewerEngagementStates.SingleAsync();
        state.ConsecutiveStreams.Should().Be(1);
        state.LastSeenStreamSessionId.Should().Be("s1");
    }

    [Fact]
    public async Task AllTriggersDisabled_ShortCircuits_NoStateNoEvent()
    {
        Harness h = Build();
        SeedConfig(h.Db, firstTime: false, returning: false, streak: false);
        DateTime at = h.Clock.GetUtcNow().UtcDateTime;

        await h.Sut.OnChatActivityAsync(Channel, Signal(at, "s1"));

        h.Bus.Published.Should().BeEmpty();
        // A fully-disabled channel does no work at all (the enabled-flags fast-path).
        (await h.Db.ViewerEngagementStates.CountAsync())
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task NoConfig_ShortCircuits_DefaultDeny()
    {
        Harness h = Build();
        DateTime at = h.Clock.GetUtcNow().UtcDateTime;

        await h.Sut.OnChatActivityAsync(Channel, Signal(at, "s1"));

        h.Bus.Published.Should().BeEmpty();
        (await h.Db.ViewerEngagementStates.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateConfig_PersistsFlagsAndMilestones_GetReturnsThem()
    {
        Harness h = Build();

        await h.Sut.UpdateConfigAsync(
            Channel,
            new UpdateEngagementConfigRequest(true, true, true, [5, 10, 25], 8)
        );

        EngagementConfigDto dto = (await h.Sut.GetConfigAsync(Channel)).Value;
        dto.FirstTimeChatterEnabled.Should().BeTrue();
        dto.ReturningChatterEnabled.Should().BeTrue();
        dto.WatchStreakEnabled.Should().BeTrue();
        dto.StreakMilestones.Should().Equal(5, 10, 25);
        dto.GreetCooldownSeconds.Should().Be(8);
    }

    [Fact]
    public async Task UpdateConfig_RejectsNonPositiveMilestones()
    {
        Harness h = Build();

        NomNomzBot.Application.Common.Models.Result<EngagementConfigDto> result =
            await h.Sut.UpdateConfigAsync(
                Channel,
                new UpdateEngagementConfigRequest(true, false, false, [0, -1], 5)
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task GetConfig_WhenNeverSet_ReturnsAllOffDefaults()
    {
        Harness h = Build();

        EngagementConfigDto dto = (await h.Sut.GetConfigAsync(Channel)).Value;

        dto.FirstTimeChatterEnabled.Should().BeFalse();
        dto.ReturningChatterEnabled.Should().BeFalse();
        dto.WatchStreakEnabled.Should().BeFalse();
        dto.StreakMilestones.Should().BeEmpty();
        dto.GreetCooldownSeconds.Should().Be(5);
    }
}
