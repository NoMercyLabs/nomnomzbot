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

namespace NomNomzBot.Domain.EventStore.Events;

/// <summary>
/// Emitted after a subject's crypto-shred DEK set has been destroyed for journal payloads, so the compliance
/// audit pipeline can record the side effect. Inherits the canonical <see cref="DomainEventBase"/>.
/// </summary>
[Event(visibility: EventVisibility.Internal)]
public sealed class EventPayloadShreddedEvent : DomainEventBase
{
    /// <summary>A hash of the person's id. It does not show the real id.</summary>
    public required string SubjectIdHash { get; init; }

    /// <summary>Journal rows whose payload became unreadable.</summary>
    public required int EventsAffected { get; init; }

    /// <summary>DEKs marked destroyed for this subject.</summary>
    public required int KeysDestroyed { get; init; }

    /// <summary>The id of the erasure request that caused this. Empty when no request started it.</summary>
    public Guid? ErasureRequestId { get; init; }
}
