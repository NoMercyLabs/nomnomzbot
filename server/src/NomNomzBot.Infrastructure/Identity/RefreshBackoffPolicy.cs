// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The retry-storm guard every routine token refresher shares. A connection flagged <c>needs_reauth</c> is
/// never refreshed: only a fresh grant can fix it, and retrying re-confirms the same dead refresh token on
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
        if (status == AuthEnums.IntegrationStatus.NeedsReauth)
            return false;

        if (lastErrorAt is null)
            return true;

        TimeSpan window = Schedule[Math.Clamp(consecutiveFailureCount - 1, 0, Schedule.Length - 1)];
        return now >= lastErrorAt.Value + window;
    }
}
