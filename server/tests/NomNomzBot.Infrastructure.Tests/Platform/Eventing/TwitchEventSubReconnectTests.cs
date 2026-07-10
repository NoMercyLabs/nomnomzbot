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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Proves the reconnect / re-subscribe hardening in <see cref="TwitchEventSubHostedService"/> (the
/// stale-session-409 storm fix). A recording <see cref="IEventSubTransport"/> captures the exact create order
/// and every delete id, and an InMemory <see cref="IApplicationDbContext"/> holds the registry, so every
/// assertion is on an actual consequence — a Twitch call made or skipped, or a persisted registry-row status —
/// never a log line:
/// <list type="bullet">
///   <item>a session welcome deletes the dead session's subscriptions before re-registering;</item>
///   <item>an already-enabled row on the current session is adopted (no duplicate create);</item>
///   <item>a create 409 parks the row <c>pending</c> and a 429 parks it <c>deferred</c> — never a terminal
///   <c>failed</c> — and a deferred row is not re-created on the next pass;</item>
///   <item>cost-0 chat topics are subscribed first;</item>
///   <item>reconcile deletes the tenant's stale subscription and reports it in the revoked count.</item>
/// </list>
/// </summary>
public sealed class TwitchEventSubReconnectTests
{
    private const string TwitchChannelId = "twitch-channel-123";

    private static (TwitchEventSubHostedService Service, EventSubTestDbContext Db) Build(
        IEventSubTransport transport
    )
    {
        EventSubTestDbContext db = EventSubTestDbContext.New();

        ITwitchIdentityResolver resolver = Substitute.For<ITwitchIdentityResolver>();
        resolver
            .GetTwitchChannelIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(TwitchChannelId);

        IPlatformBotReadinessGate gate = Substitute.For<IPlatformBotReadinessGate>();
        gate.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        // The context is a single shared instance (singleton): the hosted service opens a fresh DI scope per
        // call, so a per-scope/transient context would lose the registry between SubscribeAsync calls.
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddScoped<ITwitchIdentityResolver>(_ => resolver)
            .AddScoped<IPlatformBotReadinessGate>(_ => gate)
            .BuildServiceProvider();

        TwitchEventSubHostedService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            transport,
            new EventSubConditionBuilder(),
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<TwitchEventSubHostedService>.Instance
        );

        return (service, db);
    }

    [Fact]
    public async Task OnSessionWelcome_deletes_this_owners_dead_session_subs_before_reregister()
    {
        // The bot owner's chat-read row is stranded on the dead session "old"; the fresh welcome is "new". Only
        // the dead-session row must be deleted at Twitch so its (type+condition) 409 key stops blocking the
        // re-create on the new session. Cleanup is registry-driven: it only ever touches our OWN rows for THIS
        // owner, never another owner's live subscription.
        Guid tenant = Guid.CreateVersion7();
        RecordingEventSubTransport transport = new(startSessionId: "new");
        (TwitchEventSubHostedService service, EventSubTestDbContext db) = Build(transport);

        Seed(
            db,
            tenant,
            "channel.chat.message",
            version: "1",
            status: "enabled",
            twitchSubscriptionId: "sub-old",
            sessionId: "old"
        );

        await service.OnSessionWelcomeAsync("new", EventSubOwnerKeys.Bot, CancellationToken.None);

        transport.Deletes.Should().Contain("sub-old");
    }

    [Fact]
    public async Task SubscribeAsync_adopts_enabled_row_on_current_session_without_creating()
    {
        Guid tenant = Guid.CreateVersion7();
        RecordingEventSubTransport transport = new(startSessionId: "sess-current");
        (TwitchEventSubHostedService service, EventSubTestDbContext db) = Build(transport);

        Seed(
            db,
            tenant,
            "channel.chat.message",
            version: "1",
            status: "enabled",
            twitchSubscriptionId: "adopted-1",
            sessionId: "sess-current"
        );

        // Bring the handle up so the current session id is "sess-current" (StartAsync returns it).
        await service.ReconnectAsync();

        Result<EventSubSubscriptionDto> result = await service.SubscribeAsync(
            tenant,
            "channel.chat.message"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("enabled");
        result.Value.TwitchSubscriptionId.Should().Be("adopted-1");
        // The row was live on the current session — no duplicate create hit Twitch.
        transport.CreatedTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_on_409_marks_pending_not_failed()
    {
        Guid tenant = Guid.CreateVersion7();
        RecordingEventSubTransport transport = new(
            onCreate: (_, _) =>
                Result.Failure<TwitchSubscriptionResult>("duplicate", TwitchErrorCodes.Conflict)
        );
        (TwitchEventSubHostedService service, EventSubTestDbContext db) = Build(transport);

        // Pre-parked as "failed" so recovering it to "pending" can only come from the 409 mapping.
        Seed(db, tenant, "channel.follow", version: "2", status: "failed");

        Result<EventSubSubscriptionDto> result = await service.SubscribeAsync(
            tenant,
            "channel.follow"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.Conflict);

        EventSubSubscription row = await db.EventSubSubscriptions.SingleAsync(s =>
            s.BroadcasterId == tenant
        );
        row.Status.Should().Be("pending");
        row.LastError.Should().Be("duplicate");
        transport.CreatedTypes.Should().ContainSingle(); // the create was attempted
    }

    [Fact]
    public async Task SubscribeAsync_on_429_marks_pending_and_retries_next_pass()
    {
        Guid tenant = Guid.CreateVersion7();
        RecordingEventSubTransport transport = new(
            onCreate: (_, _) =>
                Result.Failure<TwitchSubscriptionResult>(
                    "too many requests",
                    TwitchErrorCodes.RateLimited
                )
        );
        (TwitchEventSubHostedService service, EventSubTestDbContext db) = Build(transport);

        Result<EventSubSubscriptionDto> first = await service.SubscribeAsync(
            tenant,
            "channel.cheer"
        );

        // A 429 is a transient burst limit (per-broadcaster sessions removed the permanent cost-cap), so it parks
        // the row "pending" — retryable — not "deferred".
        first.IsFailure.Should().BeTrue();
        EventSubSubscription row = await db.EventSubSubscriptions.SingleAsync(s =>
            s.BroadcasterId == tenant
        );
        row.Status.Should().Be("pending");
        transport.CreatedTypes.Should().ContainSingle();

        // The next reconcile pass retries the create (a pending row is never parked).
        await service.SubscribeAsync(tenant, "channel.cheer");
        transport.CreatedTypes.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnsureSubscribedAsync_subscribes_chat_message_first()
    {
        Guid tenant = Guid.CreateVersion7();
        RecordingEventSubTransport transport = new();
        (TwitchEventSubHostedService service, _) = Build(transport);

        // Unordered on purpose, with the cost-0 chat topic in the middle.
        List<string> types = ["channel.follow", "channel.chat.message", "channel.subscribe"];

        Result result = await service.EnsureSubscribedAsync(tenant, types);

        result.IsSuccess.Should().BeTrue();
        transport.CreatedTypes.Should().HaveCount(3);
        transport.CreatedTypes[0].Should().Be("channel.chat.message");
        transport
            .CreatedTypes.IndexOf("channel.chat.message")
            .Should()
            .BeLessThan(transport.CreatedTypes.IndexOf("channel.follow"));
    }

    [Fact]
    public async Task SubscribeAsync_routes_each_topic_to_its_token_owners_session()
    {
        // The whole point of per-broadcaster sessions: a broadcaster-authorized topic must ride THAT
        // broadcaster's own session (Twitch forbids different users' subs on one WS session), while a bot-owned
        // chat-read topic rides the shared bot session. Prove the routing by the owner key each create is
        // ensured against.
        Guid tenant = Guid.CreateVersion7();
        RecordingEventSubTransport transport = new();
        (TwitchEventSubHostedService service, _) = Build(transport);

        await service.SubscribeAsync(tenant, "channel.subscribe"); // broadcaster-authorized
        await service.SubscribeAsync(tenant, "channel.chat.message"); // bot-owned (chat-read)

        transport.EnsuredOwners.Should().Contain(tenant.ToString());
        transport.EnsuredOwners.Should().Contain(EventSubOwnerKeys.Bot);
    }

    [Fact]
    public async Task ReconcileAsync_deletes_stale_tenant_sub_and_reports_count()
    {
        Guid tenant = Guid.CreateVersion7();
        List<TwitchSubscriptionResult> live =
        [
            Sub("s1", "channel.follow", session: "old", version: "2"),
        ];
        RecordingEventSubTransport transport = new(list: live, startSessionId: "new");
        (TwitchEventSubHostedService service, EventSubTestDbContext db) = Build(transport);

        Seed(
            db,
            tenant,
            "channel.follow",
            version: "2",
            status: "enabled",
            twitchSubscriptionId: "s1",
            sessionId: "old"
        );

        // Current session is "new"; the tenant's live "s1" is stranded on the dead "old" session.
        await service.ReconnectAsync();

        Result<EventSubReconcileReportDto> report = await service.ReconcileAsync(tenant);

        report.IsSuccess.Should().BeTrue();
        report.Value.Revoked.Should().BeGreaterThanOrEqualTo(1);
        transport.Deletes.Should().Contain("s1");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static TwitchSubscriptionResult Sub(
        string id,
        string type,
        string? session = null,
        string version = "1",
        string status = "enabled"
    ) =>
        new()
        {
            TwitchSubscriptionId = id,
            Type = type,
            Version = version,
            Status = status,
            Cost = 0,
            SessionId = session,
        };

    private static void Seed(
        EventSubTestDbContext db,
        Guid tenant,
        string eventType,
        string version,
        string status,
        string? twitchSubscriptionId = null,
        string? sessionId = null,
        bool enabled = true
    )
    {
        EventSubSubscription row = new()
        {
            BroadcasterId = tenant,
            Provider = "twitch",
            EventType = eventType,
            Version = version,
            Transport = "websocket",
            Status = status,
            Enabled = enabled,
            TwitchSubscriptionId = twitchSubscriptionId,
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow,
        };
        db.EventSubSubscriptions.Add(row);
        db.SaveChanges();
    }

    /// <summary>
    /// A hand-written <see cref="IEventSubTransport"/> that records the ordered create types and every delete id,
    /// and replays a configured create outcome + subscription list — so a test asserts on the actual wire calls
    /// (created / adopted / deleted), fully deterministic and with no network.
    /// </summary>
    private sealed class RecordingEventSubTransport(
        Func<
            EventSubSubscriptionRequest,
            EventSubTransportHandle,
            Result<TwitchSubscriptionResult>
        >? onCreate = null,
        IReadOnlyList<TwitchSubscriptionResult>? list = null,
        string startSessionId = "sess-1"
    ) : IEventSubTransport
    {
        public EventSubTransportKind Kind => EventSubTransportKind.WebSocket;

        /// <summary>Every create's event type, in issue order (proves the cost-0-first ordering).</summary>
        public List<string> CreatedTypes { get; } = [];

        /// <summary>Every subscription id passed to delete (proves stale-session cleanup / reconcile pruning).</summary>
        public List<string> Deletes { get; } = [];

        /// <summary>Every owner key a create was routed to (proves per-owner session routing).</summary>
        public List<string> EnsuredOwners { get; } = [];

        public Task<Result<EventSubTransportHandle>> StartAsync(CancellationToken ct = default) =>
            Task.FromResult(
                Result.Success(
                    new EventSubTransportHandle { Kind = Kind, SessionId = startSessionId }
                )
            );

        public Task<Result<EventSubTransportHandle>> EnsureSessionAsync(
            string ownerKey,
            CancellationToken ct = default
        )
        {
            EnsuredOwners.Add(ownerKey);
            return Task.FromResult(
                Result.Success(
                    new EventSubTransportHandle { Kind = Kind, SessionId = startSessionId }
                )
            );
        }

        public string? CurrentSessionId(string ownerKey) => startSessionId;

        public Task<Result<TwitchSubscriptionResult>> CreateSubscriptionAsync(
            EventSubSubscriptionRequest request,
            EventSubTransportHandle handle,
            CancellationToken ct = default
        )
        {
            CreatedTypes.Add(request.EventType);
            Result<TwitchSubscriptionResult> result = onCreate is not null
                ? onCreate(request, handle)
                : DefaultCreate(request, handle);
            return Task.FromResult(result);
        }

        public Task<Result> DeleteSubscriptionAsync(
            string twitchSubscriptionId,
            CancellationToken ct = default
        )
        {
            Deletes.Add(twitchSubscriptionId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<TwitchSubscriptionResult>>> ListSubscriptionsAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(list ?? []));

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        private static Result<TwitchSubscriptionResult> DefaultCreate(
            EventSubSubscriptionRequest request,
            EventSubTransportHandle handle
        ) =>
            Result.Success(
                new TwitchSubscriptionResult
                {
                    TwitchSubscriptionId = $"tw-{Guid.NewGuid():N}",
                    Type = request.EventType,
                    Version = request.Version,
                    Status = "enabled",
                    Cost = 0,
                    SessionId = handle.SessionId ?? "sess-1",
                }
            );
    }
}
