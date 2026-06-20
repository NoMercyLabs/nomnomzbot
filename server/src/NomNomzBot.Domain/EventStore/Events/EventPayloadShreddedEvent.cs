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

namespace NomNomzBot.Domain.EventStore.Events;

/// <summary>
/// Emitted after a subject's crypto-shred DEK set has been destroyed for journal payloads, so the compliance
/// audit pipeline can record the side effect. Inherits the canonical <see cref="DomainEventBase"/>.
/// </summary>
public sealed class EventPayloadShreddedEvent : DomainEventBase
{
    public required string SubjectIdHash { get; init; }

    /// <summary>Journal rows whose payload became unreadable.</summary>
    public required int EventsAffected { get; init; }

    /// <summary>DEKs marked destroyed for this subject.</summary>
    public required int KeysDestroyed { get; init; }
    public Guid? ErasureRequestId { get; init; }
}
