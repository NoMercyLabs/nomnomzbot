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
/// arriving under TWO DIFFERENT message ids. That happens on every zero-downtime deploy this project ships
/// (<c>scripts/switchover.ps1</c>): the incoming colour is started and waits for <c>/health/ready</c> WHILE the
/// outgoing colour is still live, so for that overlap TWO PROCESSES both hold a live EventSub session and both
/// receive the same real event under their own message id. It also happens within one process during a
/// WebSocket reconnect (Twitch keeps a dying session's subscriptions alive for ~1 minute — twitch-eventsub §7 /
/// <c>WebSocketEventSubTransport</c>'s stale-session comments). Either way: two genuine Twitch deliveries, two
/// message ids, one real occurrence. This guard claims the (broadcaster, subscription type, raw payload) triple
/// for a short window; a second claim of the SAME triple within the window — from ANY process sharing the same
/// durable store — is the semantic duplicate.
/// <para>
/// The claim MUST be visible across processes (the blue/green overlap above is not hypothetical — it is the
/// documented shape of every deploy), so the implementation persists it in the store every deployment profile
/// already shares: the database (self_host_lite's SQLite included), not an in-process cache.
/// </para>
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
    /// fan it out) and <c>false</c> when it was already claimed — by this process OR another one sharing the
    /// same store — and the claim has not yet expired (a semantic duplicate — the caller should skip fan-out
    /// but may still journal the raw delivery). The claim itself is atomic (a unique-constrained insert), so
    /// two processes racing to claim the same triple always resolve to exactly one winner.
    /// </summary>
    Task<bool> TryClaimAsync(
        Guid broadcasterId,
        string subscriptionType,
        string rawPayloadJson,
        DateTimeOffset now,
        TimeSpan window,
        CancellationToken ct = default
    );
}
