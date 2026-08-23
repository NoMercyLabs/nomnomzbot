// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.HealthChecks;

/// <summary>
/// Debounces <see cref="EventSubReadinessHealthCheck"/> across the normal EventSub reconnect cycle (S116).
/// Twitch sends a <c>session_reconnect</c> message roughly every 5 minutes by design (README "Known
/// Issues") and <c>WebSocketEventSubTransport</c> briefly reports no session id while it swaps to the new
/// socket — a normal, healthy event. Only a disconnect that outlives <see cref="GracePeriod"/> (far longer
/// than a graceful reconnect swap, far shorter than the ~5 minute reconnect cadence) is treated as a real
/// outage. Registered as a singleton so the "disconnected since" watermark survives across the
/// per-invocation <see cref="EventSubReadinessHealthCheck"/> instances the health check framework creates.
/// </summary>
public sealed class EventSubDisconnectTracker(TimeProvider clock)
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(45);

    private readonly Lock _lock = new();
    private DateTimeOffset? _disconnectedSince;

    /// <summary>
    /// Records the current connection state and returns whether the disconnect (if any) has outlived the
    /// grace period. A connected observation always resets the watermark.
    /// </summary>
    public bool IsSustainedDisconnect(bool isConnected)
    {
        DateTimeOffset now = clock.GetUtcNow();
        lock (_lock)
        {
            if (isConnected)
            {
                _disconnectedSince = null;
                return false;
            }

            _disconnectedSince ??= now;
            return now - _disconnectedSince.Value > GracePeriod;
        }
    }
}
