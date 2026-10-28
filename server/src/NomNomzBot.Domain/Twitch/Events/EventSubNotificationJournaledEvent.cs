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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Twitch.Events;

/// <summary>
/// One EventSub notification was journaled (idempotent append committed) — twitch-eventsub §2. Emitted before
/// fan-out to the per-topic domain events. The inherited <c>BroadcasterId</c> is the owning channel.
/// </summary>
public sealed class EventSubNotificationJournaledEvent : DomainEventBase
{
    /// <summary>The <c>EventJournal.EventId</c> of the appended row.</summary>
    public required Guid JournalEventId { get; init; }

    /// <summary>The position of the saved event in the channel's event log.</summary>
    public required long StreamPosition { get; init; }

    /// <summary>The Twitch event type that was saved, for example channel.follow.</summary>
    public required string EventType { get; init; }

    /// <summary>True when the append short-circuited on the idempotency dedupe (a redelivery).</summary>
    public required bool WasDuplicate { get; init; }
}
