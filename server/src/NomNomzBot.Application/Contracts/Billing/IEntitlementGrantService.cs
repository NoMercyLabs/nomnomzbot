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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Application.Contracts.Billing;

/// <summary>
/// Gifts (comps) and per-tenant entitlement grants (S-ADMIN-4b, S-GIFT-PLAN) — an elevation of one tenant's
/// effective tier for N months or forever, distinct from <see cref="IBillingTierAdminService"/> (which edits the tier catalogue itself) and
/// from the numeric quota exceptions <c>IPlatformAdminService</c> issues via <c>TenantLimitOverride</c>. A
/// grant is read by <see cref="IBillingTierService"/>'s tier resolution, so it actually gates
/// <c>IsTierAtLeastAsync</c>/<c>GetEntitlementAsync</c> and every capability built on them — never a row that
/// looks like an entitlement without the check reading it. Same counted-preview-then-apply shape as
/// <see cref="IBillingTierAdminService"/>: preview the diff, then issue with that count echoed back; a stale
/// count fails closed.
/// </summary>
public interface IEntitlementGrantService
{
    /// <summary>Every LIVE grant (forever or not yet ended, not soft-deleted) for the tenant, newest first.</summary>
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

    /// <summary>
    /// Issue the gift an invite code carries (S-INVITE-TIER): <paramref name="months"/> from now, or forever when
    /// null. No operator stands behind a redemption, so there is no preview count to confirm and the grant has no
    /// issuing admin. The grant names <paramref name="inviteCodeId"/>, and its save commits every row the caller
    /// tracked on the same context (the redemption count, a founders badge) in one transaction.
    /// </summary>
    Task<Result<EntitlementGrantDto>> IssueInviteGiftAsync(
        Guid broadcasterId,
        Guid tierId,
        int? months,
        Guid inviteCodeId,
        string code,
        CancellationToken ct = default
    );

    /// <summary>
    /// End a live gift now: its end date becomes this moment, so the tier resolves back at once. The row stays
    /// as the record of what was given, and an <c>IamAuditLog</c> row is written in the same transaction.
    /// </summary>
    Task<Result<EntitlementGrantDto>> EndGrantAsync(
        Guid broadcasterId,
        Guid grantId,
        Guid? actorUserId,
        CancellationToken ct = default
    );
}
