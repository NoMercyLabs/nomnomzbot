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
/// Raised when the circuit breaker opens for the Helix client after sustained failures (twitch-helix.md §2).
/// Platform-level, not tenant-scoped: published with the inherited <c>BroadcasterId = Guid.Empty</c>.
/// </summary>
public sealed class TwitchHelixCircuitOpenedEvent : DomainEventBase
{
    /// <summary>The named client whose breaker opened — <c>twitch-helix</c>.</summary>
    public required string ClientName { get; init; }

    /// <summary>When the breaker opened.</summary>
    public required DateTimeOffset OpenedAt { get; init; }

    /// <summary>How long the breaker will stay open before the half-open probe.</summary>
    public required TimeSpan BreakDuration { get; init; }
}
