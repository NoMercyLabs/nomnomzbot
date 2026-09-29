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
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// A channel's live per-tenant limit overrides (<see cref="TenantLimitOverride"/>, S-ADMIN-3): the operator's
/// exception for one channel, which wins over the tier's limit for that key. Expired and cleared rows do not
/// apply. Read wherever a channel's limits are resolved, so every enforcer sees the same number.
/// </summary>
internal static class LiveLimitOverrides
{
    public static async Task<Dictionary<string, long>> LoadAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTime now,
        CancellationToken ct
    )
    {
        List<TenantLimitOverride> rows = await db
            .TenantLimitOverrides.Where(o =>
                o.BroadcasterId == broadcasterId
                && o.DeletedAt == null
                && (o.ExpiresAt == null || o.ExpiresAt > now)
            )
            .ToListAsync(ct);

        Dictionary<string, long> overrides = [];
        foreach (TenantLimitOverride row in rows)
            overrides[row.LimitKey] = row.LimitValue;
        return overrides;
    }

    /// <summary>The tier's limits with each overridden key replaced (or added) by its override.</summary>
    public static Dictionary<string, long> Overlay(
        IReadOnlyDictionary<string, long> limits,
        IReadOnlyDictionary<string, long> overrides
    )
    {
        Dictionary<string, long> effective = new(limits);
        foreach (KeyValuePair<string, long> entry in overrides)
            effective[entry.Key] = entry.Value;
        return effective;
    }
}
