// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// obs-control.md §4/§7 — on connect, <see cref="OBSRelayHub"/> must hand the bridge everything its LOCAL OBS
/// leg needs to open the right socket: the channel's OBS-WS password (already covered) AND the channel's
/// configured port. Before this the bridge page hardcoded <c>ws://127.0.0.1:4455</c> — a streamer who moved
/// OBS-WS off its default port could never be reached, silently, with the bridge reporting "connected" while
/// every local command failed "OBS connection closed". <see cref="OBSRelayHub.OnConnectedAsync"/> resolves the
/// row by <c>BridgeToken</c>, so a non-default <see cref="ObsConnection.Port"/> must ride the SAME
/// <c>SetObsCredentials</c> push as the password.
/// </summary>
public sealed class OBSRelayHubTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000ff0b5");
    private const string Token = "obs-bridge-token";

    private sealed record Fixture(
        OBSRelayHub Hub,
        IOBSRelayClient Caller,
        HubCallerContext Context
    );

    private static Fixture Build(ObsRelayHubTestDbContext db, IObsConnectionService connections)
    {
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("bridge-conn");
        DefaultHttpContext http = new();
        http.Request.QueryString = new("?token=" + Token);
        context.GetHttpContext().Returns(http);

        IObsBridgeRegistry registry = Substitute.For<IObsBridgeRegistry>();
        IOBSRelayClient caller = Substitute.For<IOBSRelayClient>();
        IHubCallerClients<IOBSRelayClient> clients = Substitute.For<
            IHubCallerClients<IOBSRelayClient>
        >();
        clients.Caller.Returns(caller);

        OBSRelayHub hub = new(
            db,
            registry,
            connections,
            new(),
            Substitute.For<IEventBus>(),
            new FakeTimeProvider(),
            NullLogger<OBSRelayHub>.Instance
        )
        {
            Context = context,
            Clients = clients,
        };
        return new(hub, caller, context);
    }

    [Fact]
    public async Task OnConnectedAsync_delivers_the_channels_configured_port_alongside_the_password()
    {
        using ObsRelayHubTestDbContext db = ObsRelayHubTestDbContext.New();
        db.ObsConnections.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                Mode = "bridge",
                BridgeToken = Token,
                IsEnabled = true,
                Port = 4456, // a streamer who moved OBS-WS off the 4455 default
            }
        );
        await db.SaveChangesAsync();

        IObsConnectionService connections = Substitute.For<IObsConnectionService>();
        connections
            .GetPasswordForTransportAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        Fixture f = Build(db, connections);

        await f.Hub.OnConnectedAsync();

        await f.Caller.Received(1).SetObsCredentials(null, 4456);
    }

    [Fact]
    public async Task OnConnectedAsync_falls_back_to_the_ObsWs_default_port_when_none_is_configured()
    {
        using ObsRelayHubTestDbContext db = ObsRelayHubTestDbContext.New();
        db.ObsConnections.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                Mode = "bridge",
                BridgeToken = Token,
                IsEnabled = true,
                Port = null,
            }
        );
        await db.SaveChangesAsync();

        IObsConnectionService connections = Substitute.For<IObsConnectionService>();
        connections
            .GetPasswordForTransportAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        Fixture f = Build(db, connections);

        await f.Hub.OnConnectedAsync();

        await f.Caller.Received(1).SetObsCredentials(null, 4455);
    }
}
