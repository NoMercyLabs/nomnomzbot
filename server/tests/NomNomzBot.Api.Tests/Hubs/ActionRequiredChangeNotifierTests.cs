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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the action-required live push (plan item A0 slice 3): a burst of signals for one channel becomes ONE
/// <c>ConfigChanged("notifications")</c> push to that channel's base group after the coalesce window, a signal
/// after the push schedules a new one, and channels never share a push.
/// </summary>
public sealed class ActionRequiredChangeNotifierTests
{
    [Fact]
    public async Task ABurstOfSignals_CoalescesIntoOnePush_ForThatChannelOnly()
    {
        (ActionRequiredChangeNotifier sut, IDashboardNotifier hub, FakeTimeProvider clock) =
            Build();
        Guid channel = Guid.CreateVersion7();
        Guid other = Guid.CreateVersion7();

        sut.NotifyChanged(channel);
        sut.NotifyChanged(channel);
        sut.NotifyChanged(channel);
        sut.NotifyChanged(other);
        await hub.DidNotReceiveWithAnyArgs().SendConfigChangedAsync(default!, default!);

        clock.Advance(ActionRequiredChangeNotifier.CoalesceWindow);
        await WaitForPushesAsync(hub, 2);

        await hub.Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(d =>
                    d.BroadcasterId == channel.ToString()
                    && d.Domain == ActionRequiredChangeNotifier.Domain
                    && d.EntityId == null
                ),
                Arg.Any<CancellationToken>()
            );
        await hub.Received(1)
            .SendConfigChangedAsync(
                other.ToString(),
                Arg.Any<ConfigChangedDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ASignalAfterAPush_SchedulesAFreshPush()
    {
        (ActionRequiredChangeNotifier sut, IDashboardNotifier hub, FakeTimeProvider clock) =
            Build();
        Guid channel = Guid.CreateVersion7();

        sut.NotifyChanged(channel);
        clock.Advance(ActionRequiredChangeNotifier.CoalesceWindow);
        await WaitForPushesAsync(hub, 1);

        sut.NotifyChanged(channel);
        clock.Advance(ActionRequiredChangeNotifier.CoalesceWindow);
        await WaitForPushesAsync(hub, 2);

        hub.ReceivedCalls().Should().HaveCount(2);
    }

    private static (ActionRequiredChangeNotifier, IDashboardNotifier, FakeTimeProvider) Build()
    {
        IDashboardNotifier hub = Substitute.For<IDashboardNotifier>();
        ServiceCollection services = new();
        services.AddScoped(_ => hub);
        FakeTimeProvider clock = new();
        ActionRequiredChangeNotifier sut = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<ActionRequiredChangeNotifier>.Instance
        );
        return (sut, hub, clock);
    }

    // The push runs on the timer continuation; wait (bounded) for it to land rather than sleeping blind.
    private static async Task WaitForPushesAsync(IDashboardNotifier hub, int expected)
    {
        for (int attempt = 0; attempt < 200 && hub.ReceivedCalls().Count() < expected; attempt++)
            await Task.Delay(10);
        hub.ReceivedCalls().Should().HaveCount(expected);
    }
}
