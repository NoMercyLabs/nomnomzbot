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
/// Tier authoring (S-ADMIN-4a) — the admin write surface for the tier catalogue itself, distinct from
/// <see cref="IBillingTierService"/> which only ever READS tiers for entitlement resolution. Editing an
/// in-use tier follows the same counted-blast-radius shape platform content publish already uses: preview
/// first, then apply with the previewed count echoed back — a stale count fails closed.
/// </summary>
public interface IBillingTierAdminService
{
    /// <summary>Every tier — public and internal — ordered by <c>SortOrder</c>, for the admin tier editor.</summary>
    Task<Result<IReadOnlyList<TierDto>>> ListAllTiersAsync(CancellationToken ct = default);

    /// <summary>
    /// The real, counted set of tenants currently on this tier via an active/trialing subscription — call
    /// before editing an in-use tier so the owner sees the blast radius before it commits.
    /// </summary>
    Task<Result<TierChangePreviewDto>> PreviewTierChangeAsync(
        Guid tierId,
        CancellationToken ct = default
    );

    /// <summary>Author a brand-new tier. Zero blast radius by construction — no tenant can be on a tier
    /// that did not exist a moment ago.</summary>
    Task<Result<TierDto>> CreateTierAsync(
        CreateTierRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Edit an existing tier's price, feature flags, and limits. Recomputes the affected-tenant count fresh
    /// and rejects (<c>PREVIEW_STALE</c>) if it no longer matches <see cref="UpdateTierRequest.ConfirmedAffectedTenantCount"/>.
    /// Records an <c>IamAuditLog</c> row on success.
    /// </summary>
    Task<Result<TierDto>> UpdateTierAsync(
        Guid tierId,
        UpdateTierRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    );
}
