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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Cross-channel savings jars (economy.md §5). The service's membership predicate is the real cross-tenant
/// guard; the controller binds the jar id from the route, the contributor from the authenticated caller, and a
/// withdrawal's actor from the caller — never from the body.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/economy/jars")]
[Authorize]
[Tags("Economy — Savings Jars")]
public class SavingsJarsController(ISavingsJarService jars, ICurrentUserService currentUser)
    : BaseController
{
    [HttpGet]
    [RequireAction("economy:jars:read")]
    public async Task<IActionResult> ListJars(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await jars.ListJarsForChannelAsync(broadcasterId, ct));
    }

    [HttpPost]
    [RequireAction("economy:jars:create")]
    public async Task<IActionResult> CreateJar(
        string channelId,
        [FromBody] CreateSavingsJarRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await jars.CreateJarAsync(broadcasterId, request, ct));
    }

    [HttpGet("{jarId:guid}")]
    [RequireAction("economy:jars:read")]
    public async Task<IActionResult> GetJar(string channelId, Guid jarId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await jars.GetJarAsync(broadcasterId, jarId, ct));
    }

    [HttpPost("{jarId:guid}/invite")]
    [RequireAction("economy:jars:invite")]
    public async Task<IActionResult> Invite(
        string channelId,
        Guid jarId,
        [FromBody] InviteChannelRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        InviteChannelRequest bound = request with { JarId = jarId };
        return ResultResponse(await jars.InviteChannelAsync(broadcasterId, bound, ct));
    }

    [HttpPost("memberships/{membershipId:guid}/accept")]
    [RequireAction("economy:jars:membership:accept")]
    public async Task<IActionResult> AcceptMembership(
        string channelId,
        Guid membershipId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await jars.AcceptMembershipAsync(broadcasterId, membershipId, ct));
    }

    [HttpDelete("memberships/{membershipId:guid}")]
    [RequireAction("economy:jars:membership:revoke")]
    public async Task<IActionResult> RevokeMembership(
        string channelId,
        Guid membershipId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await jars.RevokeMembershipAsync(broadcasterId, membershipId, ct));
    }

    [HttpPost("{jarId:guid}/contribute")]
    [RequireAction("economy:jars:contribute")]
    public async Task<IActionResult> Contribute(
        string channelId,
        Guid jarId,
        [FromBody] JarContributeRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        JarContributeRequest bound = request with { JarId = jarId, ContributorUserId = caller };
        return ResultResponse(await jars.ContributeAsync(broadcasterId, bound, ct));
    }

    [HttpPost("{jarId:guid}/withdraw")]
    [RequireAction("economy:jars:withdraw")]
    public async Task<IActionResult> Withdraw(
        string channelId,
        Guid jarId,
        [FromBody] JarWithdrawRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        JarWithdrawRequest bound = request with { JarId = jarId, ActorUserId = caller };
        return ResultResponse(await jars.WithdrawAsync(broadcasterId, bound, ct));
    }

    [HttpGet("{jarId:guid}/history")]
    [RequireAction("economy:jars:history:read")]
    [ProducesResponseType<PaginatedResponse<JarMovementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        string channelId,
        Guid jarId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<JarMovementDto>> result = await jars.GetJarHistoryAsync(
            broadcasterId,
            jarId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
