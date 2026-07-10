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
/// and <see cref="IEventSource"/>.
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

    private DateTimeOffset? _lastEventAt;
    private volatile int _activeSubscriptionCount;

    // Set once the transport has actually been started (after the readiness gate opened), so the dormancy
    // waiter never double-starts and so it stops re-checking once it has handed off to the receive loop.
    private volatile bool _transportStarted;
    private CancellationTokenSource? _dormancyCts;
    private Task? _dormancyWaiter;

    // How often the dormancy waiter re-checks the readiness gate while waiting for onboarding to complete.
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromSeconds(20);

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
        // Dormant until onboarding: a fresh, un-onboarded self-host has no platform bot token, so connecting
        // the EventSub transport would only reconnect-loop against Twitch (no subscriptions to keep it alive).
        // Stay quiet until a bot account is authorized — the waiter re-checks the gate and starts it then, so an
        // onboarding completed at runtime activates EventSub without a process restart. The full/SaaS path has a
        // configured bot, so the gate is already open and the transport starts immediately on this first check.
        if (!await IsPlatformBotConfiguredAsync(cancellationToken))
        {
            _logger.LogInformation("EventSub: waiting for onboarding before connecting to Twitch.");
            _dormancyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _dormancyWaiter = Task.Run(
                () => WaitForReadinessThenStartAsync(_dormancyCts.Token),
                _dormancyCts.Token
            );
            return;
        }

        await StartTransportAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_dormancyCts is not null)
        {
            await _dormancyCts.CancelAsync();
            _dormancyCts.Dispose();
            _dormancyCts = null;
        }

        if (_dormancyWaiter is not null)
        {
            try
            {
                await _dormancyWaiter;
            }
            catch (OperationCanceledException) { }
        }

        await _transport.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Starts the transport and records the handle. Idempotent against the dormancy waiter via
    /// <see cref="_transportStarted"/>, so onboarding completing exactly as the host stops cannot double-start.
    /// </summary>
    private async Task StartTransportAsync(CancellationToken ct)
    {
        if (_transportStarted)
            return;

        Result<EventSubTransportHandle> started = await _transport.StartAsync(ct);
        if (started.IsFailure)
        {
            _logger.LogWarning("EventSub transport failed to start: {Error}", started.ErrorMessage);
            return;
        }

        _transportStarted = true;
        _logger.LogInformation("EventSub transport started ({Kind})", _transport.Kind);
    }

    /// <summary>
    /// While dormant, re-checks the readiness gate every <see cref="ReadinessPollInterval"/> and starts the
    /// transport the first time the platform bot is configured (onboarding completed). No per-tick logging — the
    /// single "waiting" line was logged once at startup; the next line is the steady-state "transport started".
    /// </summary>
    private async Task WaitForReadinessThenStartAsync(CancellationToken ct)
    {
        try
        {
            using PeriodicTimer timer = new(ReadinessPollInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!await IsPlatformBotConfiguredAsync(ct))
                    continue;

                await StartTransportAsync(ct);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down before onboarding completed — end the waiter quietly.
        }
    }

    private async Task<bool> IsPlatformBotConfiguredAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IPlatformBotReadinessGate gate =
            scope.ServiceProvider.GetRequiredService<IPlatformBotReadinessGate>();
        return await gate.IsPlatformBotConfiguredAsync(ct);
    }

    // ── IEventSubNotificationSink (called by the transport receive loop) ────

    public async Task OnSessionWelcomeAsync(string sessionId, string ownerKey, CancellationToken ct)
    {
        _logger.LogInformation(
            "EventSub session welcome ({Owner} / {SessionId}) — re-homing subscriptions",
            ownerKey,
            sessionId
        );

        // A fresh welcome for this OWNER means its previous WebSocket session is dead. Twitch keeps that session's
        // subscriptions in a `websocket_disconnected` state for ~1 minute, and a re-create's 409-conflict key
        // is (type + condition) — session-independent — so those lingering subs would 409 every re-create and
        // strand this owner's topics. Clear this owner's dead-session orphans first (never another owner's),
        // then re-register this owner's enabled registry rows against the new session and announce steady state.
        await CleanupOwnerStaleSubsAsync(ownerKey, sessionId, ct);
        int active = await ReRegisterOwnerAsync(ownerKey, ct);
        _activeSubscriptionCount = active;

        await _eventBus.PublishAsync(
            new EventSubConnectedEvent
            {
                BroadcasterId = Guid.Empty,
                Transport = _transport.Kind,
                SessionId = sessionId,
                ActiveSubscriptionCount = active,
                OccurredAt = _clock.GetUtcNow(),
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
                OccurredAt = _clock.GetUtcNow(),
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
        // Subscribe cost-0 topics (chat + the chat-read set) first — they never hit the WebSocket cost cap,
        // so chat lands even when the cost-1 topics have exhausted it, and ahead of the general Helix rate burn.
        IEnumerable<string> ordered = eventTypes.OrderByDescending(
            EventSubConditionBuilder.IsCost0Topic
        );
        List<string> failures = [];
        foreach (string eventType in ordered)
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
            // Connected once the bot session (which carries every channel's chat-read topics) has a session id;
            // non-WebSocket transports report connected via their own liveness.
            bool connected =
                (_transport as WebSocketEventSubTransport)?.SessionId is not null
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

        // Ensure the WebSocket session this topic must ride is live, and post the create onto it. A topic rides
        // its token owner's session: bot-owned topics (chat-read) ride the bot's session; a broadcaster's
        // authorized topics ride that broadcaster's OWN session — Twitch rejects subs from different users on one
        // session ("websocket transport cannot have subscriptions created by different users").
        string ownerKey = OwnerKeyFor(broadcasterId, eventType);
        Result<EventSubTransportHandle> session = await _transport.EnsureSessionAsync(ownerKey, ct);
        if (session.IsFailure)
            return Result.Failure<EventSubSubscriptionDto>(
                session.ErrorMessage!,
                session.ErrorCode
            );
        EventSubTransportHandle handle = session.Value;

        // Resolve the bot's Twitch user id to fill the user_id / moderator_user_id slot for BOT-OWNED topics
        // (chat-read + the bot's whispers). Broadcaster-owned topics ignore it (they key on the broadcaster).
        // When no dedicated bot account is configured (streamer IS the bot), it is null and the broadcaster id
        // fills the slot as a single-account-self-host fallback.
        string? botTwitchUserId = await db
            .IntegrationConnections.Where(c =>
                c.Provider == "twitch_bot" && c.BroadcasterId == null
            )
            .Select(c => c.ProviderAccountId)
            .FirstOrDefaultAsync(ct);

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
            twitchId,
            botTwitchUserId
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

        // Idempotent adopt / park (skip the Twitch POST):
        //  - an already-`enabled` row on the CURRENT session is live at Twitch — re-POSTing it would only 409.
        //  - a `deferred` row was parked by a prior 429 (WebSocket cost cap full); leave it for the conduit
        //    transport instead of re-hammering the cost budget on every reconnect / reconcile.
        string? currentSession = handle.SessionId;
        if (!isNew)
        {
            if (
                row.Status == "enabled"
                && row.TwitchSubscriptionId is not null
                && currentSession is not null
                && row.SessionId == currentSession
            )
                return Result.Success(ToDto(row));
            if (row.Status == "deferred")
                return Result.Success(ToDto(row));
        }

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

        Result<TwitchSubscriptionResult> created = await _transport.CreateSubscriptionAsync(
            request,
            handle,
            ct
        );

        if (created.IsFailure)
        {
            // Map the failure to a retryable / parked / terminal status instead of a permanent "failed":
            //  - Conflict (409): an identical sub still lingers from a dead session inside Twitch's ~1-min GC
            //    window → "pending" so the next reconcile retries once it clears (expected, transient).
            //  - RateLimited (429): the WebSocket cost cap is full (the cost-1 topics) → "deferred", parked for
            //    the conduit transport and not re-hammered.
            //  - otherwise → "failed" (keeps the 403 missing-scope path below intact).
            row.Status = created.ErrorCode switch
            {
                TwitchErrorCodes.Conflict => "pending",
                TwitchErrorCodes.RateLimited => "deferred",
                _ => "failed",
            };
            row.LastError = created.ErrorMessage;
            await db.SaveChangesAsync(ct);
            await PublishStatusChangedAsync(row, oldStatus, row.Status, created.ErrorMessage, ct);

            // Log the full Twitch error body to diagnose real failures (400/403/etc.). A 409 conflict is the
            // expected transient during the stale-session GC window — don't spam the log with it.
            if (
                !string.IsNullOrEmpty(created.ErrorDetail)
                && created.ErrorCode != TwitchErrorCodes.Conflict
            )
                _logger.LogWarning(
                    "EventSub subscription {EventType} for {BroadcasterId} error detail: {Detail}",
                    eventType,
                    broadcasterId,
                    created.ErrorDetail
                );

            // When Twitch 403 body says "Missing required scope <scope>", publish the reauth event so
            // MissingScopeRecordingHandler can record it and the dashboard can surface an action-required flow.
            string? missingScope = ExtractMissingScope(created.ErrorDetail);
            if (missingScope is not null)
            {
                _logger.LogWarning(
                    "EventSub subscription {EventType} for {BroadcasterId} blocked: missing scope '{Scope}'",
                    eventType,
                    broadcasterId,
                    missingScope
                );
                await _eventBus.PublishAsync(
                    new TwitchHelixReauthRequiredEvent
                    {
                        BroadcasterId = broadcasterId,
                        Provider = "twitch",
                        ServiceName = "twitch",
                        Reason = "missing_scope",
                        MissingScope = missingScope,
                    },
                    ct
                );
            }

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
        int deleted = 0;
        List<string> errors = [];

        // Delete stale / orphan subscriptions this tenant owns at Twitch (attributed by TwitchSubscriptionId —
        // LIST doesn't return the condition, so we only ever delete ids in our own registry, never another
        // tenant's). "Stale" = a live sub on a dead session (SessionId set and != its OWNER's current session);
        // "orphan" = a live sub whose registry row is no longer desired (disabled / soft-deleted). Removing it
        // from the live set lets the re-create pass below re-home a still-desired stale row on the current
        // session. Each row's owner has its own session (bot vs broadcaster), so the "current" session is
        // per-owner.
        Dictionary<string, EventSubSubscription> registryById = registry
            .Where(r => r.TwitchSubscriptionId is not null)
            .ToDictionary(r => r.TwitchSubscriptionId!, r => r);
        foreach (TwitchSubscriptionResult live in listed.Value)
        {
            if (
                string.IsNullOrEmpty(live.TwitchSubscriptionId)
                || !registryById.TryGetValue(
                    live.TwitchSubscriptionId,
                    out EventSubSubscription? owned
                )
            )
                continue;

            string? currentSession = _transport.CurrentSessionId(
                OwnerKeyFor(owned.BroadcasterId, owned.EventType)
            );
            bool staleSession =
                !string.IsNullOrEmpty(live.SessionId)
                && currentSession is not null
                && live.SessionId != currentSession;
            bool notDesired = !owned.Enabled || owned.DeletedAt.HasValue;
            if (!staleSession && !notDesired)
                continue;

            Result del = await _transport.DeleteSubscriptionAsync(live.TwitchSubscriptionId, ct);
            if (del.IsFailure)
            {
                errors.Add($"delete {live.TwitchSubscriptionId}: {del.ErrorMessage}");
                continue;
            }

            deleted++;
            liveById.Remove(live.TwitchSubscriptionId);
        }

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
            new EventSubReconcileReportDto(created, deleted, repaired, unchanged, errors)
        );
    }

    public async Task<Result> ReconnectAsync(CancellationToken ct = default)
    {
        await _transport.StopAsync(ct);
        Result<EventSubTransportHandle> restarted = await _transport.StartAsync(ct);
        return restarted.IsFailure ? restarted : Result.Success();
    }

    // ── Internals ───────────────────────────────────────────────────────────

    /// <summary>
    /// The WebSocket session bucket a topic rides: the broadcaster's own session for its authorized topics,
    /// the bot's shared session for the bot-owned (chat-read) set. Mirrors the token that creates the sub.
    /// </summary>
    private string OwnerKeyFor(Guid broadcasterId, string eventType) =>
        EventSubOwnerKeys.For(broadcasterId, _conditionBuilder.RequiresBroadcasterToken(eventType));

    /// <summary>
    /// Deletes THIS owner's subscriptions still registered at Twitch under a DEAD WebSocket session before
    /// re-registering. After a full drop Twitch holds the old session's subs in a <c>websocket_disconnected</c>
    /// state for ~1 minute; since a create's 409-conflict key is (type + condition) — session-independent — those
    /// lingering subs would 409 every re-create on the new session and strand this owner's topics. Driven off our
    /// OWN registry (never another owner's rows, never another live session): the owner's rows whose
    /// <c>SessionId</c> is set and differs from the current one are the dead-session orphans. A genuine Twitch
    /// <c>session_reconnect</c> keeps the same session id, so nothing is stale and nothing is deleted.
    /// </summary>
    private async Task CleanupOwnerStaleSubsAsync(
        string ownerKey,
        string currentSessionId,
        CancellationToken ct
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<EventSubSubscription> rows = await db
            .EventSubSubscriptions.Where(s =>
                s.Enabled
                && s.DeletedAt == null
                && s.TwitchSubscriptionId != null
                && s.SessionId != null
                && s.SessionId != currentSessionId
            )
            .ToListAsync(ct);

        int deleted = 0;
        foreach (EventSubSubscription row in rows)
        {
            if (OwnerKeyFor(row.BroadcasterId, row.EventType) != ownerKey)
                continue;

            await _transport.DeleteSubscriptionAsync(row.TwitchSubscriptionId!, ct);
            deleted++;
        }

        if (deleted > 0)
            _logger.LogInformation(
                "EventSub: deleted {Count} stale-session subscription(s) for owner {Owner} before re-registering on {SessionId}",
                deleted,
                ownerKey,
                currentSessionId
            );
    }

    /// <summary>
    /// Re-registers the enabled registry subscriptions that belong to <paramref name="ownerKey"/> against its
    /// fresh session (post-welcome): the bot owner re-homes every channel's chat-read topics; a broadcaster owner
    /// re-homes that channel's authorized topics. Returns the count that ended up enabled.
    /// </summary>
    private async Task<int> ReRegisterOwnerAsync(string ownerKey, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<EventSubSubscription> rows = await db
            .EventSubSubscriptions.Where(s => s.Enabled && s.DeletedAt == null)
            .Select(s => new EventSubSubscription
            {
                BroadcasterId = s.BroadcasterId,
                EventType = s.EventType,
            })
            .ToListAsync(ct);

        // Keep only this owner's slice, cost-0 (chat-read) first so chat lands ahead of the cost-1 burn.
        List<(Guid Tenant, string EventType)> owned =
        [
            .. rows.Where(r => OwnerKeyFor(r.BroadcasterId, r.EventType) == ownerKey)
                .OrderByDescending(r => EventSubConditionBuilder.IsCost0Topic(r.EventType))
                .Select(r => (r.BroadcasterId, r.EventType)),
        ];

        int enabled = 0;
        foreach ((Guid tenant, string eventType) in owned)
        {
            Result<EventSubSubscriptionDto> result = await SubscribeAsync(tenant, eventType, ct);
            if (result.IsSuccess)
                enabled++;
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
                OccurredAt = _clock.GetUtcNow(),
            },
            ct
        );

    // Twitch 403 body for a missing scope: {"error":"Forbidden","status":403,"message":"Missing required scope channel:read:hype_train"}
    // Returns the scope token, or null when the body doesn't match.
    private static string? ExtractMissingScope(string? errorDetail)
    {
        if (string.IsNullOrWhiteSpace(errorDetail))
            return null;
        try
        {
            JsonDocument doc = JsonDocument.Parse(errorDetail);
            if (
                doc.RootElement.TryGetProperty("message", out JsonElement msg)
                && msg.GetString() is string text
                && text.StartsWith("Missing required scope ", StringComparison.OrdinalIgnoreCase)
            )
                return text["Missing required scope ".Length..].Trim();
        }
        catch (JsonException)
        {
            // not a JSON body — ignore
        }
        return null;
    }

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
