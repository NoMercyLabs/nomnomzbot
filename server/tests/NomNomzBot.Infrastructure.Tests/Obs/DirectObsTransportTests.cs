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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Infrastructure.Obs;
using NomNomzBot.Infrastructure.Obs.Transport;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves the direct OBS-WS v5 leg (obs-control.md §3.2/D1/D7): connect performs the Hello →
/// Identify handshake with the EXACT challenge/salt SHA-256 auth string and the channel's event
/// mask; a request goes out as op 6 with a correlated id and its op 7 reply resolves the caller
/// (an OBS error comment surfaces as the failure); an inbound op 5 event publishes
/// <see cref="ObsEventReceivedEvent"/> for the trigger surface; a disabled channel never dials.
/// </summary>
public sealed class DirectObsTransportTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f701");

    private sealed class FakeObsSocket : IObsSocket
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

        public void QueueClose() => _incoming.Writer.TryWrite(null);

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
    }

    private sealed class Harness
    {
        public required DirectObsTransport Transport { get; init; }
        public required FakeObsSocket Socket { get; init; }
        public required RecordingEventBus Bus { get; init; }
        public required ObsTestDbContext Db { get; init; }
    }

    private static Harness Build(
        bool enabled = true,
        string mode = "direct",
        bool withPassword = false
    )
    {
        ObsTestDbContext db = ObsTestDbContext.New();
        db.ObsConnections.Add(
            new()
            {
                BroadcasterId = Channel,
                Mode = mode,
                IsEnabled = enabled,
                PasswordCipher = withPassword ? "sealed(obs-pass)" : null,
                EventSubscriptionsMask = 0xFFF,
            }
        );
        db.SaveChanges();

        FakeObsSocket socket = new();
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
            .Returns(ci => ci.ArgAt<string?>(0) == "sealed(obs-pass)" ? "obs-pass" : null);

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(protector);
        services.AddScoped<IObsConnectionService, ObsConnectionService>();
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        RecordingEventBus bus = new();
        DirectObsTransport transport = new(
            factory,
            scopeFactory,
            bus,
            new FakeTimeProvider(),
            NullLogger<DirectObsTransport>.Instance
        );
        return new()
        {
            Transport = transport,
            Socket = socket,
            Bus = bus,
            Db = db,
        };
    }

    private static void QueueHandshake(FakeObsSocket socket, bool withAuth = false)
    {
        socket.QueueIncoming(
            withAuth
                ? """{ "op": 0, "d": { "rpcVersion": 1, "authentication": { "challenge": "chal", "salt": "salt" } } }"""
                : """{ "op": 0, "d": { "rpcVersion": 1 } }"""
        );
        socket.QueueIncoming("""{ "op": 2, "d": { "negotiatedRpcVersion": 1 } }""");
    }

    [Fact]
    public async Task Connect_identifies_with_the_exact_auth_string_and_event_mask()
    {
        Harness h = Build(withPassword: true);
        QueueHandshake(h.Socket, withAuth: true);

        // Reply to the request once it arrives so SendAsync completes. Connect fetches the real
        // current stream/record state first (op 8) before the caller's own request goes out.
        Task<Result<ObsResponse>> send = h.Transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new("GetVersion", null)
        );
        await ReplyToFirstBatchAsync(h.Socket, streamActive: false, recordActive: false);
        await ReplyToFirstRequestAsync(h.Socket, ok: true);
        Result<ObsResponse> result = await send.WaitAsync(TimeSpan.FromSeconds(20));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        JsonElement identify = JsonDocument.Parse(h.Socket.Sent[0]).RootElement;
        identify.GetProperty("op").GetInt32().Should().Be(1);
        identify
            .GetProperty("d")
            .GetProperty("authentication")
            .GetString()
            .Should()
            .Be(
                DirectObsTransport.ComputeAuthentication("obs-pass", "salt", "chal"),
                "the v5 handshake is base64(sha256(base64(sha256(password+salt)) + challenge))"
            );
        identify.GetProperty("d").GetProperty("eventSubscriptions").GetInt32().Should().Be(0xFFF);
    }

    [Fact]
    public async Task A_request_correlates_its_reply_and_surfaces_an_obs_error_comment()
    {
        Harness h = Build();
        QueueHandshake(h.Socket);

        Task<Result<ObsResponse>> send = h.Transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new("SetCurrentProgramScene", new Dictionary<string, object?> { ["sceneName"] = "BRB" })
        );
        await ReplyToFirstBatchAsync(h.Socket, streamActive: false, recordActive: false);
        string requestId = await ReplyToFirstRequestAsync(
            h.Socket,
            ok: false,
            comment: "No source was found"
        );
        Result<ObsResponse> result = await send.WaitAsync(TimeSpan.FromSeconds(20));

        result.IsSuccess.Should().BeTrue("transport-level success — OBS answered");
        result.Value.Ok.Should().BeFalse();
        result.Value.Error.Should().Be("No source was found");

        // The connect-time state-fetch batch (op 8) is a detached background task racing this test's own
        // request on the wire — either can land first, so locate this request by its id rather than assume
        // a fixed index into Sent.
        JsonElement sent = JsonDocument
            .Parse(h.Socket.Sent.Single(f => f.Contains("\"op\":6") && f.Contains(requestId)))
            .RootElement;
        sent.GetProperty("op").GetInt32().Should().Be(6);
        sent.GetProperty("d").GetProperty("requestId").GetString().Should().Be(requestId);
        sent.GetProperty("d")
            .GetProperty("requestData")
            .GetProperty("sceneName")
            .GetString()
            .Should()
            .Be("BRB");
    }

    [Fact]
    public async Task An_inbound_event_publishes_ObsEventReceivedEvent_for_the_trigger_surface()
    {
        Harness h = Build();
        QueueHandshake(h.Socket);
        Task<Result<ObsResponse>> send = h.Transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new("GetVersion", null)
        );
        await ReplyToFirstBatchAsync(h.Socket, streamActive: false, recordActive: false);
        await ReplyToFirstRequestAsync(h.Socket, ok: true);
        await send.WaitAsync(TimeSpan.FromSeconds(20));

        h.Socket.QueueIncoming(
            """{ "op": 5, "d": { "eventType": "CurrentProgramSceneChanged", "eventIntent": 4, "eventData": { "sceneName": "Live" } } }"""
        );

        ObsEventReceivedEvent received = await WaitForAsync(() =>
            h.Bus.Published.OfType<ObsEventReceivedEvent>().FirstOrDefault()
        );
        received.BroadcasterId.Should().Be(Channel);
        received.ObsEventType.Should().Be("CurrentProgramSceneChanged");
        received.DataJson.Should().Contain("Live");
    }

    [Fact]
    public async Task A_disabled_or_bridge_mode_channel_never_dials()
    {
        Harness disabled = Build(enabled: false);
        Result<ObsResponse> offResult = await disabled.Transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new("GetVersion", null)
        );
        offResult.IsFailure.Should().BeTrue();
        offResult.ErrorCode.Should().Be("OBS_DISABLED");
        disabled.Socket.Sent.Should().BeEmpty();

        Harness bridge = Build(mode: "bridge");
        Result<ObsResponse> bridgeResult = await bridge.Transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new("GetVersion", null)
        );
        bridgeResult.IsFailure.Should().BeTrue();
        bridgeResult.ErrorCode.Should().Be("OBS_WRONG_MODE");
    }

    [Fact]
    public async Task Connect_publishes_the_real_stream_and_record_state_when_OBS_is_already_live()
    {
        Harness h = Build();
        QueueHandshake(h.Socket);

        Task<Result<ObsResponse>> send = h.Transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new("GetVersion", null)
        );
        // OBS answers the connect-time probe as ALREADY streaming and recording — a session that
        // predates the bot's connection, so no StreamStateChanged/RecordStateChanged event will ever
        // fire for it.
        await ReplyToFirstBatchAsync(h.Socket, streamActive: true, recordActive: true);
        await ReplyToFirstRequestAsync(h.Socket, ok: true);
        (await send.WaitAsync(TimeSpan.FromSeconds(20))).IsSuccess.Should().BeTrue();

        ObsConnectionEstablishedEvent published = await WaitForAsync(() =>
            h.Bus.Published.OfType<ObsConnectionEstablishedEvent>().FirstOrDefault()
        );
        published.BroadcasterId.Should().Be(Channel);
        published.Streaming.Should().BeTrue();
        published.Recording.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_connect_time_state_probe_never_fails_the_connect_or_publishes()
    {
        Harness h = Build();
        QueueHandshake(h.Socket);

        Task<Result<ObsResponse>> send = h.Transport.SendAsync(
            Channel,
            Guid.CreateVersion7(),
            new("GetVersion", null)
        );
        await ReplyToFirstBatchAsync(h.Socket, ok: false);
        await ReplyToFirstRequestAsync(h.Socket, ok: true);

        (await send.WaitAsync(TimeSpan.FromSeconds(20)))
            .IsSuccess.Should()
            .BeTrue("a failed best-effort state probe must never block/fail the actual connect");
        await Task.Delay(50);
        h.Bus.Published.OfType<ObsConnectionEstablishedEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// Waits for the connect-time op-8 state-fetch batch and replies op 9. A successful reply carries
    /// [streamActive]/[recordActive] as each request's <c>outputActive</c>; <c>ok: false</c> answers
    /// the whole batch as a failed OBS-WS request instead (the best-effort-probe-fails path).
    /// </summary>
    private static async Task<string> ReplyToFirstBatchAsync(
        FakeObsSocket socket,
        bool streamActive = false,
        bool recordActive = false,
        bool ok = true
    )
    {
        string requestFrame = await WaitForAsync(() =>
            socket.Sent.FirstOrDefault(f => f.Contains("\"op\":8"))
        );
        string requestId = JsonDocument
            .Parse(requestFrame)
            .RootElement.GetProperty("d")
            .GetProperty("requestId")
            .GetString()!;
        string results = ok
            ? $$"""
                [
                  { "requestType": "GetStreamStatus", "requestStatus": { "result": true, "code": 100 }, "responseData": { "outputActive": {{(
                    streamActive ? "true" : "false"
                )}} } },
                  { "requestType": "GetRecordStatus", "requestStatus": { "result": true, "code": 100 }, "responseData": { "outputActive": {{(
                    recordActive ? "true" : "false"
                )}} } }
                ]
                """
            : """
                [
                  { "requestType": "GetStreamStatus", "requestStatus": { "result": false, "code": 600, "comment": "not ready" } },
                  { "requestType": "GetRecordStatus", "requestStatus": { "result": false, "code": 600, "comment": "not ready" } }
                ]
                """;
        socket.QueueIncoming(
            $$"""{ "op": 9, "d": { "requestId": "{{requestId}}", "results": {{results}} } }"""
        );
        return requestId;
    }

    /// <summary>Waits for the op-6 frame, replies op 7 with its request id, and returns that id.</summary>
    private static async Task<string> ReplyToFirstRequestAsync(
        FakeObsSocket socket,
        bool ok,
        string? comment = null
    )
    {
        string requestFrame = await WaitForAsync(() =>
            socket.Sent.FirstOrDefault(f => f.Contains("\"op\":6"))
        );
        string requestId = JsonDocument
            .Parse(requestFrame)
            .RootElement.GetProperty("d")
            .GetProperty("requestId")
            .GetString()!;
        string status = ok
            ? """{ "result": true, "code": 100 }"""
            : $$"""{ "result": false, "code": 600, "comment": "{{comment}}" }""";
        socket.QueueIncoming(
            $$"""{ "op": 7, "d": { "requestType": "x", "requestId": "{{requestId}}", "requestStatus": {{status}} } }"""
        );
        return requestId;
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> probe)
        where T : class
    {
        // S120: 750 * 20ms (15s ceiling, not the old 3s) — this polls a real flag set by the transport's
        // background receive loop, and under the full suite's thread-pool pressure that loop can be
        // scheduled late. Widening the ceiling doesn't change what's asserted, only how long a genuine
        // (but delayed) signal is given to arrive.
        for (int i = 0; i < 750; i++)
        {
            if (probe() is { } hit)
                return hit;
            await Task.Delay(20);
        }
        throw new Xunit.Sdk.XunitException($"Timed out waiting for {typeof(T).Name}.");
    }
}
