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
    [HttpGet("subscription")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<SubscriptionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubscription(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await subscriptions.GetSubscriptionAsync(broadcasterId, ct));
    }

    [HttpGet("tiers")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TierDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTiers(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid _))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await tiers.GetPublicTiersAsync(ct));
    }

    [HttpGet("entitlement")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<EntitlementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEntitlement(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await tiers.GetEntitlementAsync(broadcasterId, ct));
    }

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

    [HttpPost("resume")]
    [RequireAction("billing:manage")]
    public async Task<IActionResult> Resume(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await subscriptions.ResumeAsync(broadcasterId, ct));
    }

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

    [HttpGet("founders-badge")]
    [RequireAction("billing:read")]
    [ProducesResponseType<StatusResponseDto<FoundersBadgeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFoundersBadge(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await invites.GetFoundersBadgeAsync(broadcasterId, ct));
    }

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
