// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    public required long StreamPosition { get; init; }
    public required string EventType { get; init; }

    /// <summary>True when the append short-circuited on the idempotency dedupe (a redelivery).</summary>
    public required bool WasDuplicate { get; init; }
}
