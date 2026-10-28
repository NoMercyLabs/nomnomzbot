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

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// One EventSub notification received on a conduit shard and not yet processed (twitch-eventsub §10.1).
/// Platform-level. During a blue/green overlap BOTH instances hold a shard, but only the chat-ingest lease
/// holder may process: every receiver writes here, the lease holder drains the table in arrival order and
/// deletes each row once it is dispatched. A row left behind by a stopping instance is simply drained by its
/// successor, so a handover neither loses nor doubles an event.
/// </summary>
public class EventSubInboxMessage : BaseEntity
{
    public Guid Id { get; set; } = MonotonicGuid.Create();

    /// <summary>Twitch's <c>message_id</c>. Unique: a resent notification is stored once.</summary>
    public string MessageId { get; set; } = null!;

    /// <summary>Twitch's <c>message_timestamp</c>, UTC.</summary>
    public DateTime MessageTimestamp { get; set; }

    public string SubscriptionType { get; set; } = null!;

    public string SubscriptionVersion { get; set; } = null!;

    /// <summary>The Twitch id the notification is attributed to (resolved to a tenant when processed).</summary>
    public string TwitchBroadcasterUserId { get; set; } = null!;

    /// <summary>The notification's <c>event</c> object, verbatim JSON.</summary>
    public string EventJson { get; set; } = null!;

    /// <summary>When the receiving instance stored it — the drain order.</summary>
    public DateTime ReceivedAt { get; set; }
}
