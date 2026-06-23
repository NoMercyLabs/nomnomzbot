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
/// Abstract base class implementing IDomainEvent with sensible defaults.
/// All concrete domain events should inherit from this class.
/// </summary>
public abstract class DomainEventBase : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    // The single tolerated construction-time default for the clock (platform-conventions
    // §3.11): domain events are plain records built with `new` and have no DI seam, so the
    // initializer reads TimeProvider.System — the composition-root clock — rather than a bare
    // DateTimeOffset.UtcNow. Publishers needing determinism (and all tests) override OccurredAt
    // from their injected TimeProvider.
    public DateTimeOffset OccurredAt { get; init; } = TimeProvider.System.GetUtcNow();

    // The tenant (channel) this event relates to. Guid.Empty is the platform-level sentinel
    // (platform-conventions §2.0) — NOT null. Tenant-scoped events set this to the owning channel id.
    public Guid BroadcasterId { get; init; }
}
