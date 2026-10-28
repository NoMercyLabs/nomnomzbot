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
