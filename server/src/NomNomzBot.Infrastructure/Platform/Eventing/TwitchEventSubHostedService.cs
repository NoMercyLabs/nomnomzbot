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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The EventSub lifecycle host (twitch-eventsub §3.2/§7): the <c>IHostedService</c> that drives the transport,
/// owns the subscription registry, and turns inbound wire frames into journaled, fanned-out facts via
/// <see cref="INotificationDispatcher"/>. It is the single instance behind <see cref="ITwitchEventSubService"/>
/// and <see cref="NomNomzBot.Application.Contracts.Platform.IEventSource"/>.
/// <para>
/// It resolves the tenant (Twitch id ⇒ Guid), persists the raw payload, and fans out via
/// <see cref="INotificationDispatcher"/> — which journals the raw event and publishes the strongly-typed
/// per-topic domain event(s) through the §3.7 translator registry. As a singleton it crosses to scoped services
/// (DbContext, resolver, dispatcher) through <see cref="IServiceScopeFactory"/>.
/// </para>
/// </summary>
public sealed class TwitchEventSubHostedService
    : ITwitchEventSubService,
        IEventSubNotificationSink,
        IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventSubTransport _transport;
    private readonly IEventSubConditionBuilder _conditionBuilder;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly ILogger<TwitchEventSubHostedService> _logger;

    private EventSubTransportHandle? _handle;
    private DateTimeOffset? _lastEventAt;
    private volatile int _activeSubscriptionCount;

    public TwitchEventSubHostedService(
        IServiceScopeFactory scopeFactory,
        IEventSubTransport transport,
        IEventSubConditionBuilder conditionBuilder,
        IEventBus eventBus,
        TimeProvider clock,
        ILogger<TwitchEventSubHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _transport = transport;
        _conditionBuilder = conditionBuilder;
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;

        if (_transport is WebSocketEventSubTransport ws)
            ws.BindSink(this);
    }

    public string Provider => "twitch";

    // ── IHostedService ──────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Result<EventSubTransportHandle> started = await _transport.StartAsync(cancellationToken);
        if (started.IsFailure)
        {
            _logger.LogWarning("EventSub transport failed to start: {Error}", started.ErrorMessage);
            return;
        }

        _handle = started.Value;
        _logger.LogInformation("EventSub transport started ({Kind})", _transport.Kind);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        _transport.StopAsync(cancellationToken);

    // ── IEventSubNotificationSink (called by the transport receive loop) ────

    public async Task OnSessionWelcomeAsync(string sessionId, CancellationToken ct)
    {
        _handle = new EventSubTransportHandle { Kind = _transport.Kind, SessionId = sessionId };
        _logger.LogInformation(
            "EventSub session welcome ({SessionId}) — re-homing subscriptions",
            sessionId
        );

        // A fresh welcome means every WS subscription is gone (sessions are not portable). Re-register the
        // whole enabled registry against the new session id, then announce the steady state.
        int active = await ReRegisterAllAsync(ct);
        _activeSubscriptionCount = active;

        await _eventBus.PublishAsync(
            new EventSubConnectedEvent
            {
                BroadcasterId = Guid.Empty,
                Transport = _transport.Kind,
                SessionId = sessionId,
                ActiveSubscriptionCount = active,
                Timestamp = _clock.GetUtcNow(),
            },
            ct
        );
    }

    public async Task OnNotificationAsync(
        string messageId,
        DateTimeOffset messageTimestamp,
        string subscriptionType,
        string subscriptionVersion,
        string twitchBroadcasterUserId,
        JsonElement @event,
        CancellationToken ct
    )
    {
        _lastEventAt = _clock.GetUtcNow();

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ITwitchIdentityResolver resolver =
            scope.ServiceProvider.GetRequiredService<ITwitchIdentityResolver>();

        Guid? tenant = await resolver.GetBroadcasterIdAsync(twitchBroadcasterUserId, ct);
        if (tenant is null || tenant == Guid.Empty)
        {
            _logger.LogDebug(
                "EventSub notification {Type} for unknown Twitch channel {TwitchId} — skipping",
                subscriptionType,
                twitchBroadcasterUserId
            );
            return;
        }

        EventSubNotification notification = new()
        {
            MessageId = messageId,
            MessageTimestamp = messageTimestamp,
            SubscriptionType = subscriptionType,
            SubscriptionVersion = subscriptionVersion,
            BroadcasterId = tenant.Value,
            TwitchBroadcasterUserId = twitchBroadcasterUserId,
            Event = @event,
        };

        INotificationDispatcher dispatcher =
            scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        Result<NotificationDispatchResult> dispatched = await dispatcher.DispatchAsync(
            notification,
            ct
        );
        if (dispatched.IsFailure)
            _logger.LogError(
                "EventSub dispatch failed for {Type}: {Error}",
                subscriptionType,
                dispatched.ErrorMessage
            );
    }

    public async Task OnRevocationAsync(
        string twitchSubscriptionId,
        string subscriptionType,
        string status,
        string twitchBroadcasterUserId,
        CancellationToken ct
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        EventSubSubscription? row = await db.EventSubSubscriptions.FirstOrDefaultAsync(
            s => s.TwitchSubscriptionId == twitchSubscriptionId,
            ct
        );

        Guid broadcasterId = row?.BroadcasterId ?? Guid.Empty;
        if (row is not null)
        {
            string old = row.Status;
            row.Status = "revoked";
            row.Enabled = false;
            row.LastError = status;
            await db.SaveChangesAsync(ct);

            await PublishStatusChangedAsync(row, old, "revoked", status, ct);
        }

        _logger.LogWarning(
            "EventSub subscription revoked: {SubId} ({Type}, {Status})",
            twitchSubscriptionId,
            subscriptionType,
            status
        );

        await _eventBus.PublishAsync(
            new EventSubRevokedEvent
            {
                BroadcasterId = broadcasterId,
                TwitchSubscriptionId = twitchSubscriptionId,
                EventType = subscriptionType,
                Status = status,
                Timestamp = _clock.GetUtcNow(),
            },
            ct
        );
    }

    // ── IEventSource ────────────────────────────────────────────────────────

    public async Task<Result> EnsureSubscribedAsync(
        Guid broadcasterId,
        IReadOnlyCollection<string> eventTypes,
        CancellationToken ct = default
    )
    {
        List<string> failures = [];
        foreach (string eventType in eventTypes)
        {
            Result<EventSubSubscriptionDto> result = await SubscribeAsync(
                broadcasterId,
                eventType,
                ct
            );
            if (result.IsFailure)
                failures.Add($"{eventType}: {result.ErrorMessage}");
        }

        return failures.Count == 0
            ? Result.Success()
            : Result.Failure(
                $"Failed to subscribe {failures.Count} event type(s).",
                "SERVICE_UNAVAILABLE",
                string.Join("; ", failures)
            );
    }

    public async Task<Result> UnsubscribeAllAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<EventSubSubscription> rows = await db
            .EventSubSubscriptions.Where(s => s.BroadcasterId == broadcasterId)
            .ToListAsync(ct);

        foreach (EventSubSubscription row in rows)
        {
            if (row.TwitchSubscriptionId is { } id)
                await _transport.DeleteSubscriptionAsync(id, ct);

            string old = row.Status;
            row.Status = "revoked";
            row.Enabled = false;
            row.DeletedAt = _clock.GetUtcNow().UtcDateTime;
            await PublishStatusChangedAsync(row, old, "revoked", null, ct);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public EventSourceHealth Health
    {
        get
        {
            bool connected =
                _handle?.SessionId is not null
                || _transport.Kind != EventSubTransportKind.WebSocket;
            DateTimeOffset? lastReconnect = (
                _transport as WebSocketEventSubTransport
            )?.LastReconnectAt;
            return new EventSourceHealth(
                connected,
                _transport.Kind,
                _activeSubscriptionCount,
                _lastEventAt,
                lastReconnect
            );
        }
    }

    // ── ITwitchEventSubService ──────────────────────────────────────────────

    public async Task<Result<EventSubSubscriptionDto>> SubscribeAsync(
        Guid broadcasterId,
        string eventType,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITwitchIdentityResolver resolver =
            scope.ServiceProvider.GetRequiredService<ITwitchIdentityResolver>();

        string? twitchId = await resolver.GetTwitchChannelIdAsync(broadcasterId, ct);
        if (twitchId is null)
            return Result.Failure<EventSubSubscriptionDto>(
                "No Twitch channel id for the tenant.",
                "NOT_FOUND"
            );

        string version = _conditionBuilder.GetVersion(eventType);

        // Idempotent upsert on (BroadcasterId, Provider, EventType, Version).
        EventSubSubscription? row = await db.EventSubSubscriptions.FirstOrDefaultAsync(
            s =>
                s.BroadcasterId == broadcasterId
                && s.Provider == "twitch"
                && s.EventType == eventType
                && s.Version == version,
            ct
        );

        bool isNew = row is null;
        IReadOnlyDictionary<string, string> condition = _conditionBuilder.BuildCondition(
            eventType,
            twitchId
        );

        if (row is null)
        {
            row = new EventSubSubscription
            {
                BroadcasterId = broadcasterId,
                Provider = "twitch",
                EventType = eventType,
                Version = version,
                Condition = new Dictionary<string, string>(condition),
                Transport = _transport.Kind.ToString().ToLowerInvariant(),
                Status = "pending",
                Enabled = true,
            };
            await db.EventSubSubscriptions.AddAsync(row, ct);
        }
        else
        {
            row.Enabled = true;
            row.Condition = new Dictionary<string, string>(condition);
        }

        await db.SaveChangesAsync(ct);
        string oldStatus = isNew ? "none" : row.Status;

        // Create at Twitch via the transport (idempotent at our layer; Twitch 409 on an exact duplicate).
        EventSubSubscriptionRequest request = new()
        {
            BroadcasterId = broadcasterId,
            TwitchBroadcasterUserId = twitchId,
            EventType = eventType,
            Version = version,
            Condition = condition,
            UserAccessTokenOwner = _conditionBuilder.RequiresBroadcasterToken(eventType)
                ? EventSubTokenOwnerKind.Broadcaster
                : EventSubTokenOwnerKind.Bot,
        };

        EventSubTransportHandle handle =
            _handle ?? new EventSubTransportHandle { Kind = _transport.Kind };
        Result<TwitchSubscriptionResult> created = await _transport.CreateSubscriptionAsync(
            request,
            handle,
            ct
        );

        if (created.IsFailure)
        {
            row.Status = "failed";
            row.LastError = created.ErrorMessage;
            await db.SaveChangesAsync(ct);
            await PublishStatusChangedAsync(row, oldStatus, "failed", created.ErrorMessage, ct);
            return Result.Failure<EventSubSubscriptionDto>(
                created.ErrorMessage!,
                created.ErrorCode,
                created.ErrorDetail
            );
        }

        row.TwitchSubscriptionId = created.Value.TwitchSubscriptionId;
        row.SessionId = created.Value.SessionId;
        row.Cost = created.Value.Cost;
        row.Status = created.Value.Status == "enabled" ? "enabled" : created.Value.Status;
        row.LastError = null;
        await db.SaveChangesAsync(ct);

        await PublishStatusChangedAsync(row, oldStatus, row.Status, null, ct);
        return Result.Success(ToDto(row));
    }

    public async Task<Result> UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        EventSubSubscription? row = await db.EventSubSubscriptions.FirstOrDefaultAsync(
            s => s.Id == subscriptionId,
            ct
        );
        if (row is null)
            return Result.Failure("EventSub subscription not found.", "NOT_FOUND");

        if (row.TwitchSubscriptionId is { } id)
        {
            Result deleted = await _transport.DeleteSubscriptionAsync(id, ct);
            if (deleted.IsFailure)
                return deleted;
        }

        string old = row.Status;
        row.Status = "revoked";
        row.Enabled = false;
        row.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        await PublishStatusChangedAsync(row, old, "revoked", null, ct);
        return Result.Success();
    }

    public async Task<Result<PagedList<EventSubSubscriptionDto>>> GetSubscriptionsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        IQueryable<EventSubSubscription> query = db
            .EventSubSubscriptions.AsNoTracking()
            .Where(s => s.BroadcasterId == broadcasterId)
            .OrderBy(s => s.EventType);

        int total = await query.CountAsync(ct);
        List<EventSubSubscription> rows = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<EventSubSubscriptionDto>(
                rows.Select(ToDto).ToList(),
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<EventSubReconcileReportDto>> ReconcileAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<TwitchSubscriptionResult>> listed =
            await _transport.ListSubscriptionsAsync(broadcasterId, ct);
        if (listed.IsFailure)
            return Result.Failure<EventSubReconcileReportDto>(
                listed.ErrorMessage!,
                listed.ErrorCode,
                listed.ErrorDetail
            );

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<EventSubSubscription> registry = await db
            .EventSubSubscriptions.Where(s => s.BroadcasterId == broadcasterId)
            .ToListAsync(ct);

        Dictionary<string, TwitchSubscriptionResult> liveById = listed
            .Value.Where(s => !string.IsNullOrEmpty(s.TwitchSubscriptionId))
            .GroupBy(s => s.TwitchSubscriptionId)
            .ToDictionary(g => g.Key, g => g.First());

        int repaired = 0;
        int unchanged = 0;
        List<string> errors = [];

        // Repair status drift for rows Twitch still knows about.
        foreach (EventSubSubscription row in registry)
        {
            if (
                row.TwitchSubscriptionId is { } id
                && liveById.TryGetValue(id, out TwitchSubscriptionResult? live)
            )
            {
                string target = live.Status == "enabled" ? "enabled" : live.Status;
                if (row.Status != target)
                {
                    string old = row.Status;
                    row.Status = target;
                    repaired++;
                    await PublishStatusChangedAsync(row, old, target, null, ct);
                }
                else
                {
                    unchanged++;
                }
            }
        }

        // Re-create rows that are desired (Enabled) but Twitch no longer has.
        int created = 0;
        foreach (
            EventSubSubscription row in registry.Where(r => r.Enabled && !r.DeletedAt.HasValue)
        )
        {
            bool liveExists = row.TwitchSubscriptionId is { } id && liveById.ContainsKey(id);
            if (liveExists)
                continue;

            Result<EventSubSubscriptionDto> recreated = await SubscribeAsync(
                broadcasterId,
                row.EventType,
                ct
            );
            if (recreated.IsSuccess)
                created++;
            else
                errors.Add($"{row.EventType}: {recreated.ErrorMessage}");
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(
            new EventSubReconcileReportDto(created, 0, repaired, unchanged, errors)
        );
    }

    public async Task<Result> ReconnectAsync(CancellationToken ct = default)
    {
        await _transport.StopAsync(ct);
        Result<EventSubTransportHandle> restarted = await _transport.StartAsync(ct);
        if (restarted.IsFailure)
            return restarted;

        _handle = restarted.Value;
        return Result.Success();
    }

    // ── Internals ───────────────────────────────────────────────────────────

    /// <summary>
    /// Re-registers every enabled registry subscription against the current session/handle (post-welcome,
    /// for every tenant). Returns the count that ended up enabled.
    /// </summary>
    private async Task<int> ReRegisterAllAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<Guid> tenants = await db
            .EventSubSubscriptions.Where(s => s.Enabled && s.DeletedAt == null)
            .Select(s => s.BroadcasterId)
            .Distinct()
            .ToListAsync(ct);

        int enabled = 0;
        foreach (Guid tenant in tenants)
        {
            List<string> eventTypes = await db
                .EventSubSubscriptions.Where(s =>
                    s.BroadcasterId == tenant && s.Enabled && s.DeletedAt == null
                )
                .Select(s => s.EventType)
                .ToListAsync(ct);

            foreach (string eventType in eventTypes)
            {
                Result<EventSubSubscriptionDto> result = await SubscribeAsync(
                    tenant,
                    eventType,
                    ct
                );
                if (result.IsSuccess)
                    enabled++;
            }
        }

        return enabled;
    }

    private Task PublishStatusChangedAsync(
        EventSubSubscription row,
        string oldStatus,
        string newStatus,
        string? error,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new EventSubSubscriptionStatusChangedEvent
            {
                BroadcasterId = row.BroadcasterId,
                SubscriptionId = row.Id,
                EventType = row.EventType,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Error = error,
                Timestamp = _clock.GetUtcNow(),
            },
            ct
        );

    private static EventSubSubscriptionDto ToDto(EventSubSubscription row) =>
        new(
            row.Id,
            row.EventType,
            row.Version,
            row.Transport,
            row.Status,
            row.Enabled,
            row.Cost,
            row.TwitchSubscriptionId,
            row.LastError,
            row.ExpiresAt,
            row.CreatedAt
        );
}
