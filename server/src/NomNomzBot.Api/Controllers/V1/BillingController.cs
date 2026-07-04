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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Tenant self-serve billing (monetization-billing.md §5.1). Billing is owner-level control — every endpoint,
/// reads included, is Broadcaster-floor (mods/editors never see or touch billing). All operations act on the
/// route channel; the subject is the route, never the body.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/billing")]
[Authorize]
[Tags("Billing")]
public class BillingController(
    ISubscriptionService subscriptions,
    IBillingTierService tiers,
    IUsageMeteringService metering,
    IInviteCodeService invites
) : BaseController
{
    /// <summary>Get the channel's current subscription state.</summary>
    [HttpGet("subscription")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<SubscriptionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubscription(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await subscriptions.GetSubscriptionAsync(broadcasterId, ct));
    }

    /// <summary>List the publicly offered billing tiers (channel-independent catalogue).</summary>
    [HttpGet("tiers")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TierDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTiers(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid _))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await tiers.GetPublicTiersAsync(ct));
    }

    /// <summary>Get the channel's resolved entitlement — the effective features and limits of its tier.</summary>
    [HttpGet("entitlement")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<EntitlementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEntitlement(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await tiers.GetEntitlementAsync(broadcasterId, ct));
    }

    /// <summary>Get the channel's current metered usage across billed metrics.</summary>
    [HttpGet("usage")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<UsageMetricDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetUsage(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await metering.GetCurrentUsageAsync(broadcasterId, ct));
    }

    /// <summary>List the channel's invoices, paginated.</summary>
    [HttpGet("invoices")]
    [RequireAction("billing:read")]
    [ProducesResponseType<PaginatedResponse<InvoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvoices(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<InvoiceDto>> result = await subscriptions.ListInvoicesAsync(
            broadcasterId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Start a checkout session for the requested tier, returning the provider's checkout URL.</summary>
    [HttpPost("checkout")]
    [RequireAction("billing:manage")]
    [ProducesResponseType<StatusResponseDto<CheckoutSessionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout(
        string channelId,
        [FromBody] StartCheckoutRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await subscriptions.StartCheckoutAsync(broadcasterId, request, ct));
    }

    /// <summary>Change the channel's subscription to another tier, immediately or at period end.</summary>
    [HttpPost("change-tier")]
    [RequireAction("billing:manage")]
    public async Task<IActionResult> ChangeTier(
        string channelId,
        [FromBody] ChangeTierRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await subscriptions.ChangeTierAsync(broadcasterId, request, ct));
    }

    /// <summary>Cancel the channel's subscription, immediately or at period end.</summary>
    [HttpPost("cancel")]
    [RequireAction("billing:manage")]
    public async Task<IActionResult> Cancel(
        string channelId,
        [FromBody] CancelSubscriptionRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await subscriptions.CancelAsync(broadcasterId, request, ct));
    }

    /// <summary>Resume the channel's subscription, undoing a pending cancellation.</summary>
    [HttpPost("resume")]
    [RequireAction("billing:manage")]
    public async Task<IActionResult> Resume(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await subscriptions.ResumeAsync(broadcasterId, ct));
    }

    /// <summary>Create a billing-provider portal session for self-serve payment management.</summary>
    [HttpPost("portal")]
    [RequireAction("billing:manage")]
    [ProducesResponseType<StatusResponseDto<BillingPortalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Portal(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(
            await subscriptions.CreateBillingPortalSessionAsync(broadcasterId, ct)
        );
    }

    /// <summary>Get the channel's founders-badge status.</summary>
    [HttpGet("founders-badge")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<FoundersBadgeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFoundersBadge(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await invites.GetFoundersBadgeAsync(broadcasterId, ct));
    }

    /// <summary>Validate an invite code without redeeming it.</summary>
    [HttpPost("invite/validate")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<InviteCodeValidationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateInvite(
        string channelId,
        [FromBody] InviteCodeRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid _))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await invites.ValidateAsync(request.Code, ct));
    }

    /// <summary>Redeem an invite code for the channel, applying its tier and/or founders-badge grant.</summary>
    [HttpPost("invite/redeem")]
    [RequireAction("billing:manage")]
    [ProducesResponseType<StatusResponseDto<RedeemInviteCodeResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RedeemInvite(
        string channelId,
        [FromBody] InviteCodeRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await invites.RedeemAsync(broadcasterId, request.Code, ct));
    }
}
