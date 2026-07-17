// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Domain.Vts.Entities;
using NomNomzBot.Infrastructure.Obs.Bridge;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Vts.Transport;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Vts;

/// <summary>
/// Proves the shared-relay VTS leg (vtube-studio.md D1): a bridge-mode command rides the SAME
/// election/pusher/command-book as OBS with a <c>vts_request</c> payload kind and hands back the raw
/// data JSON; no leader → <c>VTS_BRIDGE_OFFLINE</c> and nothing pushes; and the Mode router selects
/// bridge vs direct off the stored row, failing closed <c>VTS_DISABLED</c> without one.
/// </summary>
public sealed class VtsBridgeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000fc01");
    private static readonly DateTime T0 = new(2026, 7, 17, 15, 0, 0, DateTimeKind.Utc);

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
    public async Task A_bridge_command_rides_the_shared_relay_with_the_vts_kind_and_returns_raw_data()
    {
        ObsBridgeRegistry registry = new(
            new FakeCache(),
            new RecordingEventBus(),
            new FakeTimeProvider()
        );
        await registry.RegisterAsync(Channel, "leader-conn", T0);

        ObsBridgeCommandBook commands = new();
        IObsBridgePusher pusher = Substitute.For<IObsBridgePusher>();
        string? pushedPayload = null;
        pusher
            .PushExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                pushedPayload = ci.ArgAt<string>(2);
                commands.Complete(
                    ci.ArgAt<Guid>(1),
                    new ObsBridgeAck(true, """{ "modelID": "m1" }""", null)
                );
                return Task.CompletedTask;
            });

        BridgeVtsTransport transport = new(registry, pusher, commands, new FakeTimeProvider());
        Result<string> result = await transport.RequestAsync(
            Channel,
            "ModelLoadRequest",
            """{ "modelID": "m1" }"""
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Contain("m1");
        JsonElement payload = JsonDocument.Parse(pushedPayload!).RootElement;
        payload.GetProperty("kind").GetString().Should().Be("vts_request");
        payload.GetProperty("requestType").GetString().Should().Be("ModelLoadRequest");
    }

    [Fact]
    public async Task No_leader_is_VTS_BRIDGE_OFFLINE_and_nothing_pushes()
    {
        IObsBridgePusher pusher = Substitute.For<IObsBridgePusher>();
        BridgeVtsTransport transport = new(
            new ObsBridgeRegistry(new FakeCache(), new RecordingEventBus(), new FakeTimeProvider()),
            pusher,
            new ObsBridgeCommandBook(),
            new FakeTimeProvider()
        );

        Result<string> result = await transport.RequestAsync(Channel, "ModelLoadRequest", "{}");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VTS_BRIDGE_OFFLINE");
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
    public async Task The_router_selects_by_mode_and_fails_closed_when_disabled()
    {
        VtsTestDbContext db = VtsTestDbContext.New();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        // Bridge mode with no leader: the router reaches the BRIDGE transport (its offline code).
        db.VtsConnections.Add(
            new VtsConnection
            {
                BroadcasterId = Channel,
                Mode = "bridge",
                IsEnabled = true,
            }
        );
        db.SaveChanges();

        BridgeVtsTransport bridge = new(
            new ObsBridgeRegistry(new FakeCache(), new RecordingEventBus(), new FakeTimeProvider()),
            Substitute.For<IObsBridgePusher>(),
            new ObsBridgeCommandBook(),
            new FakeTimeProvider()
        );
        DirectVtsTransport direct = new(
            Substitute.For<NomNomzBot.Infrastructure.Obs.Transport.IObsSocketFactory>(),
            scopeFactory,
            new RecordingEventBus(),
            new FakeTimeProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DirectVtsTransport>.Instance
        );
        VtsTransportRouter router = new(direct, bridge, scopeFactory);

        Result<string> viaBridge = await router.RequestAsync(Channel, "ModelLoadRequest", "{}");
        viaBridge.ErrorCode.Should().Be("VTS_BRIDGE_OFFLINE", "bridge mode routed to the bridge");

        // No row for another channel: fail closed before any transport.
        Result<string> disabled = await router.RequestAsync(
            Guid.NewGuid(),
            "ModelLoadRequest",
            "{}"
        );
        disabled.ErrorCode.Should().Be("VTS_DISABLED");
    }
}
