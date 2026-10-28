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
