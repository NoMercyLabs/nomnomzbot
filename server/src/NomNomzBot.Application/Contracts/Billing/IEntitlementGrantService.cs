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
/// Comps and per-tenant entitlement grants (S-ADMIN-4b) — a time-boxed elevation of one tenant's effective
/// tier, distinct from <see cref="IBillingTierAdminService"/> (which edits the tier catalogue itself) and
/// from the numeric quota exceptions <c>IPlatformAdminService</c> issues via <c>TenantLimitOverride</c>. A
/// grant is read by <see cref="IBillingTierService"/>'s tier resolution, so it actually gates
/// <c>IsTierAtLeastAsync</c>/<c>GetEntitlementAsync</c> and every capability built on them — never a row that
/// looks like an entitlement without the check reading it. Same counted-preview-then-apply shape as
/// <see cref="IBillingTierAdminService"/>: preview the diff, then issue with that count echoed back; a stale
/// count fails closed.
/// </summary>
public interface IEntitlementGrantService
{
    /// <summary>Every LIVE grant (not expired, not soft-deleted) for the tenant, newest first.</summary>
    Task<Result<IReadOnlyList<EntitlementGrantDto>>> ListGrantsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// The real, counted diff between the tenant's current resolved entitlement and <paramref name="tierId"/>
    /// — call before issuing a grant so the operator sees the blast radius before it commits.
    /// </summary>
    Task<Result<EntitlementGrantPreviewDto>> PreviewGrantAsync(
        Guid broadcasterId,
        Guid tierId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Issue a comp. Recomputes the diff fresh and rejects (<c>PREVIEW_STALE</c>) if it no longer matches
    /// <see cref="IssueEntitlementGrantRequest.ConfirmedChangedLimitCount"/>. Persists the grant and an
    /// <c>IamAuditLog</c> row in the same transaction.
    /// </summary>
    Task<Result<EntitlementGrantDto>> IssueGrantAsync(
        Guid broadcasterId,
        IssueEntitlementGrantRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    );
}
