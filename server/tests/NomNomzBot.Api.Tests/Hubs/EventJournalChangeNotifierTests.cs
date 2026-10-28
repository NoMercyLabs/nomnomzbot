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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the journal live push: a burst of appends for one channel becomes ONE <c>ConfigChanged("event-journal")</c>
/// push to that channel's group after the coalesce window, and a later append schedules a fresh push.
/// </summary>
public sealed class EventJournalChangeNotifierTests
{
    [Fact]
    public async Task ABurstOfAppends_BecomesOnePush_ForThatChannelOnly()
    {
        (EventJournalChangeNotifier sut, IDashboardNotifier hub, FakeTimeProvider clock) = Build();
        Guid channel = Guid.CreateVersion7();
        Guid other = Guid.CreateVersion7();

        sut.NotifyChanged(channel);
        sut.NotifyChanged(channel);
        sut.NotifyChanged(other);
        await hub.DidNotReceiveWithAnyArgs().SendConfigChangedAsync(default!, default!);

        clock.Advance(EventJournalChangeNotifier.CoalesceWindow);
        await WaitForPushesAsync(hub, 2);

        await hub.Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(d =>
                    d.BroadcasterId == channel.ToString()
                    && d.Domain == "event-journal"
                    && d.EntityId == null
                    && d.Action == "updated"
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
    public async Task AnAppendAfterAPush_SchedulesAFreshPush()
    {
        (EventJournalChangeNotifier sut, IDashboardNotifier hub, FakeTimeProvider clock) = Build();
        Guid channel = Guid.CreateVersion7();

        sut.NotifyChanged(channel);
        clock.Advance(EventJournalChangeNotifier.CoalesceWindow);
        await WaitForPushesAsync(hub, 1);

        sut.NotifyChanged(channel);
        clock.Advance(EventJournalChangeNotifier.CoalesceWindow);
        await WaitForPushesAsync(hub, 2);
    }

    [Fact]
    public async Task TheEmptyChannelId_PushesNothing()
    {
        (EventJournalChangeNotifier sut, IDashboardNotifier hub, FakeTimeProvider clock) = Build();

        sut.NotifyChanged(Guid.Empty);
        clock.Advance(EventJournalChangeNotifier.CoalesceWindow);
        await Task.Delay(50);

        hub.ReceivedCalls().Should().BeEmpty();
    }

    private static (EventJournalChangeNotifier, IDashboardNotifier, FakeTimeProvider) Build()
    {
        IDashboardNotifier hub = Substitute.For<IDashboardNotifier>();
        ServiceCollection services = new();
        services.AddScoped(_ => hub);
        FakeTimeProvider clock = new();
        EventJournalChangeNotifier sut = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<EventJournalChangeNotifier>.Instance
        );
        return (sut, hub, clock);
    }

    private static async Task WaitForPushesAsync(IDashboardNotifier hub, int expected)
    {
        for (int attempt = 0; attempt < 200 && hub.ReceivedCalls().Count() < expected; attempt++)
            await Task.Delay(10);
        hub.ReceivedCalls().Should().HaveCount(expected);
    }
}
