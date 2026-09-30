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
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// The zero-downtime blue/green handover in conduit mode (twitch-eventsub.md §10), driven through two real
/// hosted services and two real shard coordinators over ONE shared Twitch model, database and lease store —
/// the same three things two colours share in production. Each test asserts what Twitch would deliver to at
/// the moment it matters: the outgoing instance never closes its shard while no other shard is enabled.
/// </summary>
public sealed class TwitchEventSubConduitHandoverTests : IDisposable
{
    private const string ChatTopic = "channel.chat.message";
    private static readonly Guid Channel = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");

    // One named in-memory database, one context per DI scope — as in production, where the two colours'
    // drains and receivers hit the shared database from different threads at once. _db keeps it alive.
    private readonly string _dbName = $"handover-{Guid.NewGuid():N}";
    private readonly EventSubTestDbContext _db;
    private readonly FakeTwitchConduits _twitch = new();
    private readonly ConcurrentDictionary<string, byte> _leases = new();

    public TwitchEventSubConduitHandoverTests() => _db = EventSubTestDbContext.Shared(_dbName);

    public void Dispose() => _db.Dispose();

    private (TwitchEventSubHostedService Service, ShardTransport Transport) NewInstance(
        string name,
        Func<EventSubTransportHandle, Result<TwitchSubscriptionResult>>? onCreate = null,
        RecordingDispatcher? dispatcher = null,
        bool sharedInbox = true,
        TimeProvider? serviceClock = null
    )
    {
        ITwitchIdentityResolver resolver = Substitute.For<ITwitchIdentityResolver>();
        resolver
            .GetTwitchChannelIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("1234");
        resolver.GetBroadcasterIdAsync("1234", Arg.Any<CancellationToken>()).Returns(Channel);
        IPlatformBotReadinessGate gate = Substitute.For<IPlatformBotReadinessGate>();
        gate.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        RecordingDispatcher processed = dispatcher ?? new RecordingDispatcher();

        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IApplicationDbContext>(_ => EventSubTestDbContext.Shared(_dbName))
            .AddSingleton<ITwitchEventSubConduitsApi>(_twitch)
            .AddScoped<IRunOnceGuard>(_ => new SharedLeaseStore(_leases))
            .AddScoped(_ => resolver)
            .AddScoped(_ => gate)
            .AddScoped<INotificationDispatcher>(_ => processed)
            .BuildServiceProvider();
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();

        ShardTransport transport = new(name, _twitch, onCreate);
        TwitchEventSubHostedService service = new(
            scopes,
            transport,
            new EventSubConditionBuilder(),
            Substitute.For<IEventBus>(),
            serviceClock ?? TimeProvider.System,
            NullLogger<TwitchEventSubHostedService>.Instance,
            new EventSubConduitShardCoordinator(
                scopes,
                TimeProvider.System,
                new OutboundSanctionAccessor(),
                NullLogger<EventSubConduitShardCoordinator>.Instance
            ),
            sharedInbox ? new DatabaseEventSubInbox(scopes) : null
        );
        transport.Service = service;
        return (service, transport);
    }

    /// <summary>A notification as the transport hands it over after reading it off a shard socket.</summary>
    private static Task ReceiveAsync(TwitchEventSubHostedService receiver, string messageId)
    {
        using JsonDocument payload = JsonDocument.Parse(
            """{"broadcaster_user_id":"1234","message":{"text":"!sr never gonna give you up"}}"""
        );
        return receiver.OnNotificationAsync(
            messageId,
            DateTimeOffset.UtcNow,
            ChatTopic,
            "1",
            "1234",
            payload.RootElement.Clone(),
            CancellationToken.None
        );
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(50, timeout.Token);
    }

    [Fact]
    public async Task A_notification_on_the_standby_shard_is_processed_once_by_the_active_instance()
    {
        RecordingDispatcher blueProcessed = new();
        RecordingDispatcher greenProcessed = new();
        (TwitchEventSubHostedService blue, _) = NewInstance("blue", dispatcher: blueProcessed);
        await StartAsync(blue);
        (TwitchEventSubHostedService green, _) = NewInstance("green", dispatcher: greenProcessed);
        await StartAsync(green);

        // A !sr lands on the standby's shard during the overlap. The standby must not run it: its song
        // queue, cooldowns and caches are not the live ones — the active instance's are.
        await ReceiveAsync(green, "msg-sr");
        await WaitUntilAsync(() => blueProcessed.MessageIds.Count == 1);
        await Task.Delay(600); // a further drain cycle on both sides: nothing may run it a second time

        blueProcessed.MessageIds.Should().Equal("msg-sr");
        greenProcessed.MessageIds.Should().BeEmpty();
        (await _db.Set<EventSubInboxMessage>().AsNoTracking().CountAsync()).Should().Be(0);

        await blue.StopAsync(CancellationToken.None);
        await green.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_incoming_instance_reports_ready_only_once_its_shard_is_bound_or_the_wait_runs_out()
    {
        (TwitchEventSubHostedService blue, _) = NewInstance("blue");
        await StartAsync(blue);
        // A third session squats the free shard: the incoming instance cannot bind one.
        _twitch.BindOnTwitch(ConduitId, "1", "squatter");
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (TwitchEventSubHostedService green, _) = NewInstance("green", serviceClock: clock);

        await StartAsync(green);

        // The deploy must not stop blue yet: green would receive nothing.
        green.IsReadyForHandover.Should().BeFalse();
        clock.Advance(TwitchEventSubHostedService.ShardReadyTimeout);
        // A Twitch outage cannot hold the deploy forever; blue's own bounded wait still protects it.
        green.IsReadyForHandover.Should().BeTrue();

        await green.StopAsync(CancellationToken.None);
        await blue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Reconcile_recreates_a_conduit_subscription_Twitch_no_longer_lists()
    {
        (TwitchEventSubHostedService service, ShardTransport wire) = NewInstance("solo");
        await StartAsync(service);
        await service.SubscribeAsync(Channel, ChatTopic);
        EventSubSubscription before = await _db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        wire.Creates.Clear();

        // Twitch's conduit listing no longer has it (removed without a revocation reaching us).
        Result<EventSubReconcileReportDto> report = await service.ReconcileAsync(Channel);

        report.Value.Created.Should().Be(1);
        wire.Creates.Should().Equal($"{ChatTopic} on conduit {ConduitId}");
        EventSubSubscription after = await _db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        after.TwitchSubscriptionId.Should().NotBe(before.TwitchSubscriptionId);
        (after.Status, after.ConduitId).Should().Be(("enabled", ConduitId));
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Reconcile_keeps_a_conduit_subscription_Twitch_still_lists()
    {
        (TwitchEventSubHostedService service, ShardTransport wire) = NewInstance("solo");
        await StartAsync(service);
        await service.SubscribeAsync(Channel, ChatTopic);
        EventSubSubscription row = await _db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        wire.ConduitListed.Add(
            new()
            {
                TwitchSubscriptionId = row.TwitchSubscriptionId!,
                Type = ChatTopic,
                Version = "1",
                Status = "enabled",
                Cost = 0,
                ConduitId = ConduitId,
            }
        );
        wire.Creates.Clear();

        Result<EventSubReconcileReportDto> report = await service.ReconcileAsync(Channel);

        (report.Value.Created, report.Value.Revoked).Should().Be((0, 0));
        wire.Creates.Should().BeEmpty();
        wire.Steps.Should().NotContain(s => s.StartsWith("delete"));
        await service.StopAsync(CancellationToken.None);
    }

    private static IntegrationTokenRefreshedEvent TokenRefreshed() =>
        new()
        {
            BroadcasterId = Channel,
            ConnectionId = Guid.NewGuid(),
            Provider = "twitch",
            ExpiresAt = DateTime.UtcNow.AddHours(4),
        };

    [Fact]
    public async Task Re_auth_recovery_on_a_standby_touches_no_subscription_and_opens_no_session()
    {
        // Production, 2026-09-29: the incoming colour's token-refresh recovery opened a broadcaster session
        // and deleted 28 of the LIVE colour's subscriptions ~20 s into the overlap.
        (TwitchEventSubHostedService blue, _) = NewInstance("blue");
        await StartAsync(blue);
        (TwitchEventSubHostedService green, ShardTransport greenWire) = NewInstance("green");
        await StartAsync(green);

        await new EventSubResubscribeOnTokenRefreshedHandler(
            green,
            NullLogger<EventSubResubscribeOnTokenRefreshedHandler>.Instance
        ).HandleAsync(TokenRefreshed());

        greenWire.Steps.Should().BeEmpty();
        greenWire.EnsuredOwners.Should().OnlyContain(o => o == EventSubOwnerKeys.ConduitShard);
        (await _db.EventSubSubscriptions.AsNoTracking().CountAsync()).Should().Be(0);
        await blue.StopAsync(CancellationToken.None);
        await green.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Re_auth_recovery_before_the_lease_is_decided_touches_nothing()
    {
        // The gate defaults to closed: until StartAsync knows whether this instance is the standby, nothing
        // may open a session, create or delete.
        (TwitchEventSubHostedService undecided, ShardTransport wire) = NewInstance("booting");

        await new EventSubResubscribeOnTokenRefreshedHandler(
            undecided,
            NullLogger<EventSubResubscribeOnTokenRefreshedHandler>.Instance
        ).HandleAsync(TokenRefreshed());

        undecided.IsActiveInstance.Should().BeFalse();
        wire.Steps.Should().BeEmpty();
        wire.EnsuredOwners.Should().BeEmpty();
        (await undecided.ReconcileAsync(Channel)).IsFailure.Should().BeTrue();
        (await undecided.UnsubscribeAllAsync(Channel)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_lone_instance_is_ready_at_once_even_with_its_shard_unbound()
    {
        // Both shards are taken by other sessions, so this instance cannot bind one — yet it holds the lease,
        // so it is the only upstream behind the proxy: holding readiness back would 503 the whole port.
        _twitch.Seed("conduit-pre", 2);
        _db.EventSubConduits.Add(new EventSubConduit { ConduitId = "conduit-pre", ShardCount = 2 });
        await _db.SaveChangesAsync();
        _twitch.BindOnTwitch("conduit-pre", "0", "squatter-a");
        _twitch.BindOnTwitch("conduit-pre", "1", "squatter-b");
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        (TwitchEventSubHostedService lone, _) = NewInstance("lone", serviceClock: clock);

        await StartAsync(lone);

        lone.IsActiveInstance.Should().BeTrue();
        _twitch.Shard("conduit-pre", "0").SessionId.Should().Be("squatter-a");
        _twitch.Shard("conduit-pre", "1").SessionId.Should().Be("squatter-b");
        lone.IsReadyForHandover.Should().BeTrue();
        await lone.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_resent_notification_waits_in_the_inbox_once()
    {
        // Twitch resends a disabled shard's notification to the other shard; while the first copy is still
        // waiting, the second must not queue a second dispatch. (Once processed, the journal's
        // Unique(EventId) is the guard.)
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IApplicationDbContext>(_ => EventSubTestDbContext.Shared(_dbName))
            .BuildServiceProvider();
        DatabaseEventSubInbox inbox = new(provider.GetRequiredService<IServiceScopeFactory>());

        bool first = await inbox.EnqueueAsync(Message("msg-twice"));
        bool second = await inbox.EnqueueAsync(Message("msg-twice"));

        (first, second).Should().Be((true, false));
        (await inbox.PeekAsync(10)).Select(m => m.MessageId).Should().Equal("msg-twice");
    }

    private static EventSubInboxMessage Message(string messageId) =>
        new()
        {
            MessageId = messageId,
            MessageTimestamp = DateTime.UtcNow,
            SubscriptionType = ChatTopic,
            SubscriptionVersion = "1",
            TwitchBroadcasterUserId = "1234",
            EventJson = "{}",
            ReceivedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task Notifications_still_waiting_when_the_old_instance_stops_are_processed_by_its_successor()
    {
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingDispatcher blueProcessed = new(firstDispatchGate: releaseFirst.Task);
        RecordingDispatcher greenProcessed = new();
        (TwitchEventSubHostedService blue, _) = NewInstance("blue", dispatcher: blueProcessed);
        await StartAsync(blue);
        (TwitchEventSubHostedService green, _) = NewInstance("green", dispatcher: greenProcessed);
        await StartAsync(green);

        // Blue is mid-dispatch on the first when the deploy stops it; the second is still waiting.
        await ReceiveAsync(green, "msg-1");
        await WaitUntilAsync(() => blueProcessed.Started == 1);
        await ReceiveAsync(green, "msg-2");
        Task stopping = blue.StopAsync(CancellationToken.None);
        await Task.Delay(300);
        releaseFirst.SetResult();
        await stopping;

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await green.WaitUntilActiveAsync(timeout.Token);
        await WaitUntilAsync(() => greenProcessed.MessageIds.Count == 1);

        // The one in hand finished on blue; the waiting one went to green. Each exactly once.
        blueProcessed.MessageIds.Should().Equal("msg-1");
        greenProcessed.MessageIds.Should().Equal("msg-2");
        (await _db.Set<EventSubInboxMessage>().AsNoTracking().CountAsync()).Should().Be(0);
        await green.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_single_instance_without_a_shared_inbox_processes_in_process_at_once()
    {
        RecordingDispatcher processed = new();
        (TwitchEventSubHostedService lone, _) = NewInstance(
            "lone",
            dispatcher: processed,
            sharedInbox: false
        );
        await StartAsync(lone);

        await ReceiveAsync(lone, "msg-direct");

        // Dispatched inside the receive call itself — no queue, no second instance to hand it to.
        processed.MessageIds.Should().Equal("msg-direct");
        (await _db.Set<EventSubInboxMessage>().AsNoTracking().CountAsync()).Should().Be(0);
        await lone.StopAsync(CancellationToken.None);
    }

    /// <summary>Records what an instance actually dispatched; can hold the first dispatch open.</summary>
    private sealed class RecordingDispatcher(Task? firstDispatchGate = null)
        : INotificationDispatcher
    {
        private readonly List<string> _messageIds = [];
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public IReadOnlyList<string> MessageIds
        {
            get
            {
                lock (_messageIds)
                    return [.. _messageIds];
            }
        }

        public async Task<Result<NotificationDispatchResult>> DispatchAsync(
            EventSubNotification notification,
            CancellationToken ct = default
        )
        {
            if (Interlocked.Increment(ref _started) == 1 && firstDispatchGate is not null)
                await firstDispatchGate;
            lock (_messageIds)
                _messageIds.Add(notification.MessageId);
            return Result.Success(new NotificationDispatchResult(Guid.NewGuid(), 0, false));
        }
    }

    private static async Task StartAsync(TwitchEventSubHostedService service)
    {
        await service.StartAsync(CancellationToken.None);
        await service.WhenWelcomeWorkIdleAsync();
    }

    private string ConduitId => _db.EventSubConduits.Single().ConduitId;

    [Fact]
    public async Task The_outgoing_instance_closes_its_shard_only_after_the_successor_shard_is_enabled()
    {
        (TwitchEventSubHostedService blue, _) = NewInstance("blue");
        await StartAsync(blue);
        (TwitchEventSubHostedService green, _) = NewInstance("green");
        await StartAsync(green);

        // Overlap: both colours hold a shard, and the standby is still a standby — but ready to take over.
        _twitch.EnabledShardCount(ConduitId).Should().Be(2);
        green.IsActiveInstance.Should().BeFalse();
        green.IsReadyForHandover.Should().BeTrue();

        await blue.StopAsync(CancellationToken.None);

        // Twitch still delivers: blue's shard is disabled (its events are resent to green's), green's is live.
        _twitch.EnabledShardCount(ConduitId).Should().Be(1);
        _twitch.Calls.Should().Contain("blue closes with 2 enabled shard(s)");

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await green.WaitUntilActiveAsync(timeout.Token);
        green.IsActiveInstance.Should().BeTrue();
        await green.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_outgoing_instance_waits_for_a_successor_whose_shard_is_not_bound_yet()
    {
        (TwitchEventSubHostedService blue, _) = NewInstance("blue");
        await StartAsync(blue);

        // The successor is queued (holds the standby claim) but its shard session has not welcomed yet.
        await using IAsyncDisposable standbyClaim = (
            await new SharedLeaseStore(_leases).TryAcquireAsync(
                "eventsub-chat-ingest-standby",
                TimeSpan.FromMinutes(1)
            )
        )!;

        Task stopping = blue.StopAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        _twitch.Calls.Should().NotContain(c => c.StartsWith("blue closes"));

        (TwitchEventSubHostedService green, _) = NewInstance("green");
        await StartAsync(green);
        await stopping;

        // Green's shard was bound BEFORE blue closed, so at the close two shards were enabled.
        List<string> calls = _twitch.Calls;
        int greenBound = calls.FindIndex(c => c.Contains("->green-shard"));
        int blueClosed = calls.FindIndex(c => c.StartsWith("blue closes"));
        greenBound.Should().BeGreaterThan(-1);
        blueClosed.Should().BeGreaterThan(greenBound);
        calls[blueClosed].Should().Be("blue closes with 2 enabled shard(s)");

        await green.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_lone_instance_stops_at_once_so_the_outage_is_announced()
    {
        (TwitchEventSubHostedService lone, _) = NewInstance("lone");
        await StartAsync(lone);

        (await lone.HasWaitingSuccessorAsync(CancellationToken.None)).Should().BeFalse();
        _twitch.Calls.Clear();
        await lone.StopAsync(CancellationToken.None);

        // No successor: it never polls the shards, it closes straight away.
        _twitch.Calls.Should().Equal("lone closes with 1 enabled shard(s)");
    }

    [Fact]
    public async Task A_successful_handover_is_not_announced_because_the_successor_is_seen()
    {
        (TwitchEventSubHostedService blue, _) = NewInstance("blue");
        await StartAsync(blue);
        (TwitchEventSubHostedService green, _) = NewInstance("green");
        await StartAsync(green);

        // BotShutdownAnnouncementService posts the "going offline" line only when this answers false.
        (await blue.HasWaitingSuccessorAsync(CancellationToken.None))
            .Should()
            .BeTrue();

        await blue.StopAsync(CancellationToken.None);
        await green.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_successor_adopts_conduit_subscriptions_so_no_topic_exists_twice()
    {
        (TwitchEventSubHostedService blue, ShardTransport blueWire) = NewInstance("blue");
        await StartAsync(blue);
        (await blue.SubscribeAsync(Channel, ChatTopic)).IsSuccess.Should().BeTrue();
        blueWire.Creates.Should().Equal($"{ChatTopic} on conduit {ConduitId}");

        (TwitchEventSubHostedService green, ShardTransport greenWire) = NewInstance("green");
        await StartAsync(green);

        // During the overlap the standby creates nothing — a second subscription would deliver twice.
        (await green.SubscribeAsync(Channel, ChatTopic))
            .IsFailure.Should()
            .BeTrue();

        await blue.StopAsync(CancellationToken.None);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await green.WaitUntilActiveAsync(timeout.Token);

        // The takeover reconcile finds the topic live on the conduit and makes no Twitch call for it.
        Result<EventSubSubscriptionDto> adopted = await green.SubscribeAsync(Channel, ChatTopic);
        adopted.Value.Status.Should().Be("enabled");
        greenWire.Creates.Should().BeEmpty();
        greenWire.EnsuredOwners.Should().NotContain(EventSubOwnerKeys.Bot);

        await green.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_row_still_on_a_websocket_session_is_deleted_there_before_it_moves_to_the_conduit()
    {
        _db.EventSubSubscriptions.Add(
            new EventSubSubscription
            {
                BroadcasterId = Channel,
                Provider = "twitch",
                EventType = ChatTopic,
                Version = new EventSubConditionBuilder().GetVersion(ChatTopic),
                Condition = new(),
                Transport = "websocket",
                Status = "enabled",
                Enabled = true,
                TwitchSubscriptionId = "ws-sub-1",
                SessionId = "old-version-session",
            }
        );
        await _db.SaveChangesAsync();
        (TwitchEventSubHostedService service, ShardTransport wire) = NewInstance("new");
        await StartAsync(service);

        await service.SubscribeAsync(Channel, ChatTopic);

        wire.Steps.Should()
            .ContainInOrder("delete ws-sub-1 as bot", $"create {ChatTopic} on conduit {ConduitId}");
        EventSubSubscription row = await _db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        (row.ConduitId, row.SessionId, row.Transport).Should().Be((ConduitId, null, "conduit"));
        await service.StopAsync(CancellationToken.None);
    }

    private static Result<TwitchSubscriptionResult> Conflict(string existingId) =>
        Result.Failure<TwitchSubscriptionResult>(
            "Twitch request failed (409).",
            TwitchErrorCodes.Conflict,
            $"subscription already exists; id={existingId}"
        );

    [Fact]
    public async Task A_409_against_a_subscription_live_on_our_conduit_adopts_it_and_stops_posting()
    {
        // Dev box, 2026-09-30: ten rows sat `pending` with "Twitch request failed (409)" and were re-POSTed
        // every five minutes, while Twitch held every one of them live on our conduit under an id no row
        // knew. The 409 names that id; the row must take it, not park.
        (TwitchEventSubHostedService service, ShardTransport wire) = NewInstance(
            "solo",
            handle =>
                handle.Kind == EventSubTransportKind.Conduit
                    ? Conflict("live-on-conduit")
                    : ShardTransport.Created(handle)
        );
        await StartAsync(service);
        wire.ConduitListed.Add(
            new()
            {
                TwitchSubscriptionId = "live-on-conduit",
                Type = ChatTopic,
                Version = "1",
                Status = "enabled",
                Cost = 0,
                ConduitId = ConduitId,
            }
        );

        Result<EventSubSubscriptionDto> subscribed = await service.SubscribeAsync(
            Channel,
            ChatTopic
        );

        subscribed.IsSuccess.Should().BeTrue(subscribed.ErrorMessage);
        subscribed.Value.TwitchSubscriptionId.Should().Be("live-on-conduit");
        EventSubSubscription row = await _db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        (row.Status, row.TwitchSubscriptionId, row.ConduitId, row.Transport, row.LastError)
            .Should()
            .Be(("enabled", "live-on-conduit", ConduitId, "conduit", null));
        wire.Steps.Should()
            .Equal($"create {ChatTopic} on conduit {ConduitId}", "get live-on-conduit as app");

        // Healed: the next reconcile finds the row bound to what Twitch lists and posts nothing.
        wire.Steps.Clear();
        Result<EventSubReconcileReportDto> report = await service.ReconcileAsync(Channel);
        report.Value.Created.Should().Be(0);
        wire.Steps.Should().BeEmpty();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_409_against_a_stale_websocket_subscription_deletes_it_and_lands_on_the_conduit()
    {
        // The row forgot its old WebSocket subscription, but Twitch still holds it under the same key. The
        // app token cannot see a WebSocket subscription; the creating user's token can. Delete it as that
        // user, then the conduit create goes through.
        int conduitCreates = 0;
        (TwitchEventSubHostedService service, ShardTransport wire) = NewInstance(
            "solo",
            handle =>
                handle.Kind == EventSubTransportKind.Conduit && ++conduitCreates == 1
                    ? Conflict("ws-stale")
                    : ShardTransport.Created(handle)
        );
        await StartAsync(service);
        wire.UserListed.Add(
            new()
            {
                TwitchSubscriptionId = "ws-stale",
                Type = ChatTopic,
                Version = "1",
                Status = "enabled",
                Cost = 0,
                SessionId = "an-older-session",
            }
        );

        Result<EventSubSubscriptionDto> subscribed = await service.SubscribeAsync(
            Channel,
            ChatTopic
        );

        subscribed.IsSuccess.Should().BeTrue(subscribed.ErrorMessage);
        wire.Steps.Should()
            .Equal(
                $"create {ChatTopic} on conduit {ConduitId}",
                "get ws-stale as app",
                "get ws-stale as bot",
                "delete ws-stale as bot",
                $"create {ChatTopic} on conduit {ConduitId}"
            );
        EventSubSubscription row = await _db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        (row.Status, row.ConduitId, row.Transport, row.SessionId, row.LastError)
            .Should()
            .Be(("enabled", ConduitId, "conduit", null, null));
        row.TwitchSubscriptionId.Should().NotBe("ws-stale");
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_concurrent_winners_enabled_row_is_not_downgraded_by_the_losers_409()
    {
        // At a takeover the channel sync and the onboarding seed both create the same topic. The winner
        // saves its row enabled; the loser's 409 arrives against a tracked copy that still says pending,
        // and used to write that copy back over the winner's binding.
        (TwitchEventSubHostedService service, ShardTransport wire) = NewInstance(
            "solo",
            handle =>
            {
                if (handle.Kind != EventSubTransportKind.Conduit)
                    return ShardTransport.Created(handle);
                // The winner lands while this create is in flight, on its own context as in production.
                using EventSubTestDbContext winner = EventSubTestDbContext.Shared(_dbName);
                EventSubSubscription saved = winner.EventSubSubscriptions.Single();
                saved.Status = "enabled";
                saved.TwitchSubscriptionId = "winner-sub";
                saved.ConduitId = handle.ConduitId;
                saved.Transport = "conduit";
                winner.SaveChanges();
                return Conflict("winner-sub");
            }
        );
        await StartAsync(service);

        Result<EventSubSubscriptionDto> loser = await service.SubscribeAsync(Channel, ChatTopic);

        loser.IsSuccess.Should().BeTrue(loser.ErrorMessage);
        loser.Value.TwitchSubscriptionId.Should().Be("winner-sub");
        EventSubSubscription row = await _db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        (row.Status, row.TwitchSubscriptionId, row.ConduitId, row.LastError)
            .Should()
            .Be(("enabled", "winner-sub", ConduitId, null));
        // The database already answered; no lookup, no second create.
        wire.Steps.Should().Equal($"create {ChatTopic} on conduit {ConduitId}");
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_topic_the_conduit_refuses_for_a_grant_keeps_working_on_the_owner_session()
    {
        (TwitchEventSubHostedService service, ShardTransport wire) = NewInstance(
            "solo",
            handle =>
                handle.Kind == EventSubTransportKind.Conduit
                    ? Result.Failure<TwitchSubscriptionResult>(
                        "Forbidden",
                        TwitchErrorCodes.TwitchError,
                        "subscription missing proper authorization"
                    )
                    : ShardTransport.Created(handle)
        );
        await StartAsync(service);

        Result<EventSubSubscriptionDto> subscribed = await service.SubscribeAsync(
            Channel,
            ChatTopic
        );

        subscribed.Value.Status.Should().Be("enabled");
        wire.EnsuredOwners.Should().Contain(EventSubOwnerKeys.Bot);
        EventSubSubscription row = await _db.EventSubSubscriptions.AsNoTracking().SingleAsync();
        (row.Transport, row.ConduitId, row.SessionId).Should().Be(("websocket", null, "solo-bot"));
        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// A transport that behaves like the real one where it matters here: the shard session "welcomes" on open
    /// (which triggers the hosted service's shard claim), and closing a session is Twitch seeing the socket go —
    /// its shard turns disabled. Every step lands on the shared Twitch timeline.
    /// </summary>
    private sealed class ShardTransport(
        string name,
        FakeTwitchConduits twitch,
        Func<EventSubTransportHandle, Result<TwitchSubscriptionResult>>? onCreate
    ) : IEventSubTransport
    {
        private readonly ConcurrentDictionary<string, string> _sessions = new();
        private int _shardSessions;

        public TwitchEventSubHostedService? Service { get; set; }
        public List<string> Creates { get; } = [];
        public List<string> Steps { get; } = [];
        public List<string> EnsuredOwners { get; } = [];

        public EventSubTransportKind Kind => EventSubTransportKind.WebSocket;
        public IReadOnlyCollection<string> KnownOwnerKeys => [.. _sessions.Keys];

        public Task<Result<EventSubTransportHandle>> StartAsync(CancellationToken ct = default) =>
            EnsureSessionAsync(EventSubOwnerKeys.Bot, ct);

        public async Task<Result<EventSubTransportHandle>> EnsureSessionAsync(
            string ownerKey,
            CancellationToken ct = default
        )
        {
            lock (EnsuredOwners)
                EnsuredOwners.Add(ownerKey);
            if (!_sessions.TryGetValue(ownerKey, out string? sessionId))
            {
                sessionId =
                    ownerKey == EventSubOwnerKeys.ConduitShard
                        ? $"{name}-shard-{Interlocked.Increment(ref _shardSessions)}"
                        : $"{name}-{ownerKey}";
                _sessions[ownerKey] = sessionId;
                // Only the shard welcome matters here (it triggers the claim); an owner session's welcome
                // work is covered by the reconnect tests.
                if (ownerKey == EventSubOwnerKeys.ConduitShard)
                    await Service!.OnSessionWelcomeAsync(
                        sessionId,
                        ownerKey,
                        null,
                        CancellationToken.None
                    );
            }
            return Result.Success(
                new EventSubTransportHandle { Kind = Kind, SessionId = sessionId }
            );
        }

        public string? CurrentSessionId(string ownerKey) =>
            _sessions.TryGetValue(ownerKey, out string? id) ? id : null;

        public Task<Result<TwitchSubscriptionResult>> CreateSubscriptionAsync(
            EventSubSubscriptionRequest request,
            EventSubTransportHandle handle,
            CancellationToken ct = default
        )
        {
            string where =
                handle.Kind == EventSubTransportKind.Conduit
                    ? $"conduit {handle.ConduitId}"
                    : $"session {handle.SessionId}";
            Result<TwitchSubscriptionResult> result = onCreate?.Invoke(handle) ?? Created(handle);
            if (result.IsSuccess)
                Creates.Add($"{request.EventType} on {where}");
            Steps.Add($"create {request.EventType} on {where}");
            return Task.FromResult(result);
        }

        public static Result<TwitchSubscriptionResult> Created(EventSubTransportHandle handle) =>
            Result.Success(
                new TwitchSubscriptionResult
                {
                    TwitchSubscriptionId = $"tw-{Guid.NewGuid():N}",
                    Type = ChatTopic,
                    Version = "1",
                    Status = "enabled",
                    Cost = 0,
                    SessionId = handle.SessionId,
                    ConduitId = handle.ConduitId,
                }
            );

        public Task<Result> DeleteSubscriptionAsync(
            string twitchSubscriptionId,
            Guid? ownerBroadcasterId = null,
            CancellationToken ct = default
        )
        {
            Steps.Add(
                $"delete {twitchSubscriptionId} as {(ownerBroadcasterId is null ? "bot" : "broadcaster")}"
            );
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteConduitSubscriptionAsync(
            string twitchSubscriptionId,
            CancellationToken ct = default
        )
        {
            Steps.Add($"delete {twitchSubscriptionId} as app");
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<TwitchSubscriptionResult>>> ListSubscriptionsAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<TwitchSubscriptionResult>>([]));

        /// <summary>What Twitch reports as this app's conduit subscriptions (reconcile's live set).</summary>
        public List<TwitchSubscriptionResult> ConduitListed { get; } = [];

        /// <summary>What Twitch holds for a user token: this instance's WebSocket subscriptions.</summary>
        public List<TwitchSubscriptionResult> UserListed { get; } = [];

        public Task<Result<TwitchSubscriptionResult?>> GetSubscriptionAsync(
            string twitchSubscriptionId,
            Guid? ownerBroadcasterId = null,
            CancellationToken ct = default
        )
        {
            Steps.Add(
                $"get {twitchSubscriptionId} as {(ownerBroadcasterId is null ? "bot" : "broadcaster")}"
            );
            return Task.FromResult(
                Result.Success(
                    UserListed.FirstOrDefault(s => s.TwitchSubscriptionId == twitchSubscriptionId)
                )
            );
        }

        public Task<Result<TwitchSubscriptionResult?>> GetConduitSubscriptionAsync(
            string twitchSubscriptionId,
            CancellationToken ct = default
        )
        {
            Steps.Add($"get {twitchSubscriptionId} as app");
            return Task.FromResult(
                Result.Success(
                    ConduitListed.FirstOrDefault(s =>
                        s.TwitchSubscriptionId == twitchSubscriptionId
                    )
                )
            );
        }

        public Task<Result<IReadOnlyList<TwitchSubscriptionResult>>> ListConduitSubscriptionsAsync(
            string twitchUserId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success<IReadOnlyList<TwitchSubscriptionResult>>([.. ConduitListed])
            );

        public Task StopAsync(CancellationToken ct = default)
        {
            if (_sessions.TryGetValue(EventSubOwnerKeys.ConduitShard, out string? shardSession))
            {
                string conduit = twitch.ConduitIds.Single();
                twitch.Note(
                    $"{name} closes with {twitch.EnabledShardCount(conduit)} enabled shard(s)"
                );
                twitch.Disconnect(shardSession);
            }
            _sessions.Clear();
            return Task.CompletedTask;
        }
    }

    /// <summary>One lease store for every instance: the single database two colours contend over.</summary>
    private sealed class SharedLeaseStore(ConcurrentDictionary<string, byte> store) : IRunOnceGuard
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string resourceName,
            TimeSpan ttl,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<IAsyncDisposable?>(
                store.TryAdd(resourceName, 0) ? new Lease(store, resourceName) : null
            );

        private sealed class Lease(ConcurrentDictionary<string, byte> store, string name)
            : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                store.TryRemove(name, out _);
                return ValueTask.CompletedTask;
            }
        }
    }
}
