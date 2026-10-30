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

using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// The one rule for a LIVE gift (<see cref="EntitlementGrant"/>, S-GIFT-PLAN): not soft-deleted, and either
/// forever (no end date) or not ended yet. Every reader that resolves, lists or counts gifts filters through
/// this, so a forever gift can never be live in one place and lapsed in another.
/// </summary>
internal static class LiveEntitlementGrants
{
    public static IQueryable<EntitlementGrant> WhereLive(
        this IQueryable<EntitlementGrant> grants,
        DateTime now
    ) => grants.Where(g => g.DeletedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now));
}
