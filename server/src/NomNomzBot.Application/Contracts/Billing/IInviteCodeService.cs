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
/// Invite codes + founders badge (monetization-billing.md §3.4). Codes are GLOBAL; redemption grants the calling
/// tenant a founders badge and/or a tier (via <see cref="ISubscriptionService.GrantTierAsync"/>).
/// </summary>
public interface IInviteCodeService
{
    /// <summary>Validates a code without consuming it; returns the would-be grants for preview. NOT_FOUND on unknown.</summary>
    Task<Result<InviteCodeValidationDto>> ValidateAsync(
        string code,
        CancellationToken ct = default
    );

    /// <summary>
    /// Redeems a code for the tenant: increments the count (guarded), grants the badge and/or tier, publishes the
    /// redeemed (+ badge) events. ALREADY_EXISTS if already redeemed; RATE_LIMITED/VALIDATION_FAILED if
    /// exhausted/expired.
    /// </summary>
    Task<Result<RedeemInviteCodeResultDto>> RedeemAsync(
        Guid broadcasterId,
        string code,
        CancellationToken ct = default
    );

    /// <summary>The tenant's founders badge, or a null DTO when none.</summary>
    Task<Result<FoundersBadgeDto?>> GetFoundersBadgeAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    // ── Platform-admin (Plane-C) ──

    /// <summary>Creates an invite code. VALIDATION_FAILED on a bad max-redemptions / tier.</summary>
    Task<Result<InviteCodeDto>> CreateInviteCodeAsync(
        CreateInviteCodeRequest request,
        CancellationToken ct = default
    );

    /// <summary>Paginated invite-code list with live redemption counts.</summary>
    Task<Result<PagedList<InviteCodeDto>>> ListInviteCodesAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Expires a code now so it can no longer be redeemed; existing grants are untouched.</summary>
    Task<Result> RevokeInviteCodeAsync(Guid inviteCodeId, CancellationToken ct = default);

    /// <summary>Admin-grants a founders badge directly (no invite); publishes the badge event.</summary>
    Task<Result<FoundersBadgeDto>> GrantFoundersBadgeAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
