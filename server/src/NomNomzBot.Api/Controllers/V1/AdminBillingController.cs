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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Platform-admin billing (monetization-billing.md §5.3) — invite-code administration + manual tier/founder
/// grants. Plane-C operations; gated by the platform admin role (the Plane-C IAM policy gate
/// <c>billing:read</c>/<c>iam:manage</c> is the target — currently the live <c>admin</c> role gate).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/billing")]
[Authorize(Roles = "admin")]
[Tags("Admin")]
public class AdminBillingController(IInviteCodeService invites, ISubscriptionService subscriptions)
    : BaseController
{
    [HttpGet("invites")]
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

    [HttpPost("invites")]
    public async Task<IActionResult> CreateInvite(
        [FromBody] CreateInviteCodeRequest request,
        CancellationToken ct
    ) => ResultResponse(await invites.CreateInviteCodeAsync(request, ct));

    [HttpPost("invites/{inviteCodeId:guid}/revoke")]
    public async Task<IActionResult> RevokeInvite(Guid inviteCodeId, CancellationToken ct) =>
        ResultResponse(await invites.RevokeInviteCodeAsync(inviteCodeId, ct));

    [HttpPost("channels/{broadcasterId:guid}/grant-tier")]
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

    [HttpPost("channels/{broadcasterId:guid}/grant-founder")]
    public async Task<IActionResult> GrantFounder(Guid broadcasterId, CancellationToken ct) =>
        ResultResponse(await invites.GrantFoundersBadgeAsync(broadcasterId, ct));
}
