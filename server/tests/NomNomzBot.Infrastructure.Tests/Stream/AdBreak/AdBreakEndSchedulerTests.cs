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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream.AdBreak;
using NomNomzBot.Infrastructure.Stream.EventHandlers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.Stream.AdBreak;

/// <summary>
/// Old-bot parity for the ad break end line: once an ad break began, chat hears the end line after the break's
/// duration, once, and only when the same stream is still live. The handler must not hold the event while it waits.
/// </summary>
public sealed class AdBreakEndSchedulerTests
{
    private const string EndKey = "channel.ad_break.end";
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ad01");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000ad02");
    private static readonly DateTimeOffset LiveSince = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BreakStart = LiveSince.AddMinutes(30);

    private sealed class Rig
    {
        public required AdBreakEndScheduler Sut { get; init; }
        public required AdBreakEndScheduleHandler Handler { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required IEventResponseExecutor Executor { get; init; }
        public required ChannelContext Context { get; init; }

        public Task BeginAsync(Guid channel, int seconds) =>
            Handler.HandleAsync(
                new()
                {
                    BroadcasterId = channel,
                    DurationSeconds = seconds,
                    IsAutomatic = true,
                    StartedAt = BreakStart,
                }
            );

        public Task TickAtAsync(int secondsAfterStart, CancellationToken ct = default)
        {
            Clock.SetUtcNow(BreakStart.AddSeconds(secondsAfterStart));
            return Sut.ProcessDueAsync(ct);
        }

        public int EndLines(Guid channel) =>
            Executor
                .ReceivedCalls()
                .Count(c =>
                    c.GetMethodInfo().Name == nameof(IEventResponseExecutor.ExecuteAsync)
                    && (Guid)c.GetArguments()[0]! == channel
                    && (string)c.GetArguments()[1]! == EndKey
                );
    }

    private static ChannelContext Live(Guid id) =>
        new()
        {
            BroadcasterId = id,
            TwitchChannelId = "tw-" + id.ToString("N")[^4..],
            ChannelName = "stoney",
            IsLive = true,
            WentLiveAt = LiveSince,
        };

    private static Rig Build()
    {
        FakeTimeProvider clock = new(BreakStart);
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceCollection services = new();
        services.AddSingleton(executor);
        IServiceScopeFactory scopes = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        ChannelContext context = Live(Channel);
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(context);
        registry.Get(OtherChannel).Returns(Live(OtherChannel));

        AdBreakEndScheduler sut = new(
            scopes,
            registry,
            clock,
            NullLogger<AdBreakEndScheduler>.Instance
        );
        return new()
        {
            Sut = sut,
            Handler = new(sut),
            Clock = clock,
            Executor = executor,
            Context = context,
        };
    }

    [Fact]
    public async Task The_end_line_goes_out_once_when_the_break_duration_has_passed_and_not_before()
    {
        Rig rig = Build();

        await rig.BeginAsync(Channel, 180);
        await rig.TickAtAsync(179);
        rig.EndLines(Channel).Should().Be(0, "the break still runs one second");

        await rig.TickAtAsync(180);
        await rig.TickAtAsync(240);

        rig.EndLines(Channel).Should().Be(1, "the end line goes out once");
        await rig
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                EndKey,
                null,
                null,
                Arg.Is<Dictionary<string, string>>(v =>
                    v.Count == 1 && v["ad.duration"] == "3 minutes"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Handling_the_begin_event_returns_at_once_and_waits_for_nothing()
    {
        Rig rig = Build();

        Task handled = rig.BeginAsync(Channel, 180);

        handled.IsCompletedSuccessfully.Should().BeTrue("the handler only schedules the end line");
        rig.EndLines(Channel).Should().Be(0);
        await handled;
        await rig.TickAtAsync(180);
        rig.EndLines(Channel).Should().Be(1, "the scheduled break is the proof it was handed over");
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_stream_went_offline_during_the_break()
    {
        Rig rig = Build();
        await rig.BeginAsync(Channel, 60);

        rig.Context.IsLive = false;
        await rig.TickAtAsync(60);
        rig.Context.IsLive = true;
        await rig.TickAtAsync(120);

        rig.EndLines(Channel).Should().Be(0, "the dropped break never comes back");
    }

    [Fact]
    public async Task Nothing_is_sent_when_a_new_stream_started_during_the_break()
    {
        Rig rig = Build();
        await rig.BeginAsync(Channel, 60);

        rig.Context.WentLiveAt = LiveSince.AddMinutes(31);
        await rig.TickAtAsync(60);

        rig.EndLines(Channel).Should().Be(0);
    }

    [Fact]
    public async Task A_break_that_begins_while_the_channel_is_offline_is_not_scheduled()
    {
        Rig rig = Build();
        rig.Context.IsLive = false;

        await rig.BeginAsync(Channel, 60);
        rig.Context.IsLive = true;
        await rig.TickAtAsync(60);

        rig.EndLines(Channel).Should().Be(0);
    }

    [Fact]
    public async Task A_cancelled_token_on_shutdown_does_not_throw_and_sends_nothing_more()
    {
        Rig rig = Build();
        using CancellationTokenSource cts = new();
        rig.Executor.ExecuteAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());
        await rig.BeginAsync(Channel, 60);
        await cts.CancelAsync();

        Func<Task> tick = () => rig.TickAtAsync(60, cts.Token);

        await tick.Should().NotThrowAsync();
        rig.EndLines(Channel).Should().Be(1, "the due break was handed to the executor once");
    }

    [Fact]
    public async Task A_failing_send_for_one_channel_does_not_stop_the_other_channels_end_line()
    {
        Rig rig = Build();
        rig.Executor.ExecuteAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("chat is down"));
        await rig.BeginAsync(Channel, 60);
        await rig.BeginAsync(OtherChannel, 60);

        await rig.TickAtAsync(60);

        rig.EndLines(OtherChannel).Should().Be(1);
    }
}
