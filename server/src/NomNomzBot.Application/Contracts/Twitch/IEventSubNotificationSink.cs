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
    /// </summary>
    Task OnSessionWelcomeAsync(string sessionId, string ownerKey, CancellationToken ct);
}
