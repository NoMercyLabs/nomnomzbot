// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>
/// Raised when a feature flag's effective state changes (global toggle/ramp or a per-tenant override) — the cache
/// invalidation trigger (platform-conventions §2). <c>BroadcasterId</c> is the affected channel for an override
/// change, or <c>Guid.Empty</c> for a global change (bounded by the cache TTL).
/// </summary>
public sealed class FeatureFlagChangedEvent : DomainEventBase
{
    public required string FlagKey { get; init; }
}
