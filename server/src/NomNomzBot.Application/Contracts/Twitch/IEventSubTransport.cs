// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Enums;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The deployment-profile transport adapter seam (twitch-eventsub §3.3). Two impls, one chosen by DI: the
/// WebSocket transport (lite/self-host) and the conduit transport (SaaS). The hosted service owns lifecycle;
/// the transport owns the wire.
/// </summary>
public interface IEventSubTransport
{
    EventSubTransportKind Kind { get; }

    /// <summary>
    /// Brings the transport up (connect WS / ensure conduit+shards exist) and returns the session/conduit
    /// handle the service uses when creating subscriptions. Idempotent; safe to call after reconnect.
    /// </summary>
    Task<Result<EventSubTransportHandle>> StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates one subscription at Twitch under this transport (session_id for WS, conduit_id for conduit).
    /// Returns Twitch's subscription id + cost + status; failure carries the Twitch error body.
    /// </summary>
    Task<Result<TwitchSubscriptionResult>> CreateSubscriptionAsync(
        EventSubSubscriptionRequest request,
        EventSubTransportHandle handle,
        CancellationToken ct = default
    );

    /// <summary><c>DELETE /eventsub/subscriptions?id=</c>. Idempotent (404 → Success).</summary>
    Task<Result> DeleteSubscriptionAsync(
        string twitchSubscriptionId,
        CancellationToken ct = default
    );

    /// <summary>Lists the app/user's current subscriptions at Twitch (paged, follows cursor). For reconcile.</summary>
    Task<Result<IReadOnlyList<TwitchSubscriptionResult>>> ListSubscriptionsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Gracefully tears down (close WS / leave conduit shards). Called on shutdown.</summary>
    Task StopAsync(CancellationToken ct = default);
}
