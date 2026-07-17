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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Infrastructure.Obs.Bridge;
using NomNomzBot.Infrastructure.Obs.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves the bridge half (obs-control.md §3.3/D2/D3/§6): the LONGEST-LIVED bridge is the leader and
/// losing it promotes the next-oldest (never a newcomer steal); every join/leave publishes the bridge
/// state event; a command with no leader online fails <c>OBS_BRIDGE_OFFLINE</c> without pushing
/// anything, while with a leader it pushes to exactly that connection and the ack settles the caller;
/// and an inbound OBS event dispatches its bound responses under <c>obs.&lt;EventType&gt;</c> with
/// the flat fields as <c>{obs.event.*}</c> vars.
/// </summary>
public sealed class ObsBridgeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f901");
    private static readonly DateTime T0 = new(2026, 7, 17, 14, 0, 0, DateTimeKind.Utc);

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object> _store = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? value) ? (T?)value : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _store[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }

    [Fact]
    public async Task The_longest_lived_bridge_leads_and_losing_it_promotes_the_next_oldest()
    {
        RecordingEventBus bus = new();
        ObsBridgeRegistry registry = new(new FakeCache(), bus, new FakeTimeProvider());

        await registry.RegisterAsync(Channel, "conn-old", T0);
        await registry.RegisterAsync(Channel, "conn-new", T0.AddMinutes(5));

        (await registry.GetLeaderAsync(Channel)).Should().Be("conn-old", "oldest wins");
        ObsBridgeStatusDto status = await registry.GetStatusAsync(Channel);
        status.InstanceCount.Should().Be(2);
        status.LeaderSince.Should().Be(T0);

        await registry.UnregisterAsync(Channel, "conn-old");
        (await registry.GetLeaderAsync(Channel))
            .Should()
            .Be("conn-new", "losing the leader promotes the next-oldest");

        await registry.UnregisterAsync(Channel, "conn-new");
        (await registry.GetLeaderAsync(Channel)).Should().BeNull();

        bus.Published.OfType<ObsBridgeStateChangedEvent>()
            .Should()
            .HaveCount(4, "every join/leave announces the fleet state");
        bus.Published.OfType<ObsBridgeStateChangedEvent>().Last().HasLeader.Should().BeFalse();
    }

    [Fact]
    public async Task A_command_without_a_leader_is_OBS_BRIDGE_OFFLINE_and_pushes_nothing()
    {
        IObsBridgePusher pusher = Substitute.For<IObsBridgePusher>();
        BridgeObsTransport transport = new(
            new ObsBridgeRegistry(new FakeCache(), new RecordingEventBus(), new FakeTimeProvider()),
            pusher,
            new ObsBridgeCommandBook(),
            new FakeTimeProvider()
        );

        Result<ObsResponse> result = await transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new ObsRequest("GetVersion", null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("OBS_BRIDGE_OFFLINE");
        await pusher
            .DidNotReceive()
            .PushExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_command_pushes_to_exactly_the_leader_and_the_ack_settles_it()
    {
        ObsBridgeRegistry registry = new(
            new FakeCache(),
            new RecordingEventBus(),
            new FakeTimeProvider()
        );
        await registry.RegisterAsync(Channel, "leader-conn", T0);
        await registry.RegisterAsync(Channel, "spare-conn", T0.AddMinutes(1));

        ObsBridgeCommandBook commands = new();
        IObsBridgePusher pusher = Substitute.For<IObsBridgePusher>();
        Guid pushedCommand = Guid.Empty;
        pusher
            .PushExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                pushedCommand = ci.ArgAt<Guid>(1);
                // The bridge acks out-of-band, like the hub would.
                commands.Complete(
                    pushedCommand,
                    new ObsResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null)
                );
                return Task.CompletedTask;
            });

        BridgeObsTransport transport = new(registry, pusher, commands, new FakeTimeProvider());
        Result<ObsResponse> result = await transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new ObsRequest("GetVersion", null)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Ok.Should().BeTrue();
        await pusher
            .Received(1)
            .PushExecuteAsync(
                "leader-conn",
                pushedCommand,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        // A duplicate ack for a settled id is a no-op (idempotent CommandId).
        commands.Complete(pushedCommand, new ObsResponse(false, null, "late")).Should().BeFalse();
    }

    [Fact]
    public async Task An_obs_event_dispatches_its_bound_responses_with_the_event_vars()
    {
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ObsEventTriggerSource source = new(
            executor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ObsEventTriggerSource>.Instance
        );

        await source.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = Channel,
                ObsEventType = "CurrentProgramSceneChanged",
                DataJson = """{ "sceneName": "Live", "sceneUuid": "abc" }""",
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "obs.CurrentProgramSceneChanged",
                null,
                string.Empty,
                Arg.Is<Dictionary<string, string>>(v =>
                    v["obs.event.sceneName"] == "Live"
                    && v["obs.event.type"] == "CurrentProgramSceneChanged"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
