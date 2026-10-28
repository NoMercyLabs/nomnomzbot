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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Obs.Recovery;
using NomNomzBot.Infrastructure.Tests.Seeding;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves when a recovery pass is asked for (a widget disconnect, OBS coming up, a bridge page joining) and that
/// the scheduler waits out the reconnect window and collapses a burst of asks into one pass.
/// </summary>
public sealed class ObsBrowserSourceRecoveryTriggerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f921");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f922");

    [Fact]
    public async Task A_widget_disconnect_schedules_a_pass_for_its_channel()
    {
        IObsBrowserSourceRecoveryScheduler scheduler =
            Substitute.For<IObsBrowserSourceRecoveryScheduler>();

        await new WidgetDisconnectedRecoveryTrigger(scheduler).HandleAsync(
            new WidgetDisconnectedEvent
            {
                BroadcasterId = Channel,
                WidgetId = Guid.CreateVersion7(),
                ConnectionId = "c1",
            }
        );

        scheduler.Received(1).Schedule(Channel);
    }

    [Fact]
    public async Task OBS_coming_up_schedules_a_pass_for_its_channel()
    {
        IObsBrowserSourceRecoveryScheduler scheduler =
            Substitute.For<IObsBrowserSourceRecoveryScheduler>();

        await new ObsConnectedRecoveryTrigger(scheduler).HandleAsync(
            new ObsConnectionEstablishedEvent
            {
                BroadcasterId = Channel,
                Streaming = false,
                Recording = false,
            }
        );

        scheduler.Received(1).Schedule(Channel);
    }

    [Fact]
    public async Task A_bridge_page_joining_schedules_a_pass_but_the_last_one_leaving_does_not()
    {
        IObsBrowserSourceRecoveryScheduler scheduler =
            Substitute.For<IObsBrowserSourceRecoveryScheduler>();
        ObsBridgeJoinedRecoveryTrigger trigger = new(scheduler);

        await trigger.HandleAsync(
            new ObsBridgeStateChangedEvent
            {
                BroadcasterId = Channel,
                InstanceCount = 0,
                HasLeader = false,
            }
        );
        scheduler.DidNotReceive().Schedule(Arg.Any<Guid>());

        await trigger.HandleAsync(
            new ObsBridgeStateChangedEvent
            {
                BroadcasterId = Channel,
                InstanceCount = 1,
                HasLeader = true,
            }
        );
        scheduler.Received(1).Schedule(Channel);
    }

    private sealed class SchedulerRig
    {
        public required ObsBrowserSourceRecoveryScheduler Scheduler { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required List<Guid> Passes { get; init; }
        public required SemaphoreSlim PassRan { get; init; }
    }

    private static SchedulerRig BuildScheduler(
        Result<IReadOnlyList<string>>? passResult = null,
        ILogger<ObsBrowserSourceRecoveryScheduler>? logger = null
    )
    {
        List<Guid> passes = [];
        SemaphoreSlim passRan = new(0);
        IObsBrowserSourceRecoveryService recovery =
            Substitute.For<IObsBrowserSourceRecoveryService>();
        recovery
            .RecoverAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (passes)
                    passes.Add(call.ArgAt<Guid>(0));
                passRan.Release();
                return Task.FromResult(passResult ?? Result.Success<IReadOnlyList<string>>([]));
            });

        ServiceCollection services = new();
        services.AddScoped(_ => recovery);
        FakeTimeProvider clock = new();
        return new()
        {
            Scheduler = new(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                clock,
                logger ?? NullLogger<ObsBrowserSourceRecoveryScheduler>.Instance
            ),
            Clock = clock,
            Passes = passes,
            PassRan = passRan,
        };
    }

    [Fact]
    public async Task The_pass_runs_once_after_the_reconnect_window_and_not_before()
    {
        SchedulerRig rig = BuildScheduler();

        rig.Scheduler.Schedule(Channel);
        rig.Clock.Advance(ObsBrowserSourceRecoveryScheduler.Delay - TimeSpan.FromMilliseconds(1));
        await Task.Delay(100);
        rig.Passes.Should().BeEmpty();

        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        (await rig.PassRan.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        rig.Passes.Should().Equal(Channel);
    }

    [Fact]
    public async Task A_burst_of_asks_for_one_channel_collapses_into_one_pass_after_the_last()
    {
        SchedulerRig rig = BuildScheduler();
        TimeSpan half = ObsBrowserSourceRecoveryScheduler.Delay / 2;

        rig.Scheduler.Schedule(Channel);
        rig.Clock.Advance(half);
        rig.Scheduler.Schedule(Channel);
        rig.Clock.Advance(half);
        await Task.Delay(100);
        rig.Passes.Should().BeEmpty("the second ask restarted the window");

        rig.Clock.Advance(half);
        (await rig.PassRan.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await Task.Delay(100);

        rig.Passes.Should().Equal(Channel);
    }

    [Fact]
    public async Task A_pass_that_cannot_reach_obs_logs_one_warning_naming_the_channel_and_the_reason()
    {
        ListLogger<ObsBrowserSourceRecoveryScheduler> log = new();
        SchedulerRig rig = BuildScheduler(
            Result.Failure<IReadOnlyList<string>>("OBS is not connected", "OBS_NOT_CONNECTED"),
            log
        );

        rig.Scheduler.Schedule(Channel);
        rig.Clock.Advance(ObsBrowserSourceRecoveryScheduler.Delay);
        (await rig.PassRan.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await Task.Delay(100);

        log.Entries.Should()
            .ContainSingle(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains(Channel.ToString())
                && e.Message.Contains("OBS is not connected")
            );
    }

    [Fact]
    public async Task A_pass_that_reaches_obs_logs_no_warning()
    {
        ListLogger<ObsBrowserSourceRecoveryScheduler> log = new();
        SchedulerRig rig = BuildScheduler(logger: log);

        rig.Scheduler.Schedule(Channel);
        rig.Clock.Advance(ObsBrowserSourceRecoveryScheduler.Delay);
        (await rig.PassRan.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await Task.Delay(100);

        log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Two_channels_each_get_their_own_pass()
    {
        SchedulerRig rig = BuildScheduler();

        rig.Scheduler.Schedule(Channel);
        rig.Scheduler.Schedule(OtherChannel);
        rig.Clock.Advance(ObsBrowserSourceRecoveryScheduler.Delay);
        (await rig.PassRan.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        (await rig.PassRan.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        rig.Passes.Should().BeEquivalentTo([Channel, OtherChannel]);
    }
}
