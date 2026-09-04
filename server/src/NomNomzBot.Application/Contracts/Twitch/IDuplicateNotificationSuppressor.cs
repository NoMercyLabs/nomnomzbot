// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Second-layer EventSub dedupe guard (S-DUPE). <see cref="INotificationDispatcher"/>'s journal dedupe only
/// collapses an EXACT redelivery of the same wire message-id; it cannot see the same real-world Twitch event
/// arriving under TWO DIFFERENT message ids, which happens during a WebSocket reconnect: Twitch keeps a dying
/// session's subscriptions alive for ~1 minute (twitch-eventsub §7 / <c>WebSocketEventSubTransport</c>'s
/// stale-session comments), so an event that fires right as we re-home onto a fresh session can be handed to
/// BOTH the old, not-yet-GC'd subscription and the newly re-created one — two genuine Twitch deliveries, two
/// message ids, one real occurrence. This guard claims the (broadcaster, subscription type, raw payload)
/// triple for a short window; a second claim of the SAME triple within the window is the semantic duplicate.
/// <para>
/// Deliberately payload-content-keyed rather than a bespoke per-topic natural key: Twitch stamps most payloads
/// with something that changes between two genuinely distinct occurrences of the same shape (chat's own
/// <c>message_id</c>, a follow's <c>followed_at</c>, a redemption's <c>id</c>, …), so an exact byte-for-byte
/// repeat within the window is the duplicate-delivery signature, and a legitimate repeat (the same viewer
/// re-sending the same text, a second separate follow) carries different bytes and is never suppressed.
/// </para>
/// </summary>
public interface IDuplicateNotificationSuppressor
{
    /// <summary>
    /// Attempts to claim the (<paramref name="broadcasterId"/>, <paramref name="subscriptionType"/>,
    /// <paramref name="rawPayloadJson"/>) triple as of <paramref name="now"/> for <paramref name="window"/>.
    /// Returns <c>true</c> the first time this exact triple is claimed within the window (the caller should
    /// fan it out) and <c>false</c> when it was already claimed and the claim has not yet expired (a semantic
    /// duplicate — the caller should skip fan-out but may still journal the raw delivery).
    /// </summary>
    bool TryClaim(
        Guid broadcasterId,
        string subscriptionType,
        string rawPayloadJson,
        DateTimeOffset now,
        TimeSpan window
    );
}
