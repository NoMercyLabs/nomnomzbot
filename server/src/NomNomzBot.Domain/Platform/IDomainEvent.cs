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
    /// <summary>Unique ID for this event instance. Used for deduplication and tracing.</summary>
    string EventId { get; }

    /// <summary>When this event occurred (UTC).</summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>The broadcaster channel this event relates to, or null for platform-level events.</summary>
    string? BroadcasterId { get; }
}
