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
/// Raised when the adaptive limiter or a real 429 forces a Helix call to be throttled/queued
/// (twitch-helix.md §2). Consumed by observability + the "rate limited" UI surfacing; not persisted.
/// App/bot-token (non-tenant) calls publish with the inherited <c>BroadcasterId = Guid.Empty</c>.
/// </summary>
public sealed class TwitchHelixRateLimitedEvent : DomainEventBase
{
    /// <summary>The hashed token-bucket identity — never the raw token.</summary>
    public required string TokenBucketKey { get; init; }

    /// <summary>Remaining budget observed at the moment the throttle engaged.</summary>
    public required int RemainingBeforeThrottle { get; init; }

    /// <summary>When the bucket resets / the throttle lifts.</summary>
    public required DateTimeOffset ResetsAt { get; init; }

    /// <summary>True when a real 429 was received (vs a proactive, header-driven throttle).</summary>
    public required bool WasHardLimited { get; init; }
}
