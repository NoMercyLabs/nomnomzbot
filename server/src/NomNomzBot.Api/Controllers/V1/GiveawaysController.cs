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
/// Giveaway campaigns (giveaways.md §6): CRUD + the open/close/draw/redraw lifecycle + winner history.
/// Gate 1 is <c>[Authorize]</c> + tenant resolution; Gate 2 floors read/write at Moderator
/// (<c>giveaways:read</c> / <c>giveaways:write</c>) — the broadcaster-only code reveal lives on the
/// code-pools controller (<c>giveaways:codes:write</c>).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/giveaways")]
[Authorize]
[Tags("Giveaways")]
public class GiveawaysController : BaseController
{
    private readonly IGiveawayService _giveaways;
    private readonly ICurrentTenantService _tenant;

    public GiveawaysController(IGiveawayService giveaways, ICurrentTenantService tenant)
    {
        _giveaways = giveaways;
        _tenant = tenant;
    }

    /// <summary>List the channel's giveaways (non-archived by default; ?status= filters).</summary>
    [RequireAction("giveaways:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<GiveawayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<PagedList<GiveawayDto>> result = await _giveaways.ListAsync(
            broadcasterId,
            new GiveawayFilter(status),
            new PaginationParams(request.Page, request.Take, request.Sort, request.Order),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Create a giveaway (draft).</summary>
    [RequireAction("giveaways:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<GiveawayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        [FromBody] UpsertGiveawayRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _giveaways.CreateAsync(broadcasterId, request, ct));
    }

    /// <summary>Read one giveaway with its live entry count.</summary>
    [RequireAction("giveaways:read")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<GiveawayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _giveaways.GetAsync(broadcasterId, id, ct));
    }

    /// <summary>Update a draft/closed giveaway's configuration.</summary>
    [RequireAction("giveaways:write")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<GiveawayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpsertGiveawayRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _giveaways.UpdateAsync(broadcasterId, id, request, ct));
    }

    /// <summary>Soft-delete a giveaway.</summary>
    [RequireAction("giveaways:write")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _giveaways.DeleteAsync(broadcasterId, id, ct));
    }

    /// <summary>Open for entries — one active giveaway per channel.</summary>
    [RequireAction("giveaways:write")]
    [HttpPost("{id:guid}/open")]
    [ProducesResponseType<StatusResponseDto<GiveawayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Open(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _giveaways.OpenAsync(broadcasterId, id, ct));
    }

    /// <summary>Stop accepting entries (the giveaway stays drawable).</summary>
    [RequireAction("giveaways:write")]
    [HttpPost("{id:guid}/close")]
    [ProducesResponseType<StatusResponseDto<GiveawayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _giveaways.CloseAsync(broadcasterId, id, ct));
    }

    /// <summary>Draw the winners (weighted CSPRNG) and fulfill per the prize mode.</summary>
    [RequireAction("giveaways:write")]
    [HttpPost("{id:guid}/draw")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<GiveawayWinnerDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> Draw(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _giveaways.DrawAsync(broadcasterId, id, ct));
    }

    /// <summary>Replace one winner (forfeit/no-show) with a fresh draw.</summary>
    [RequireAction("giveaways:write")]
    [HttpPost("{id:guid}/winners/{winnerId:guid}/redraw")]
    [ProducesResponseType<StatusResponseDto<GiveawayWinnerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Redraw(Guid id, Guid winnerId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _giveaways.RedrawAsync(broadcasterId, id, winnerId, ct));
    }

    /// <summary>Winner history (append-only), paginated.</summary>
    [RequireAction("giveaways:read")]
    [HttpGet("{id:guid}/winners")]
    [ProducesResponseType<PaginatedResponse<GiveawayWinnerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Winners(
        Guid id,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<PagedList<GiveawayWinnerDto>> result = await _giveaways.GetWinnersAsync(
            broadcasterId,
            id,
            new PaginationParams(request.Page, request.Take, request.Sort, request.Order),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }
}
