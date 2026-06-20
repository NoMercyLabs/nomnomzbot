// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Behavior tests for the WebSocket EventSub transport lifecycle, driven over an in-memory channel (no real
/// socket, no Twitch). Each test proves a consequence: welcome captures the session id and signals the sink to
/// (re)register; a server reconnect swaps to the new channel and signals a fresh welcome without losing the
/// subscription intent; a keepalive timeout (virtual time) tears down and reconnects with backoff; a revocation
/// frame reaches the sink with the subscription id + status.
/// </summary>
public sealed class WebSocketEventSubTransportTests
{
    private static string Welcome(string sessionId, int keepaliveSeconds = 30) =>
        "{\"metadata\":{\"message_id\":\"w-"
        + sessionId
        + "\",\"message_type\":\"session_welcome\",\"message_timestamp\":\"2026-06-20T12:00:00Z\"},"
        + "\"payload\":{\"session\":{\"id\":\""
        + sessionId
        + "\",\"status\":\"connected\",\"keepalive_timeout_seconds\":"
        + keepaliveSeconds
        + "}}}";

    private static string ReconnectFrame(string url) =>
        "{\"metadata\":{\"message_id\":\"r-1\",\"message_type\":\"session_reconnect\",\"message_timestamp\":\"2026-06-20T12:01:00Z\"},"
        + "\"payload\":{\"session\":{\"id\":\"sess\",\"status\":\"reconnecting\",\"reconnect_url\":\""
        + url
        + "\"}}}";

    private const string NotificationFrame = """
        {"metadata":{"message_id":"n-1","message_type":"notification","message_timestamp":"2026-06-20T12:02:00Z"},
         "payload":{"subscription":{"id":"sub-1","type":"channel.follow","version":"2","status":"enabled"},
                    "event":{"broadcaster_user_id":"twitch-9","user_id":"42"}}}
        """;

    private const string RevocationFrame = """
        {"metadata":{"message_id":"v-1","message_type":"revocation","message_timestamp":"2026-06-20T12:03:00Z"},
         "payload":{"subscription":{"id":"sub-1","type":"channel.follow","version":"2","status":"authorization_revoked"},
                    "event":{"broadcaster_user_id":"twitch-9"}}}
        """;

    private static WebSocketEventSubTransport NewTransport(
        ScriptedChannelFactory factory,
        FakeTimeProvider clock,
        CapturingSink sink
    )
    {
        WebSocketEventSubTransport transport = new(
            factory,
            new SingleServiceScopeFactory(new NoopHelixTransport()),
            new EventSubConditionBuilder(),
            clock,
            NullLogger<WebSocketEventSubTransport>.Instance
        );
        transport.BindSink(sink);
        return transport;
    }

    [Fact]
    public async Task SessionWelcome_CapturesSessionId_AndSignalsSinkToRegister()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        CapturingSink sink = new();
        ScriptedChannel channel = new([Welcome("session-A")]);
        ScriptedChannelFactory factory = new(channel);
        WebSocketEventSubTransport transport = NewTransport(factory, clock, sink);

        Result<EventSubTransportHandle> handle = await transport.StartAsync();

        handle.IsSuccess.Should().BeTrue(handle.ErrorMessage);
        handle.Value.SessionId.Should().Be("session-A", "the welcome's session id is captured");
        transport.SessionId.Should().Be("session-A");

        // The fresh welcome triggers exactly one re-registration signal carrying the new session id.
        await sink.WaitForWelcomesAsync(1);
        sink.Welcomes.Should().ContainSingle().Which.Should().Be("session-A");

        await transport.StopAsync();
    }

    [Fact]
    public async Task Notification_IsForwardedToSink_WithTypeAndBroadcasterAndEvent()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        CapturingSink sink = new();
        ScriptedChannel channel = new([Welcome("session-A"), NotificationFrame]);
        WebSocketEventSubTransport transport = NewTransport(
            new ScriptedChannelFactory(channel),
            clock,
            sink
        );

        await transport.StartAsync();
        await sink.WaitForNotificationsAsync(1);

        CapturedNotification notification = sink.Notifications.Should().ContainSingle().Subject;
        notification.SubscriptionType.Should().Be("channel.follow");
        notification.SubscriptionVersion.Should().Be("2");
        notification
            .TwitchBroadcasterUserId.Should()
            .Be("twitch-9", "the broadcaster id is read from the event");
        notification.Event.GetProperty("user_id").GetString().Should().Be("42");

        await transport.StopAsync();
    }

    [Fact]
    public async Task SessionReconnect_SwapsChannel_AndReRegistersOnTheNewSession()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        CapturingSink sink = new();

        // First channel: welcome (A) then a reconnect pointing at the second channel's URL.
        ScriptedChannel first = new([
            Welcome("session-A"),
            ReconnectFrame("wss://reconnect.example/ws"),
        ]);
        // Second channel: a fresh welcome (B) — the swapped connection.
        ScriptedChannel second = new([Welcome("session-B")]);
        ScriptedChannelFactory factory = new(first, second);
        WebSocketEventSubTransport transport = NewTransport(factory, clock, sink);

        await transport.StartAsync();

        // The transport must connect the reconnect URL (the swap) and surface the new session's welcome.
        await sink.WaitForWelcomesAsync(2);
        sink.Welcomes.Should().Equal("session-A", "session-B");
        factory.ConnectedUrls.Should().Contain(u => u.AbsoluteUri.Contains("reconnect.example"));
        transport
            .SessionId.Should()
            .Be("session-B", "the swapped session is now active — no subscription gap");

        await transport.StopAsync();
    }

    [Fact]
    public async Task KeepaliveTimeout_TearsDownAndReconnects_WithBackoff()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        CapturingSink sink = new();

        // First channel: welcome then go silent (idle) so the keepalive timer fires.
        ScriptedChannel first = new(
            [Welcome("session-A", keepaliveSeconds: 10)],
            idleAfterScript: true
        );
        // Second channel: a fresh welcome after the reconnect.
        ScriptedChannel second = new([Welcome("session-B")]);
        ScriptedChannelFactory factory = new(first, second);
        WebSocketEventSubTransport transport = NewTransport(factory, clock, sink);

        await transport.StartAsync();
        await sink.WaitForWelcomesAsync(1);

        // Drive virtual time forward in steps until the keepalive timer fires, the channel is torn down, the
        // backoff elapses, and the transport reconnects to a fresh welcome. Stepping (rather than one big jump)
        // is robust to whether the idle receive's keepalive timer was armed before or after a given advance.
        for (int i = 0; i < 40 && sink.Welcomes.Count < 2; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(25);
        }

        sink.Welcomes.Should().Equal("session-A", "session-B");
        first.WasDisposed.Should().BeTrue("the timed-out channel is torn down");
        factory
            .ConnectedUrls.Count.Should()
            .BeGreaterThanOrEqualTo(2, "a reconnect opened a second channel");

        await transport.StopAsync();
    }

    [Fact]
    public async Task Revocation_IsForwardedToSink_WithSubscriptionIdAndStatus()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        CapturingSink sink = new();
        ScriptedChannel channel = new([Welcome("session-A"), RevocationFrame]);
        WebSocketEventSubTransport transport = NewTransport(
            new ScriptedChannelFactory(channel),
            clock,
            sink
        );

        await transport.StartAsync();
        await sink.WaitForRevocationsAsync(1);

        CapturedRevocation revocation = sink.Revocations.Should().ContainSingle().Subject;
        revocation.TwitchSubscriptionId.Should().Be("sub-1");
        revocation.SubscriptionType.Should().Be("channel.follow");
        revocation.Status.Should().Be("authorization_revoked");

        await transport.StopAsync();
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed record CapturedNotification(
        string MessageId,
        string SubscriptionType,
        string SubscriptionVersion,
        string TwitchBroadcasterUserId,
        JsonElement Event
    );

    private sealed record CapturedRevocation(
        string TwitchSubscriptionId,
        string SubscriptionType,
        string Status
    );

    private sealed class CapturingSink : IEventSubNotificationSink
    {
        private readonly ConcurrentQueue<string> _welcomes = new();
        private readonly ConcurrentQueue<CapturedNotification> _notifications = new();
        private readonly ConcurrentQueue<CapturedRevocation> _revocations = new();

        public IReadOnlyList<string> Welcomes => _welcomes.ToList();
        public IReadOnlyList<CapturedNotification> Notifications => _notifications.ToList();
        public IReadOnlyList<CapturedRevocation> Revocations => _revocations.ToList();

        public Task OnSessionWelcomeAsync(string sessionId, CancellationToken ct)
        {
            _welcomes.Enqueue(sessionId);
            return Task.CompletedTask;
        }

        public Task OnNotificationAsync(
            string messageId,
            DateTimeOffset messageTimestamp,
            string subscriptionType,
            string subscriptionVersion,
            string twitchBroadcasterUserId,
            JsonElement @event,
            CancellationToken ct
        )
        {
            _notifications.Enqueue(
                new CapturedNotification(
                    messageId,
                    subscriptionType,
                    subscriptionVersion,
                    twitchBroadcasterUserId,
                    @event.Clone()
                )
            );
            return Task.CompletedTask;
        }

        public Task OnRevocationAsync(
            string twitchSubscriptionId,
            string subscriptionType,
            string status,
            string twitchBroadcasterUserId,
            CancellationToken ct
        )
        {
            _revocations.Enqueue(
                new CapturedRevocation(twitchSubscriptionId, subscriptionType, status)
            );
            return Task.CompletedTask;
        }

        public Task WaitForWelcomesAsync(int count) => WaitUntil(() => _welcomes.Count >= count);

        public Task WaitForNotificationsAsync(int count) =>
            WaitUntil(() => _notifications.Count >= count);

        public Task WaitForRevocationsAsync(int count) =>
            WaitUntil(() => _revocations.Count >= count);

        private static async Task WaitUntil(Func<bool> predicate)
        {
            for (int i = 0; i < 200 && !predicate(); i++)
                await Task.Delay(25);
            predicate().Should().BeTrue("the awaited condition was reached within the timeout");
        }
    }

    private sealed class ScriptedChannelFactory : IWebSocketChannelFactory
    {
        private readonly Queue<ScriptedChannel> _channels;
        private readonly ConcurrentQueue<Uri> _connected = new();
        private int _connections;

        public ScriptedChannelFactory(params ScriptedChannel[] channels) =>
            _channels = new Queue<ScriptedChannel>(channels);

        public IReadOnlyList<Uri> ConnectedUrls => _connected.ToList();

        public Task<IWebSocketChannel> ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            _connected.Enqueue(uri);
            Interlocked.Increment(ref _connections);
            ScriptedChannel channel =
                _channels.Count > 0
                    ? _channels.Dequeue()
                    : new ScriptedChannel([], idleAfterScript: true);
            return Task.FromResult<IWebSocketChannel>(channel);
        }

        public async Task WaitForConnectionsAsync(int count)
        {
            for (int i = 0; i < 200 && Volatile.Read(ref _connections) < count; i++)
                await Task.Delay(25);
        }
    }

    /// <summary>
    /// An in-memory <see cref="IWebSocketChannel"/> that yields a fixed script of frames, then either closes
    /// the connection (default — drives a clean reconnect) or blocks forever (idle — lets the keepalive timer
    /// fire). Records disposal so a test can assert the channel was torn down.
    /// </summary>
    private sealed class ScriptedChannel(IReadOnlyList<string> frames, bool idleAfterScript = false)
        : IWebSocketChannel
    {
        private readonly Queue<string> _frames = new(frames);
        private readonly TaskCompletionSource _idle = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public bool WasDisposed { get; private set; }

        public async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken
        )
        {
            if (_frames.Count > 0)
            {
                byte[] payload = Encoding.UTF8.GetBytes(_frames.Dequeue());
                payload.CopyTo(buffer.Array!, buffer.Offset);
                return new WebSocketReceiveResult(payload.Length, WebSocketMessageType.Text, true);
            }

            if (!idleAfterScript)
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);

            // Idle: block until the keepalive timer cancels the token (or shutdown cancels it).
            await using (cancellationToken.Register(() => _idle.TrySetResult()))
                await _idle.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            _idle.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A no-op Helix transport — the lifecycle tests exercise the receive loop, not subscription HTTP.</summary>
    private sealed class NoopHelixTransport : ITwitchHelixTransport
    {
        public Task<Result<T>> GetSingleAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<T>("not used", "NOT_FOUND"));

        public Task<Result<IReadOnlyList<T>>> GetListAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<T>>([]));

        public Task<Result<TwitchPage<T>>> GetPageAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(new TwitchPage<T>([], null, 0)));

        public Task<Result<int>> GetTotalAsync(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(0));

        public Task<Result> SendAsync(TwitchHelixRequest request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<T>> SendWithResultAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<T>("not used", "NOT_FOUND"));
    }

    /// <summary>
    /// A real <see cref="IServiceScopeFactory"/> over a one-binding container, mirroring how the singleton
    /// transport resolves the scoped Helix client per call in production (it never captures a scoped service).
    /// </summary>
    private sealed class SingleServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;

        public SingleServiceScopeFactory(ITwitchHelixTransport helix) =>
            _provider = new ServiceCollection()
                .AddScoped<ITwitchHelixTransport>(_ => helix)
                .BuildServiceProvider();

        public IServiceScope CreateScope() => _provider.CreateScope();
    }
}
