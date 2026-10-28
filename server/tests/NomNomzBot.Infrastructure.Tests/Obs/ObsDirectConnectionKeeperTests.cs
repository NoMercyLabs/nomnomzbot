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

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Infrastructure.Obs.Transport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FakeObsSocket = NomNomzBot.Infrastructure.Tests.Obs.DirectObsTransportTests.FakeObsSocket;
using Harness = NomNomzBot.Infrastructure.Tests.Obs.DirectObsTransportTests.Harness;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// S2 defect A: a direct-mode channel must receive OBS events without a first command, and again after
/// OBS drops the socket. The keeper dials every enabled direct channel and retries a failed dial with a
/// doubling backoff, so a closed OBS is not hammered.
/// </summary>
public sealed class ObsDirectConnectionKeeperTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f701");

    private static ObsDirectConnectionKeeper Keeper(Harness h, FakeTimeProvider clock)
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(h.Db);
        IServiceScopeFactory scopes = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new(h.Transport, scopes, clock, NullLogger<ObsDirectConnectionKeeper>.Instance);
    }

    [Fact]
    public async Task A_sweep_dials_an_enabled_direct_channel_and_its_events_flow_without_any_command()
    {
        Harness h = DirectObsTransportTests.Build();
        DirectObsTransportTests.QueueHandshake(h.Socket);
        ObsDirectConnectionKeeper keeper = Keeper(h, new());

        await keeper.SweepAsync(CancellationToken.None);

        h.Transport.IsConnected(Channel).Should().BeTrue();
        JsonElement identify = JsonDocument.Parse(h.Socket.Sent[0]).RootElement;
        identify.GetProperty("op").GetInt32().Should().Be(1);

        h.Socket.QueueIncoming(
            """{ "op": 5, "d": { "eventType": "CurrentProgramSceneChanged", "eventData": { "sceneName": "Game" } } }"""
        );
        ObsEventReceivedEvent received = await WaitForAsync(() =>
            h.Bus.Published.OfType<ObsEventReceivedEvent>().FirstOrDefault()
        );
        received.ObsEventType.Should().Be("CurrentProgramSceneChanged");
        received.BroadcasterId.Should().Be(Channel);
    }

    [Fact]
    public async Task A_dropped_session_is_redialed_on_the_next_sweep()
    {
        Harness h = DirectObsTransportTests.Build();
        DirectObsTransportTests.QueueHandshake(h.Socket);
        ObsDirectConnectionKeeper keeper = Keeper(h, new());
        await keeper.SweepAsync(CancellationToken.None);
        h.Transport.IsConnected(Channel).Should().BeTrue();

        FakeObsSocket second = new();
        DirectObsTransportTests.QueueHandshake(second);
        h.Factory.ConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IObsSocket>(second));
        h.Socket.QueueClose();
        await WaitForAsync(() => h.Transport.IsConnected(Channel) ? null : "dropped");

        await keeper.SweepAsync(CancellationToken.None);

        h.Transport.IsConnected(Channel).Should().BeTrue();
        second.Sent.Should().NotBeEmpty("the redial identifies on the new socket");
    }

    [Fact]
    public async Task A_failed_dial_backs_off_doubling_before_the_next_attempt()
    {
        Harness h = DirectObsTransportTests.Build();
        h.Factory.ConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("OBS is closed"));
        FakeTimeProvider clock = new();
        ObsDirectConnectionKeeper keeper = Keeper(h, clock);

        await keeper.SweepAsync(CancellationToken.None); // attempt 1, next after 2 s
        clock.Advance(TimeSpan.FromSeconds(1));
        await keeper.SweepAsync(CancellationToken.None); // not due
        clock.Advance(TimeSpan.FromSeconds(1));
        await keeper.SweepAsync(CancellationToken.None); // attempt 2, next after 4 s
        clock.Advance(TimeSpan.FromSeconds(3));
        await keeper.SweepAsync(CancellationToken.None); // not due
        clock.Advance(TimeSpan.FromSeconds(1));
        await keeper.SweepAsync(CancellationToken.None); // attempt 3

        await h.Factory.Received(3).ConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
        h.Transport.IsConnected(Channel).Should().BeFalse();
    }

    [Fact]
    public async Task A_bridge_mode_or_disabled_channel_is_never_dialed()
    {
        Harness bridge = DirectObsTransportTests.Build(mode: "bridge");
        Harness disabled = DirectObsTransportTests.Build(enabled: false);

        await Keeper(bridge, new()).SweepAsync(CancellationToken.None);
        await Keeper(disabled, new()).SweepAsync(CancellationToken.None);

        await bridge.Factory.DidNotReceiveWithAnyArgs().ConnectAsync(default!, default);
        await disabled.Factory.DidNotReceiveWithAnyArgs().ConnectAsync(default!, default);
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> probe)
        where T : class
    {
        for (int i = 0; i < 750; i++)
        {
            if (probe() is { } hit)
                return hit;
            await Task.Delay(20);
        }
        throw new Xunit.Sdk.XunitException($"Timed out waiting for {typeof(T).Name}.");
    }
}
