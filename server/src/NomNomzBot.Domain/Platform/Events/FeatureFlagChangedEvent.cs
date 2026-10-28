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

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>
/// Raised when a feature flag's effective state changes (global toggle/ramp or a per-tenant override) — the cache
/// invalidation trigger (platform-conventions §2). <c>BroadcasterId</c> is the affected channel for an override
/// change, or <c>Guid.Empty</c> for a global change (bounded by the cache TTL).
/// </summary>
public sealed class FeatureFlagChangedEvent : DomainEventBase
{
    /// <summary>The key of the feature flag whose value changed.</summary>
    public required string FlagKey { get; init; }
}
