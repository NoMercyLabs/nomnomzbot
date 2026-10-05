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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Stream.AdBreak;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.AdBreak;

/// <summary>
/// Old-bot parity for the ad schedule warning (AdScheduleService): a live channel with the ads scope gets its
/// schedule read, the overlays hear the countdown at each threshold once per next-ad slot, and chat hears one
/// warning at about 3 minutes. An offline channel, or one without the scope, is left alone.
/// </summary>
public sealed class AdScheduleWarnerTests
{
    private const string UpcomingKey = "channel.ad_break.upcoming";
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ad11");
    private static readonly DateTimeOffset LiveSince = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = LiveSince.AddMinutes(30);
    private static readonly DateTimeOffset NextAd = Start.AddMinutes(20);

    private sealed class RecordingBus : IEventBus
    {
        public List<IDomainEvent> Events { get; } = [];

        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, IDomainEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Events.Add(@event);
    }

    private sealed class Rig
    {
        public required AdScheduleWarner Sut { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required RecordingBus Bus { get; init; }
        public required IEventResponseExecutor Executor { get; init; }
        public required ITwitchAdsApi Ads { get; init; }
        public required ITwitchTokenResolver Tokens { get; init; }
        public required ChannelContext Context { get; init; }
        public DateTimeOffset CurrentNextAd { get; set; } = NextAd;

        /// <summary>Moves the clock to <paramref name="secondsLeft"/> before the next ad and runs one pass.</summary>
        public Task TickAsync(int secondsLeft)
        {
            Clock.SetUtcNow(CurrentNextAd.AddSeconds(-secondsLeft));
            return Sut.ProcessAsync(CancellationToken.None);
        }

        public List<AdBreakUpcomingEvent> Upcoming =>
            [.. Bus.Events.OfType<AdBreakUpcomingEvent>()];

        public List<AdScheduleUpdatedEvent> Schedules =>
            [.. Bus.Events.OfType<AdScheduleUpdatedEvent>()];

        public int ChatWarnings =>
            Executor
                .ReceivedCalls()
                .Count(c =>
                    c.GetMethodInfo().Name == nameof(IEventResponseExecutor.ExecuteAsync)
                    && (Guid)c.GetArguments()[0]! == Channel
                    && (string)c.GetArguments()[1]! == UpcomingKey
                );

        public int Polls =>
            Ads.ReceivedCalls()
                .Count(c => c.GetMethodInfo().Name == nameof(ITwitchAdsApi.GetAdScheduleAsync));

        public void SetNextAd(DateTimeOffset when, int duration = 90)
        {
            CurrentNextAd = when;
            Ads.GetAdScheduleAsync(Channel, Arg.Any<CancellationToken>())
                .Returns(
                    Result.Success(
                        new TwitchAdSchedule(
                            3,
                            (int)when.AddMinutes(30).ToUnixTimeSeconds(),
                            (int)when.ToUnixTimeSeconds(),
                            duration,
                            (int)when.AddMinutes(-60).ToUnixTimeSeconds(),
                            0
                        )
                    )
                );
        }
    }

    private static Rig Build(bool hasScope = true)
    {
        FakeTimeProvider clock = new(Start);
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ITwitchAdsApi ads = Substitute.For<ITwitchAdsApi>();
        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.Ads.Returns(ads);
        ITwitchTokenResolver tokens = Substitute.For<ITwitchTokenResolver>();
        tokens
            .HasScopeAsync(Channel, TwitchScopes.ChannelReadAds, Arg.Any<CancellationToken>())
            .Returns(hasScope);
        ServiceCollection services = new();
        services.AddSingleton(executor);
        services.AddSingleton(helix);
        services.AddSingleton(tokens);
        IServiceScopeFactory scopes = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        ChannelContext context = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-ad11",
            ChannelName = "stoney",
            IsLive = true,
            WentLiveAt = LiveSince,
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.GetLiveChannels().Returns(_ => context.IsLive ? [context] : []);

        RecordingBus bus = new();
        AdScheduleWarner sut = new(
            scopes,
            registry,
            bus,
            clock,
            NullLogger<AdScheduleWarner>.Instance
        );
        Rig rig = new()
        {
            Sut = sut,
            Clock = clock,
            Bus = bus,
            Executor = executor,
            Ads = ads,
            Tokens = tokens,
            Context = context,
        };
        rig.SetNextAd(NextAd);
        return rig;
    }

    [Theory]
    [InlineData(180, "in ~3 minutes", "90")]
    [InlineData(150, "in ~2 minutes", "90")]
    [InlineData(60, "in ~1 minute", "90")]
    [InlineData(45, "in ~1 minute", "90")]
    [InlineData(29, "in ~29 seconds", "90")]
    public async Task Chat_gets_the_old_bots_warning_wording_for_the_time_that_is_left(
        int secondsLeft,
        string when,
        string length
    )
    {
        Rig rig = Build();

        await rig.TickAsync(secondsLeft);

        rig.ChatWarnings.Should().Be(1);
        await rig
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                UpcomingKey,
                null,
                null,
                Arg.Is<Dictionary<string, string>>(v =>
                    v.Count == 2 && v["ad.when"] == when && v["ad.seconds"] == length
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Chat_hears_nothing_before_three_minutes_and_one_warning_per_slot()
    {
        Rig rig = Build();

        await rig.TickAsync(300);
        rig.ChatWarnings.Should().Be(0, "the ad is more than 3 minutes away");

        await rig.TickAsync(181);
        rig.ChatWarnings.Should().Be(0, "181 seconds is still above the 3 minute mark");

        await rig.TickAsync(180);
        await rig.TickAsync(120);
        await rig.TickAsync(60);
        await rig.TickAsync(10);
        rig.ChatWarnings.Should().Be(1, "chat is told once per next-ad slot");
    }

    [Fact]
    public async Task A_snooze_that_moves_the_next_ad_arms_the_warning_again()
    {
        Rig rig = Build();
        await rig.TickAsync(170);
        rig.ChatWarnings.Should().Be(1);

        rig.SetNextAd(NextAd.AddMinutes(5));
        await rig.TickAsync(170);

        rig.ChatWarnings.Should().Be(2, "the new slot gets its own warning");
        rig.Upcoming.Count(u => u.ThresholdSeconds == 300)
            .Should()
            .Be(2, "the new slot also re-arms the overlay thresholds");
    }

    [Fact]
    public async Task The_overlays_get_each_threshold_once_with_the_old_bots_fields()
    {
        Rig rig = Build();

        foreach (int left in new[] { 400, 300, 299, 120, 119, 60, 30, 10, 5 })
            await rig.TickAsync(left);

        rig.Upcoming.Select(u => u.ThresholdSeconds)
            .Should()
            .Equal([300, 120, 60, 30, 10], "each threshold goes out once, in order");
        AdBreakUpcomingEvent first = rig.Upcoming[0];
        first.BroadcasterId.Should().Be(Channel);
        first.SecondsUntilAd.Should().Be(300);
        first.DurationSeconds.Should().Be(90);
        first.NextAdAt.Should().Be(NextAd);
        rig.Upcoming[4].SecondsUntilAd.Should().Be(10);
    }

    [Fact]
    public async Task A_channel_first_seen_close_to_the_ad_gets_every_crossed_threshold_at_once()
    {
        Rig rig = Build();

        await rig.TickAsync(25);

        rig.Upcoming.Select(u => u.ThresholdSeconds)
            .Should()
            .Equal([300, 120, 60, 30], "the old bot fired each threshold that was already crossed");
        rig.Upcoming.Should().OnlyContain(u => u.SecondsUntilAd == 25);
    }

    [Fact]
    public async Task The_schedule_event_carries_the_old_bots_fields_on_every_poll()
    {
        Rig rig = Build();

        await rig.TickAsync(400);

        AdScheduleUpdatedEvent schedule = rig.Schedules.Should().ContainSingle().Subject;
        schedule.BroadcasterId.Should().Be(Channel);
        schedule.NextAdAt.Should().Be(NextAd);
        schedule.LastAdAt.Should().Be(NextAd.AddMinutes(-60));
        schedule.DurationSeconds.Should().Be(90);
        schedule.PrerollFreeTimeSeconds.Should().Be(0);
        schedule.SnoozeCount.Should().Be(3);
        schedule.SnoozeRefreshAt.Should().Be(NextAd.AddMinutes(30));
        schedule.TimeUntilNextAdSeconds.Should().Be(400);
    }

    [Fact]
    public async Task The_schedule_is_read_once_a_minute_and_the_thresholds_follow_the_clock_in_between()
    {
        Rig rig = Build();

        await rig.TickAsync(75);
        await rig.TickAsync(70);
        await rig.TickAsync(65);
        rig.Polls.Should().Be(1, "the second and third pass are inside the minute");

        await rig.TickAsync(12);
        rig.Polls.Should().Be(2, "a minute later the schedule is read again");

        await rig.TickAsync(10);
        rig.Upcoming.Select(u => u.ThresholdSeconds).Should().Contain(10);
        rig.Upcoming.Count(u => u.ThresholdSeconds == 10).Should().Be(1);
    }

    [Fact]
    public async Task Nothing_happens_for_an_offline_channel()
    {
        Rig rig = Build();
        rig.Context.IsLive = false;

        await rig.TickAsync(170);

        rig.Polls.Should().Be(0);
        rig.Bus.Events.Should().BeEmpty();
        rig.ChatWarnings.Should().Be(0);
    }

    [Fact]
    public async Task A_new_stream_starts_with_fresh_slots_and_a_fresh_poll()
    {
        Rig rig = Build();
        await rig.TickAsync(170);
        rig.ChatWarnings.Should().Be(1);

        rig.Context.WentLiveAt = LiveSince.AddMinutes(25);
        await rig.TickAsync(169);

        rig.ChatWarnings.Should().Be(2, "a new stream is a new slot");
        rig.Polls.Should().Be(2, "the new stream is read at once");
    }

    [Fact]
    public async Task A_channel_without_the_ads_scope_is_never_polled_and_hears_nothing()
    {
        Rig rig = Build(hasScope: false);

        await rig.TickAsync(170);
        await rig.TickAsync(60);

        rig.Polls.Should().Be(0, "without the scope no Helix call goes out");
        rig.Bus.Events.Should().BeEmpty();
        rig.ChatWarnings.Should().Be(0);
    }

    [Fact]
    public async Task A_failed_schedule_read_is_quiet_and_tries_again_next_minute()
    {
        Rig rig = Build();
        rig.Ads.GetAdScheduleAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TwitchAdSchedule>("Twitch is down.", TwitchErrorCodes.NotFound)
            );

        Func<Task> pass = () => rig.TickAsync(170);

        await pass.Should().NotThrowAsync();
        rig.Bus.Events.Should().BeEmpty();
        rig.ChatWarnings.Should().Be(0);

        rig.SetNextAd(NextAd);
        await rig.TickAsync(100);
        rig.ChatWarnings.Should().Be(1, "the next read worked, so the warning goes out then");
    }

    [Fact]
    public async Task No_ad_scheduled_means_no_warning()
    {
        Rig rig = Build();
        rig.Ads.GetAdScheduleAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TwitchAdSchedule(3, 0, 0, 0, 0, 0)));

        await rig.TickAsync(170);

        rig.Upcoming.Should().BeEmpty();
        rig.ChatWarnings.Should().Be(0);
        AdScheduleUpdatedEvent schedule = rig.Schedules.Should().ContainSingle().Subject;
        schedule.NextAdAt.Should().BeNull();
        schedule.TimeUntilNextAdSeconds.Should().BeNull();
    }
}
