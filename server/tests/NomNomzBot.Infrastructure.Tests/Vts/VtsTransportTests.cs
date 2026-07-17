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
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Domain.Vts.Entities;
using NomNomzBot.Domain.Vts.Events;
using NomNomzBot.Infrastructure.Obs.Transport;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Vts;
using NomNomzBot.Infrastructure.Vts.Transport;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Vts;

/// <summary>
/// Proves the direct VTS leg (vtube-studio.md §0 D1/D2): a session replays the STORED plugin token
/// as an AuthenticationRequest before anything else and subscribes the masked event set; a rejected
/// token fails closed VTS_UNAUTHORIZED (no control request ever leaves); requests correlate by
/// requestID with an APIError surfacing as the failure; inbound *Event frames publish
/// <see cref="VtsEventReceived"/>; and the one-time authorize flow stores the granted token sealed
/// (a denial stores nothing).
/// </summary>
public sealed class VtsTransportTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000fb01");

    private sealed class FakeVtsSocket : IObsSocket
    {
        private readonly Channel<string?> _incoming =
            System.Threading.Channels.Channel.CreateUnbounded<string?>();
        private readonly Lock _gate = new();
        private readonly List<string> _sent = [];

        public IReadOnlyList<string> Sent
        {
            get
            {
                lock (_gate)
                    return [.. _sent];
            }
        }

        public void QueueIncoming(string frame) => _incoming.Writer.TryWrite(frame);

        public Task SendAsync(string frameJson, CancellationToken ct)
        {
            lock (_gate)
                _sent.Add(frameJson);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken ct) =>
            await _incoming.Reader.ReadAsync(ct);

        public ValueTask DisposeAsync()
        {
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        /// <summary>Waits for the next sent frame of the given messageType and returns its requestID.</summary>
        public async Task<string> ReplyToAsync(
            string messageType,
            string replyMessageType,
            string replyDataJson
        )
        {
            string frame = await WaitForAsync(() =>
                Sent.FirstOrDefault(f => f.Contains($"\"messageType\":\"{messageType}\""))
            );
            string requestId = JsonDocument
                .Parse(frame)
                .RootElement.GetProperty("requestID")
                .GetString()!;
            QueueIncoming(
                $$"""{ "apiName": "VTubeStudioPublicAPI", "requestID": "{{requestId}}", "messageType": "{{replyMessageType}}", "data": {{replyDataJson}} }"""
            );
            return requestId;
        }
    }

    private sealed class Harness
    {
        public required DirectVtsTransport Transport { get; init; }
        public required FakeVtsSocket Socket { get; init; }
        public required RecordingEventBus Bus { get; init; }
        public required VtsTestDbContext Db { get; init; }
        public required IServiceScopeFactory ScopeFactory { get; init; }
        public required IObsSocketFactory SocketFactory { get; init; }
    }

    private static Harness Build(bool withToken = true, int mask = 0)
    {
        VtsTestDbContext db = VtsTestDbContext.New();
        db.VtsConnections.Add(
            new VtsConnection
            {
                BroadcasterId = Channel,
                Mode = "direct",
                IsEnabled = true,
                PluginTokenCipher = withToken ? "sealed(plugin-token)" : null,
                EventSubscriptionsMask = mask,
            }
        );
        db.SaveChanges();

        FakeVtsSocket socket = new();
        IObsSocketFactory factory = Substitute.For<IObsSocketFactory>();
        factory
            .ConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IObsSocket>(socket));

        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .TryUnprotectAsync(
                Arg.Any<string?>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => ci.ArgAt<string?>(0) == "sealed(plugin-token)" ? "plugin-token" : null);
        protector
            .ProtectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => $"sealed({ci.ArgAt<string>(0)})");

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(protector);
        services.AddScoped<IVtsConnectionService, VtsConnectionService>();
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        RecordingEventBus bus = new();
        return new Harness
        {
            Transport = new DirectVtsTransport(
                factory,
                scopeFactory,
                bus,
                new FakeTimeProvider(),
                NullLogger<DirectVtsTransport>.Instance
            ),
            Socket = socket,
            Bus = bus,
            Db = db,
            ScopeFactory = scopeFactory,
            SocketFactory = factory,
        };
    }

    [Fact]
    public async Task A_session_replays_the_stored_token_first_then_the_request_flows()
    {
        Harness h = Build();

        Task<Result<string>> send = h.Transport.RequestAsync(
            Channel,
            "ModelLoadRequest",
            """{ "modelID": "m1" }"""
        );
        await h.Socket.ReplyToAsync(
            "AuthenticationRequest",
            "AuthenticationResponse",
            """{ "authenticated": true }"""
        );
        await h.Socket.ReplyToAsync(
            "ModelLoadRequest",
            "ModelLoadResponse",
            """{ "modelID": "m1" }"""
        );
        Result<string> result = await send.WaitAsync(TimeSpan.FromSeconds(5));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Contain("m1");

        // The auth frame went FIRST and carried the opened token + plugin identity.
        JsonElement auth = JsonDocument.Parse(h.Socket.Sent[0]).RootElement;
        auth.GetProperty("messageType").GetString().Should().Be("AuthenticationRequest");
        auth.GetProperty("data")
            .GetProperty("authenticationToken")
            .GetString()
            .Should()
            .Be("plugin-token");
        auth.GetProperty("data")
            .GetProperty("pluginName")
            .GetString()
            .Should()
            .Be(DirectVtsTransport.PluginName);
    }

    [Fact]
    public async Task A_rejected_token_fails_closed_and_no_control_request_leaves()
    {
        Harness h = Build();

        Task<Result<string>> send = h.Transport.RequestAsync(Channel, "ModelLoadRequest", "{}");
        await h.Socket.ReplyToAsync(
            "AuthenticationRequest",
            "AuthenticationResponse",
            """{ "authenticated": false, "reason": "token revoked" }"""
        );
        Result<string> result = await send.WaitAsync(TimeSpan.FromSeconds(5));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VTS_UNAUTHORIZED");
        h.Socket.Sent.Should().NotContain(f => f.Contains("ModelLoadRequest"));
        (await h.Db.VtsConnections.SingleAsync()).Status.Should().Be("error");
    }

    [Fact]
    public async Task No_stored_token_is_VTS_UNAUTHORIZED_without_dialing()
    {
        Harness h = Build(withToken: false);

        Result<string> result = await h.Transport.RequestAsync(Channel, "ModelLoadRequest", "{}");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VTS_UNAUTHORIZED");
        await h
            .SocketFactory.DidNotReceive()
            .ConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_APIError_surfaces_and_an_inbound_event_publishes()
    {
        Harness h = Build(mask: 0b1); // ModelLoadedEvent subscribed

        Task<Result<string>> send = h.Transport.RequestAsync(Channel, "HotkeyTriggerRequest", "{}");
        await h.Socket.ReplyToAsync(
            "AuthenticationRequest",
            "AuthenticationResponse",
            """{ "authenticated": true }"""
        );
        await h.Socket.ReplyToAsync(
            "EventSubscriptionRequest",
            "EventSubscriptionResponse",
            """{ "subscribedEventCount": 1 }"""
        );
        await h.Socket.ReplyToAsync(
            "HotkeyTriggerRequest",
            "APIError",
            """{ "errorID": 251, "message": "Hotkey not found" }"""
        );
        Result<string> result = await send.WaitAsync(TimeSpan.FromSeconds(5));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VTS_ERROR");
        result.ErrorMessage.Should().Be("Hotkey not found");

        // The mask subscribed exactly ModelLoadedEvent, and its events publish for the triggers.
        h.Socket.Sent.Should()
            .ContainSingle(f =>
                f.Contains("EventSubscriptionRequest") && f.Contains("ModelLoadedEvent")
            );
        h.Socket.QueueIncoming(
            """{ "apiName": "VTubeStudioPublicAPI", "messageType": "ModelLoadedEvent", "data": { "modelName": "Akari" } }"""
        );
        VtsEventReceived received = await WaitForAsync(() =>
            h.Bus.Published.OfType<VtsEventReceived>().FirstOrDefault()
        );
        received.EventType.Should().Be("ModelLoadedEvent");
        received.PayloadJson.Should().Contain("Akari");
    }

    [Fact]
    public async Task The_authorize_flow_stores_a_granted_token_and_a_denial_stores_nothing()
    {
        Harness h = Build(withToken: false);
        using IServiceScope scope = h.ScopeFactory.CreateScope();
        VtsPluginAuthorizer authorizer = new(
            h.SocketFactory,
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<IVtsConnectionService>(),
            new FakeTimeProvider(),
            NullLogger<VtsPluginAuthorizer>.Instance
        );

        // Grant path: VTS answers with a token after the streamer clicks Allow.
        Task<Result> authorize = authorizer.AuthorizeAsync(Channel);
        await h.Socket.ReplyToAsync(
            "AuthenticationTokenRequest",
            "AuthenticationTokenResponse",
            """{ "authenticationToken": "fresh-grant" }"""
        );
        Result granted = await authorize.WaitAsync(TimeSpan.FromSeconds(5));

        granted.IsSuccess.Should().BeTrue(granted.ErrorMessage);
        VtsConnection row = await h.Db.VtsConnections.SingleAsync();
        row.PluginTokenCipher.Should().Be("sealed(fresh-grant)");
        row.Status.Should().Be("authorized");
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> probe)
        where T : class
    {
        for (int i = 0; i < 150; i++)
        {
            if (probe() is T hit)
                return hit;
            await Task.Delay(20);
        }
        throw new Xunit.Sdk.XunitException($"Timed out waiting for {typeof(T).Name}.");
    }
}
