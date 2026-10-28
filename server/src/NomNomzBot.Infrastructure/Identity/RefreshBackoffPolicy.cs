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

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The retry-storm guard every routine token refresher shares. A connection flagged <c>needs_reauth</c> or
/// <c>decrypt_failed</c> is never refreshed: only a fresh grant can fix it, and retrying re-confirms the same dead refresh token on
/// every poll (the Spotify path once reached 4653 consecutive failures). After any failed attempt, dead-grant
/// or transient, the next attempt waits out a window that grows with the dead-grant count.
/// </summary>
internal static class RefreshBackoffPolicy
{
    private static readonly TimeSpan[] Schedule =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    public static bool AllowsAttempt(
        string status,
        int consecutiveFailureCount,
        DateTime? lastErrorAt,
        DateTime now
    )
    {
        if (AuthEnums.IntegrationStatus.RequiresReconnect(status))
            return false;

        if (lastErrorAt is null)
            return true;

        TimeSpan window = Schedule[Math.Clamp(consecutiveFailureCount - 1, 0, Schedule.Length - 1)];
        return now >= lastErrorAt.Value + window;
    }
}
