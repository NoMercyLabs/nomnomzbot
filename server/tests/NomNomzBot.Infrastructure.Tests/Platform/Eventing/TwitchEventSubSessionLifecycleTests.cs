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
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// The hosted service driving the REAL WebSocket transport over in-memory channels, so each test observes what
/// reaches the wire: how many connections were opened, how many creates Twitch saw, and whether frames kept
/// being read while post-welcome registration was still running.
/// </summary>
public sealed class TwitchEventSubSessionLifecycleTests
{
    private const string TwitchChannelId = "twitch-9";

    private static string Welcome(string sessionId) =>
        "{\"metadata\":{\"message_id\":\"w-"
        + sessionId
        + "\",\"message_type\":\"session_welcome\",\"message_timestamp\":\"2026-06-20T12:00:00Z\"},"
        + "\"payload\":{\"session\":{\"id\":\""
        + sessionId
        + "\",\"status\":\"connected\",\"keepalive_timeout_seconds\":30}}}";

    private const string ChatNotificationFrame = """
        {"metadata":{"message_id":"n-1","message_type":"notification","message_timestamp":"2026-06-20T12:02:00Z"},
         "payload":{"subscription":{"id":"sub-1","type":"channel.chat.message","version":"1","status":"enabled"},
                    "event":{"broadcaster_user_id":"twitch-9","chatter_user_id":"42"}}}
        """;

    [Fact]
    public async Task An_owner_whose_topics_are_all_refused_stays_closed_until_its_grant_changes()
    {
        ScriptedChannel first = new([Welcome("s1")], holdOpen: true);
        Harness h = Harness.Build(first, new ScriptedChannel([Welcome("s2")], holdOpen: true));
        Guid tenant = h.Tenant;
        h.SeedConnection(["user:read:email"]);
        h.SeedRow(
            "channel.follow",
            "2",
            "failed",
            lastError: "Missing required scope moderator:read:followers"
        );

        // The session was open when the topic got refused; Twitch then closes it for carrying nothing (4003).
        await h.Transport.EnsureSessionAsync(tenant.ToString());
        await h.Service.WhenWelcomeWorkIdleAsync();
        first.CloseNow();
        await first.Disposed.WaitAsync(TimeSpan.FromSeconds(10));

        // N reconnect windows (well past the 64 s backoff cap) and N reconcile passes.
        for (int cycle = 0; cycle < 5; cycle++)
        {
            await h.AdvanceAsync(TimeSpan.FromSeconds(70));
            Result reconciled = await h.Service.EnsureSubscribedAsync(tenant, ["channel.follow"]);
            reconciled.IsFailure.Should().BeTrue("the topic is still refused");
        }

        h.Factory.Connections.Should().Be(1, "a fully refused owner must not be reopened");
        h.Helix.Creates.Should().BeEmpty("a held topic is never re-POSTed");
        h.Transport.CurrentSessionId(tenant.ToString()).Should().BeNull();

        // A token refresh that changed nothing must not reopen it either.
        await h.Service.EnsureSubscribedAsync(tenant, ["channel.follow"]);
        h.Factory.Connections.Should().Be(1);

        // The grant changes (what EventSubResubscribeOnTokenRefreshedHandler reacts to): one session opens and
        // the topic is POSTed onto it.
        await h.GrantAsync(["user:read:email", "moderator:read:followers"]);
        Result resubscribed = await h.Service.EnsureSubscribedAsync(tenant, ["channel.follow"]);

        resubscribed.IsSuccess.Should().BeTrue(resubscribed.ErrorMessage);
        h.Factory.Connections.Should().Be(2);
        h.Helix.Creates.Should().ContainSingle().Which.Should().Be("s2");
        EventSubSubscription row = await h.Db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        row.Status.Should().Be("enabled");
        row.SessionId.Should().Be("s2");

        await h.StopAsync();
    }

    [Fact]
    public async Task The_receive_loop_keeps_reading_frames_while_post_welcome_registration_runs()
    {
        Harness h = Harness.Build(
            new ScriptedChannel([Welcome("s1"), ChatNotificationFrame], holdOpen: true)
        );
        h.SeedRow("channel.chat.message", "1", "pending");
        h.Helix.BlockCreates();

        // The bot owner has welcomed before, so the next welcome is a reconnect that re-registers its rows.
        await h.Service.OnSessionWelcomeAsync(
            "s0",
            EventSubOwnerKeys.Bot,
            null,
            CancellationToken.None
        );
        await h.Service.WhenWelcomeWorkIdleAsync();

        await h.Transport.StartAsync();
        await h.Helix.CreateStarted.WaitAsync(TimeSpan.FromSeconds(10));

        // The create is still stuck, yet the frame after the welcome was read and dispatched.
        await h.WaitForDispatchAsync();
        h.Helix.CreatesReleased.Should().BeFalse();
        h.Dispatched.Should()
            .ContainSingle()
            .Which.SubscriptionType.Should()
            .Be("channel.chat.message");

        h.Helix.ReleaseCreates();
        await h.Service.WhenWelcomeWorkIdleAsync();
        EventSubSubscription row = await h.Db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        row.Status.Should().Be("enabled");
        row.SessionId.Should().Be("s1");

        await h.StopAsync();
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public required Guid Tenant { get; init; }
        public required EventSubTestDbContext Db { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required ScriptedChannelFactory Factory { get; init; }
        public required GatedHelixTransport Helix { get; init; }
        public required WebSocketEventSubTransport Transport { get; init; }
        public required TwitchEventSubHostedService Service { get; init; }
        public required ConcurrentQueue<EventSubNotification> DispatchedQueue { get; init; }

        public IReadOnlyList<EventSubNotification> Dispatched => [.. DispatchedQueue];

        public static Harness Build(params ScriptedChannel[] channels)
        {
            // One context per DI scope over one shared database: the welcome worker and the receive loop reach
            // the registry concurrently, exactly as separate request scopes do in production.
            string database = $"eventsub-lifecycle-{Guid.NewGuid():N}";
            EventSubTestDbContext db = EventSubTestDbContext.Shared(database);
            FakeTimeProvider clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
            GatedHelixTransport helix = new();
            ConcurrentQueue<EventSubNotification> dispatched = new();
            Guid tenant = Guid.CreateVersion7();

            ITwitchIdentityResolver resolver = Substitute.For<ITwitchIdentityResolver>();
            resolver
                .GetTwitchChannelIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(TwitchChannelId);
            resolver
                .GetBroadcasterIdAsync(TwitchChannelId, Arg.Any<CancellationToken>())
                .Returns(tenant);

            INotificationDispatcher dispatcher = Substitute.For<INotificationDispatcher>();
            dispatcher
                .DispatchAsync(Arg.Any<EventSubNotification>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    dispatched.Enqueue(call.Arg<EventSubNotification>());
                    return Result.Success(
                        new NotificationDispatchResult(Guid.CreateVersion7(), 1, false)
                    );
                });

            IEventSubGapBackfillService backfill = Substitute.For<IEventSubGapBackfillService>();
            backfill
                .BackfillGapAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success(0));

            ServiceProvider provider = new ServiceCollection()
                .AddScoped<IApplicationDbContext>(_ => EventSubTestDbContext.Shared(database))
                .AddScoped<ITwitchIdentityResolver>(_ => resolver)
                .AddScoped<INotificationDispatcher>(_ => dispatcher)
                .AddScoped<IEventSubGapBackfillService>(_ => backfill)
                .AddScoped<ITwitchHelixTransport>(_ => helix)
                .BuildServiceProvider();
            IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();

            ScriptedChannelFactory factory = new(channels);
            WebSocketEventSubTransport transport = new(
                factory,
                scopes,
                new EventSubConditionBuilder(),
                clock,
                NullLogger<WebSocketEventSubTransport>.Instance,
                TestSanction.Held()
            );
            TwitchEventSubHostedService service = new(
                scopes,
                transport,
                new EventSubConditionBuilder(),
                Substitute.For<IEventBus>(),
                clock,
                NullLogger<TwitchEventSubHostedService>.Instance
            );

            return new()
            {
                Tenant = tenant,
                Db = db,
                Clock = clock,
                Factory = factory,
                Helix = helix,
                Transport = transport,
                Service = service,
                DispatchedQueue = dispatched,
            };
        }

        public void SeedConnection(IReadOnlyList<string> scopes)
        {
            Db.IntegrationConnections.Add(
                new()
                {
                    BroadcasterId = Tenant,
                    Provider = "twitch",
                    Status = "connected",
                    Scopes = [.. scopes],
                }
            );
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public async Task GrantAsync(IReadOnlyList<string> scopes)
        {
            IntegrationConnection connection = await Db.IntegrationConnections.SingleAsync();
            connection.Scopes = [.. scopes];
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public void SeedRow(
            string eventType,
            string version,
            string status,
            string? lastError = null
        )
        {
            Db.EventSubSubscriptions.Add(
                new()
                {
                    BroadcasterId = Tenant,
                    Provider = "twitch",
                    EventType = eventType,
                    Version = version,
                    Transport = "websocket",
                    Status = status,
                    Enabled = true,
                    LastError = lastError,
                    CreatedAt = DateTime.UtcNow,
                }
            );
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        // Steps virtual time in 1 s slices with a real yield each, so a jittered backoff anywhere inside the
        // window is crossed and the reconnect (if any) has run before the caller looks.
        public async Task AdvanceAsync(TimeSpan window)
        {
            for (
                TimeSpan elapsed = TimeSpan.Zero;
                elapsed < window;
                elapsed += TimeSpan.FromSeconds(1)
            )
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(5);
            }
        }

        public async Task WaitForDispatchAsync()
        {
            for (int i = 0; i < 200 && DispatchedQueue.IsEmpty; i++)
                await Task.Delay(25);
        }

        public async Task StopAsync()
        {
            Helix.ReleaseCreates();
            await Service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// A Helix transport that answers every EventSub create with an enabled subscription on the posted session,
    /// records that session id per create, and can hold creates open to model a slow Twitch.
    /// </summary>
    private sealed class GatedHelixTransport : ITwitchHelixTransport
    {
        private readonly ConcurrentQueue<string> _creates = new();
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _createStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private bool _blocking;

        /// <summary>The session id each create was POSTed onto, in order.</summary>
        public IReadOnlyList<string> Creates => [.. _creates];
        public Task CreateStarted => _createStarted.Task;
        public bool CreatesReleased => _release.Task.IsCompleted;

        public void BlockCreates() => _blocking = true;

        public void ReleaseCreates() => _release.TrySetResult();

        public async Task<Result<T>> SendWithResultAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        )
        {
            _createStarted.TrySetResult();
            if (_blocking)
                await _release.Task.WaitAsync(ct);

            string sessionId = System
                .Text.Json.JsonSerializer.SerializeToElement(request.Body)
                .GetProperty("transport")
                .GetProperty("session_id")
                .GetString()!;
            _creates.Enqueue(sessionId);

            TwitchEventSubWireSubscription wire = new()
            {
                Id = $"tw-{Guid.NewGuid():N}",
                Status = "enabled",
                Cost = 0,
            };
            return Result.Success((T)(object)wire);
        }

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

        public Task<Result<string>> GetRawAsync(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(""));

        public Task<Result> SendAsync(TwitchHelixRequest request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class ScriptedChannelFactory(params ScriptedChannel[] channels)
        : IWebSocketChannelFactory
    {
        private readonly Queue<ScriptedChannel> _channels = new(channels);
        private int _connections;

        public int Connections => Volatile.Read(ref _connections);

        public Task<IWebSocketChannel> ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connections);
            ScriptedChannel channel;
            lock (_channels)
                channel = _channels.Count > 0 ? _channels.Dequeue() : new([]);
            return Task.FromResult<IWebSocketChannel>(channel);
        }
    }

    /// <summary>
    /// Yields a fixed script of frames, then closes with 4003 — at once, or when <see cref="CloseNow"/> is
    /// called if <c>holdOpen</c> is set.
    /// </summary>
    private sealed class ScriptedChannel(IReadOnlyList<string> frames, bool holdOpen = false)
        : IWebSocketChannel
    {
        private readonly Queue<string> _frames = new(frames);
        private readonly TaskCompletionSource _close = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task Disposed => _disposed.Task;

        public void CloseNow() => _close.TrySetResult();

        public async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken
        )
        {
            if (_frames.Count > 0)
            {
                byte[] payload = Encoding.UTF8.GetBytes(_frames.Dequeue());
                payload.CopyTo(buffer.Array!, buffer.Offset);
                return new(payload.Length, WebSocketMessageType.Text, true);
            }

            if (holdOpen)
                await _close.Task.WaitAsync(cancellationToken);

            return new(
                0,
                WebSocketMessageType.Close,
                true,
                (WebSocketCloseStatus)4003,
                "connection unused"
            );
        }

        public ValueTask DisposeAsync()
        {
            _disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
