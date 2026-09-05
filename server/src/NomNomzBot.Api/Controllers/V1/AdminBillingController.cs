// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Platform-admin billing (monetization-billing.md §5.3) — invite-code administration + manual tier/founder
/// grants + invoice refunds. Plane-C IAM gates per the §5.3 rows: reads on <c>billing:read</c>, invite
/// minting/revocation and manual grants on <c>billing:write</c>, refunds on <c>billing:refund</c> (policy
/// name = <c>IamPermission.Key</c> verbatim, audited on SaaS) — the <c>platform-billing</c> role holds all
/// three; <c>iam:manage</c> stays reserved for actual IAM administration.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/billing")]
[Authorize]
[Tags("Admin")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class AdminBillingController(
    IInviteCodeService invites,
    ISubscriptionService subscriptions,
    IBillingTierAdminService tiers,
    IEntitlementGrantService grants,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>List every tier — public and internal — for the admin tier editor.</summary>
    [HttpGet("tiers")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.BillingRead)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TierDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTiers(CancellationToken ct) =>
        ResultResponse(await tiers.ListAllTiersAsync(ct));

    /// <summary>
    /// The counted blast radius of editing this tier — the real number of tenants on it right now. Call this
    /// before <see cref="UpdateTier"/> and echo its <c>AffectedTenantCount</c> back as
    /// <c>ConfirmedAffectedTenantCount</c>.
    /// </summary>
    [HttpGet("tiers/{tierId:guid}/preview")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.BillingRead)]
    [ProducesResponseType<StatusResponseDto<TierChangePreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewTierChange(Guid tierId, CancellationToken ct) =>
        ResultResponse(await tiers.PreviewTierChangeAsync(tierId, ct));

    /// <summary>Author a brand-new tier. Zero blast radius by construction — no confirmation required.</summary>
    [HttpPost("tiers")]
    [Authorize(Policy = IamPermissionKeys.BillingWrite)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> CreateTier(
        [FromBody] CreateTierRequest request,
        CancellationToken ct
    ) => ResultResponse(await tiers.CreateTierAsync(request, Caller(), ct));

    /// <summary>
    /// Edit an existing tier's price, feature flags and limits. Fails closed (<c>PREVIEW_STALE</c>, 409) if
    /// <see cref="UpdateTierRequest.ConfirmedAffectedTenantCount"/> no longer matches a freshly recomputed
    /// count of tenants on the tier.
    /// </summary>
    [HttpPut("tiers/{tierId:guid}")]
    [Authorize(Policy = IamPermissionKeys.BillingWrite)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> UpdateTier(
        Guid tierId,
        [FromBody] UpdateTierRequest request,
        CancellationToken ct
    ) => ResultResponse(await tiers.UpdateTierAsync(tierId, request, Caller(), ct));

    private Guid? Caller() => Guid.TryParse(currentUser.UserId, out Guid id) ? id : null;

    /// <summary>List all invite codes platform-wide, paginated.</summary>
    [HttpGet("invites")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.BillingRead)]
    [ProducesResponseType<PaginatedResponse<InviteCodeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvites(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<InviteCodeDto>> result = await invites.ListInviteCodesAsync(
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Mint an invite code with redemption cap, optional tier/founders-badge grant, and expiry.</summary>
    [HttpPost("invites")]
    [Authorize(Policy = IamPermissionKeys.BillingWrite)]
    public async Task<IActionResult> CreateInvite(
        [FromBody] CreateInviteCodeRequest request,
        CancellationToken ct
    ) => ResultResponse(await invites.CreateInviteCodeAsync(request, ct));

    /// <summary>Revoke an invite code, blocking further redemptions.</summary>
    [HttpPost("invites/{inviteCodeId:guid}/revoke")]
    [Authorize(Policy = IamPermissionKeys.BillingWrite)]
    public async Task<IActionResult> RevokeInvite(Guid inviteCodeId, CancellationToken ct) =>
        ResultResponse(await invites.RevokeInviteCodeAsync(inviteCodeId, ct));

    /// <summary>Manually grant a channel a tier without Stripe, optionally marked as an invite-only grant.</summary>
    [HttpPost("channels/{broadcasterId:guid}/grant-tier")]
    [Authorize(Policy = IamPermissionKeys.BillingWrite)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> GrantTier(
        Guid broadcasterId,
        [FromBody] GrantTierRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await subscriptions.GrantTierAsync(
                broadcasterId,
                request.TierId,
                request.IsInviteOnlyGrant,
                ct
            )
        );

    /// <summary>Grant a channel the founders badge directly.</summary>
    [HttpPost("channels/{broadcasterId:guid}/grant-founder")]
    [Authorize(Policy = IamPermissionKeys.BillingWrite)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> GrantFounder(Guid broadcasterId, CancellationToken ct) =>
        ResultResponse(await invites.GrantFoundersBadgeAsync(broadcasterId, ct));

    /// <summary>Refund a paid invoice in full via Stripe; marks the local invoice <c>Refunded</c>.</summary>
    [HttpPost("invoices/{invoiceId:guid}/refund")]
    [Authorize(Policy = IamPermissionKeys.BillingRefund)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> RefundInvoice(Guid invoiceId, CancellationToken ct) =>
        ResultResponse(await subscriptions.RefundInvoiceAsync(invoiceId, ct));

    /// <summary>Every LIVE comp/entitlement grant (S-ADMIN-4b) for a channel, newest first.</summary>
    [HttpGet("channels/{broadcasterId:guid}/grants")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.BillingRead)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<EntitlementGrantDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListGrants(Guid broadcasterId, CancellationToken ct) =>
        ResultResponse(await grants.ListGrantsAsync(broadcasterId, ct));

    /// <summary>
    /// The counted blast radius of comping this channel to <paramref name="tierId"/> — the limit keys that
    /// would actually change. Call this before <see cref="IssueGrant"/> and echo its
    /// <c>ChangedLimitCount</c> back as <c>ConfirmedChangedLimitCount</c>.
    /// </summary>
    [HttpGet("channels/{broadcasterId:guid}/grants/preview")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.BillingRead)]
    [ProducesResponseType<StatusResponseDto<EntitlementGrantPreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewGrant(
        Guid broadcasterId,
        [FromQuery] Guid tierId,
        CancellationToken ct
    ) => ResultResponse(await grants.PreviewGrantAsync(broadcasterId, tierId, ct));

    /// <summary>
    /// Issue a comp — a time-boxed entitlement grant with a mandatory reason and expiry. Fails closed
    /// (<c>PREVIEW_STALE</c>, 409) if <see cref="IssueEntitlementGrantRequest.ConfirmedChangedLimitCount"/>
    /// no longer matches a freshly recomputed diff.
    /// </summary>
    [HttpPost("channels/{broadcasterId:guid}/grants")]
    [Authorize(Policy = IamPermissionKeys.BillingWrite)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> IssueGrant(
        Guid broadcasterId,
        [FromBody] IssueEntitlementGrantRequest request,
        CancellationToken ct
    ) => ResultResponse(await grants.IssueGrantAsync(broadcasterId, request, Caller(), ct));
}
