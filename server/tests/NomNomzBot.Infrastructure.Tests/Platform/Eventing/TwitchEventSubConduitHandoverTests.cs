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
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Interfaces;
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
/// The zero-downtime blue/green handover in conduit mode (twitch-eventsub.md §10), driven through two real
/// hosted services and two real shard coordinators over ONE shared Twitch model, database and lease store —
/// the same three things two colours share in production. Each test asserts what Twitch would deliver to at
/// the moment it matters: the outgoing instance never closes its shard while no other shard is enabled.
/// </summary>
public sealed class TwitchEventSubConduitHandoverTests : IDisposable
{
    private const string ChatTopic = "channel.chat.message";
    private static readonly Guid Channel = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");

    private readonly EventSubTestDbContext _db = EventSubTestDbContext.New();
    private readonly FakeTwitchConduits _twitch = new();
    private readonly ConcurrentDictionary<string, byte> _leases = new();

    public void Dispose() => _db.Dispose();

    private (TwitchEventSubHostedService Service, ShardTransport Transport) NewInstance(
        string name,
        Func<EventSubTransportHandle, Result<TwitchSubscriptionResult>>? onCreate = null
    )
    {
        ITwitchIdentityResolver resolver = Substitute.For<ITwitchIdentityResolver>();
        resolver
            .GetTwitchChannelIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("1234");
        IPlatformBotReadinessGate gate = Substitute.For<IPlatformBotReadinessGate>();
        gate.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(_db)
            .AddSingleton<ITwitchEventSubConduitsApi>(_twitch)
            .AddScoped<IRunOnceGuard>(_ => new SharedLeaseStore(_leases))
            .AddScoped(_ => resolver)
            .AddScoped(_ => gate)
            .BuildServiceProvider();
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();

        ShardTransport transport = new(name, _twitch, onCreate);
        TwitchEventSubHostedService service = new(
            scopes,
            transport,
            new EventSubConditionBuilder(),
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<TwitchEventSubHostedService>.Instance,
            new EventSubConduitShardCoordinator(
                scopes,
                TimeProvider.System,
                NullLogger<EventSubConduitShardCoordinator>.Instance
            )
        );
        transport.Service = service;
        return (service, transport);
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

        // Overlap: both colours hold a shard, and the standby is still a standby.
        _twitch.EnabledShardCount(ConduitId).Should().Be(2);
        green.IsActiveInstance.Should().BeFalse();

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
