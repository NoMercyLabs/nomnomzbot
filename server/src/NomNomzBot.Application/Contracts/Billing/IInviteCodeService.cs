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
/// Invite codes + founders badge (monetization-billing.md §3.4, S-INVITE-TIER). Codes are GLOBAL; redemption
/// gives the calling tenant a founders badge and/or a tier gift for N months or forever (an entitlement grant
/// through <see cref="IEntitlementGrantService.IssueInviteGiftAsync"/>, never a subscription row).
/// </summary>
public interface IInviteCodeService
{
    /// <summary>Validates a code without consuming it; returns the would-be grants for preview. NOT_FOUND on unknown.</summary>
    Task<Result<InviteCodeValidationDto>> ValidateAsync(
        string code,
        CancellationToken ct = default
    );

    /// <summary>
    /// What a code gives, for a signed-out visitor. Always succeeds: an unknown, used-up, expired or revoked code
    /// reads as one identical not-usable answer. Never spends a redemption.
    /// </summary>
    Task<Result<PublicInviteDto>> GetPublicInviteAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Redeems a code for the tenant: increments the count (guarded), grants the badge and/or the tier gift,
    /// publishes the redeemed (+ badge) events. ALREADY_EXISTS if this tenant already redeemed it;
    /// RATE_LIMITED/VALIDATION_FAILED if exhausted/expired. A failed gift spends nothing.
    /// </summary>
    Task<Result<RedeemInviteCodeResultDto>> RedeemAsync(
        Guid broadcasterId,
        string code,
        CancellationToken ct = default
    );

    /// <summary>
    /// Redeems a code on the onboarded channel the user owns (the oldest one), for a code carried through
    /// sign-in. NOT_FOUND, with nothing spent, when the user owns no onboarded channel.
    /// </summary>
    Task<Result<RedeemInviteCodeResultDto>> RedeemForOwnerAsync(
        Guid userId,
        string code,
        CancellationToken ct = default
    );

    /// <summary>The tenant's founders badge, or a null DTO when none.</summary>
    Task<Result<FoundersBadgeDto?>> GetFoundersBadgeAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    // ── Platform-admin (Plane-C) ──

    /// <summary>
    /// Creates an invite code. VALIDATION_FAILED on a bad max-redemptions / tier, a length outside 1 to 120
    /// months, or a length without a tier; nothing is stored on failure.
    /// </summary>
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
