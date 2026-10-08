// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The seven-day dry-run observation window a channel serves before spam defence may act
/// (spam-defense.md §6.2). Shared by the message path and the follow-bot sweep, so turning dry run off
/// early cannot skip it on either.
/// </summary>
internal static class SpamObservationWindow
{
    private const int ObservationDays = 7;

    /// <summary>When the channel may first act: its stamped clock, else seven days after it was onboarded.</summary>
    public static async Task<DateTime?> EligibleAtAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        DateTime? stamped = await db
            .SpamDefensePolicies.IgnoreQueryFilters()
            .Where(p => p.BroadcasterId == broadcasterId && p.DeletedAt == null)
            .Select(p => p.EnforcementEligibleAt)
            .FirstOrDefaultAsync(ct);
        if (stamped is not null)
            return stamped;

        DateTime? created = await db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.Id == broadcasterId)
            .Select(c => (DateTime?)c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return created?.AddDays(ObservationDays);
    }

    public static async Task<bool> IsActiveAsync(
        IApplicationDbContext db,
        TimeProvider time,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        DateTime? eligibleAt = await EligibleAtAsync(db, broadcasterId, ct);
        return eligibleAt is not null && time.GetUtcNow().UtcDateTime < eligibleAt.Value;
    }
}
