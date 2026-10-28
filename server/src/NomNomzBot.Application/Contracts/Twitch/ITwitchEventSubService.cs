// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.DTOs.Twitch.EventSub;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The EventSub registry + lifecycle facade (twitch-eventsub §3.2), implemented by the
/// <c>TwitchEventSubHostedService</c> (the <c>IHostedService</c>). Extends <see cref="IEventSource"/> with the
/// runtime control surface; method bodies delegate the wire to the selected transport.
/// </summary>
public interface ITwitchEventSubService : IEventSource
{
    /// <summary>
    /// Subscribes one event type for a tenant; idempotent on <c>(BroadcasterId, EventType, Version)</c>.
    /// Upserts an <c>EventSubSubscription</c> (Status=pending→enabled), calls Twitch via the transport, emits
    /// <c>EventSubSubscriptionStatusChangedEvent</c>. Returns the registry row.
    /// </summary>
    Task<Result<EventSubSubscriptionDto>> SubscribeAsync(
        Guid broadcasterId,
        string eventType,
        CancellationToken ct = default
    );

    /// <summary>
    /// Revokes one subscription by its surrogate id. DELETEs at Twitch + soft-deletes the registry row;
    /// emits <c>EventSubSubscriptionStatusChangedEvent(NewStatus=revoked)</c>. NOT_FOUND if unknown.
    /// </summary>
    Task<Result> UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default);

    /// <summary>Reads the persisted registry for a tenant (no Twitch call). Paginated.</summary>
    Task<Result<PagedList<EventSubSubscriptionDto>>> GetSubscriptionsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// Reconciles this tenant's registry against Twitch's actual subscription list (drops orphans,
    /// re-creates missing, repairs status). Used after reconnect and by admin.
    /// </summary>
    Task<Result<EventSubReconcileReportDto>> ReconcileAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Forces a transport reconnect (admin / diagnostic): drops the session, re-runs welcome + resubscribe.</summary>
    Task<Result> ReconnectAsync(CancellationToken ct = default);
}
