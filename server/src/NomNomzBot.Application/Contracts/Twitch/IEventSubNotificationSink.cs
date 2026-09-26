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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The seam a transport's receive loop calls when a wire frame arrives (twitch-eventsub §3.3). The transport
/// owns the wire and parses the frame to these primitives; the sink (the hosted service) owns tenant
/// resolution (Twitch id ⇒ tenant Guid) and dispatch (journal + bus) so the transport stays free of DB
/// concerns and the dedupe/journal path is not duplicated across transports.
/// </summary>
public interface IEventSubNotificationSink
{
    /// <summary>A <c>notification</c> frame arrived. The raw <paramref name="event"/> stays on System.Text.Json.</summary>
    Task OnNotificationAsync(
        string messageId,
        DateTimeOffset messageTimestamp,
        string subscriptionType,
        string subscriptionVersion,
        string twitchBroadcasterUserId,
        JsonElement @event,
        CancellationToken ct
    );

    /// <summary>A <c>revocation</c> frame arrived for a subscription. Marks the registry row + surfaces it.</summary>
    Task OnRevocationAsync(
        string twitchSubscriptionId,
        string subscriptionType,
        string status,
        string twitchBroadcasterUserId,
        CancellationToken ct
    );

    /// <summary>
    /// A transport session reached a fresh steady state (welcome received) — (re)register the subscriptions that
    /// belong to <paramref name="ownerKey"/> (see <see cref="EventSubOwnerKeys"/>). Each token owner has its own
    /// WebSocket session, so a welcome re-registers only that owner's slice, not the whole registry.
    /// Called from the receive loop: an implementation must return promptly and do its Twitch/DB work elsewhere,
    /// or the loop stops reading frames and Twitch closes a session with no subscription inside 10 s (4003).
    /// </summary>
    /// <param name="handoffFromSessionId">
    /// Set when this welcome arrived on a <c>session_reconnect</c> URL: the id of the session it replaces.
    /// Twitch migrates that session's subscriptions itself, so nothing is deleted or re-created. Null for a
    /// fresh session.
    /// </param>
    Task OnSessionWelcomeAsync(
        string sessionId,
        string ownerKey,
        string? handoffFromSessionId,
        CancellationToken ct
    );

    /// <summary>
    /// Asked after an owner's session dropped, before a reconnect is scheduled. False parks the session: no
    /// reconnect happens until something ensures that owner's session again (a subscribe that passes the grant
    /// gate). Used when every one of the owner's topics is refused until its grant changes — a session with
    /// nothing to carry is closed by Twitch within 10 s, so reconnecting it only loops.
    /// </summary>
    Task<bool> ShouldReconnectAsync(string ownerKey, CancellationToken ct);

    /// <summary>
    /// The token owner's session dropped (keepalive timeout, unexpected close, network error) and a reconnect
    /// with backoff is scheduled (twitch-eventsub §7 hardening). A dashboard-visible "degraded" diagnostic —
    /// distinct from <see cref="OnRevocationAsync"/>, which means the authorization itself is gone; this means
    /// only the transport hiccuped and is already retrying.
    /// </summary>
    Task OnSessionDisconnectedAsync(
        string ownerKey,
        string? sessionId,
        string reason,
        TimeSpan nextRetryIn,
        CancellationToken ct
    );
}
