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
