// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// Marker interface for all domain events. Provides common metadata for tracing and routing.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique ID for this event instance (UUIDv7). Used for deduplication and tracing.</summary>
    Guid EventId { get; }

    /// <summary>When this event occurred (UTC).</summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>The broadcaster channel (tenant) this event relates to, or <see cref="Guid.Empty"/> for platform-level events.</summary>
    Guid BroadcasterId { get; }
}
