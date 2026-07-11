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
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Secret-safe giveaway code pools (giveaways.md §6, D6). Every route floors at BROADCASTER
/// (<c>giveaways:codes:write</c>) — pools hold valuable secrets, so even reads are held to the top:
/// list/detail responses are MASKED, and the single plaintext path is the failed-whisper reveal.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/giveaways/code-pools")]
[Authorize]
[Tags("Giveaways")]
public class GiveawayCodePoolsController : BaseController
{
    private readonly IGiveawayCodePoolService _pools;
    private readonly ICurrentTenantService _tenant;

    public GiveawayCodePoolsController(IGiveawayCodePoolService pools, ICurrentTenantService tenant)
    {
        _pools = pools;
        _tenant = tenant;
    }

    /// <summary>List the channel's code pools (counts only — never code contents).</summary>
    [RequireAction("giveaways:codes:write")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<CodePoolDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PageRequestDto request, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<PagedList<CodePoolDto>> result = await _pools.ListPoolsAsync(
            broadcasterId,
            new PaginationParams(request.Page, request.Take, request.Sort, request.Order),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Create a pool.</summary>
    [RequireAction("giveaways:codes:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<CodePoolDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCodePoolRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _pools.CreatePoolAsync(broadcasterId, request, ct));
    }

    /// <summary>Pool detail — the codes come back MASKED (label + status), never plaintext.</summary>
    [RequireAction("giveaways:codes:write")]
    [HttpGet("{poolId:guid}")]
    [ProducesResponseType<StatusResponseDto<CodePoolDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid poolId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _pools.GetPoolAsync(broadcasterId, poolId, ct));
    }

    /// <summary>Bulk-add codes — AEAD-encrypted on write, never echoed back.</summary>
    [RequireAction("giveaways:codes:write")]
    [HttpPost("{poolId:guid}/codes")]
    [ProducesResponseType<StatusResponseDto<CodePoolDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddCodes(
        Guid poolId,
        [FromBody] AddCodesRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _pools.AddCodesAsync(broadcasterId, poolId, request, ct));
    }

    /// <summary>Soft-delete a pool (blocked while it backs an active giveaway).</summary>
    [RequireAction("giveaways:codes:write")]
    [HttpDelete("{poolId:guid}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid poolId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _pools.DeletePoolAsync(broadcasterId, poolId, ct));
    }

    /// <summary>Reveal a winner's assigned code — the failed-whisper fallback, broadcaster-gated (D6).
    /// Routed under /giveaways to match the spec's `GET /giveaways/{id}/winners/{winnerId}/code`.</summary>
    [RequireAction("giveaways:codes:write")]
    [HttpGet("/api/v{version:apiVersion}/giveaways/{id:guid}/winners/{winnerId:guid}/code")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevealWinnerCode(Guid id, Guid winnerId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _pools.RevealAssignedCodeAsync(broadcasterId, winnerId, ct));
    }
}
