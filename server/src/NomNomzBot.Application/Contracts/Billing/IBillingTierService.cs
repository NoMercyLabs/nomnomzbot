// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Application.Contracts.Billing;

/// <summary>
/// Tier catalog + entitlement resolution (monetization-billing.md §3.2) — the single tier-aware source that
/// feature-gating and quota checks read. Self-host resolves every limit to unlimited; <c>IFeatureGateService</c>
/// consults this, it is never forked.
/// </summary>
public interface IBillingTierService
{
    /// <summary>The public tier catalogue (ordered by <c>SortOrder</c>) with limits — drives the pricing/upgrade UI.</summary>
    Task<Result<IReadOnlyList<TierDto>>> GetPublicTiersAsync(CancellationToken ct = default);

    /// <summary>
    /// A tenant's effective entitlement: active tier key, commercial flags, and the full LimitKey→value map.
    /// Self-host returns the unlimited/founder profile.
    /// </summary>
    Task<Result<EntitlementDto>> GetEntitlementAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>One limit value for a tenant (<c>-1</c> = unlimited; an unseeded key is unlimited). Hot-path convenience.</summary>
    Task<Result<long>> GetLimitAsync(
        Guid broadcasterId,
        string limitKey,
        CancellationToken ct = default
    );

    /// <summary>True iff the tenant's active tier ranks at or above the required tier (by <c>SortOrder</c>).</summary>
    Task<Result<bool>> IsTierAtLeastAsync(
        Guid broadcasterId,
        string requiredTierKey,
        CancellationToken ct = default
    );
}
