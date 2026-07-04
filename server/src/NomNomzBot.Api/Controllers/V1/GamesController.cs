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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Mini-games + fun-money gambling, and the 18+ consent surface (economy.md §5). Game config is
/// management-floored; playing is community (the optional 18+ gate runs in the service only when the streamer
/// enabled it). A play binds the player + role level from the caller; granting consent binds the subject to the
/// caller (you confirm your own age) — never the body.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/economy/games")]
[Authorize]
[Tags("Economy — Games")]
public class GamesController(
    IGameService games,
    IAgeConsentService ageConsent,
    IRoleResolver roles,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>List the channel's mini-game configurations.</summary>
    [HttpGet]
    [RequireAction("economy:games:read")]
    public async Task<IActionResult> ListGames(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await games.ListGamesAsync(broadcasterId, ct));
    }

    /// <summary>Create or update a mini-game configuration for the channel.</summary>
    [HttpPut]
    [RequireAction("economy:games:write")]
    public async Task<IActionResult> UpsertGame(
        string channelId,
        [FromBody] UpsertGameConfigRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await games.UpsertGameAsync(broadcasterId, request, ct));
    }

    /// <summary>Play a configured game as the authenticated caller, with the player's role level resolved server-side.</summary>
    [HttpPost("{gameConfigId:guid}/play")]
    [RequireAction("economy:games:play")]
    public async Task<IActionResult> Play(
        string channelId,
        Guid gameConfigId,
        [FromBody] PlayGameRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        Result<int> level = await roles.ResolveEffectiveLevelAsync(caller, broadcasterId, ct);
        if (level.IsFailure)
            return ResultResponse(level);
        PlayGameRequest bound = request with
        {
            GameConfigId = gameConfigId,
            PlayerUserId = caller,
            RoleLevel = level.Value,
        };
        return ResultResponse(await games.PlayAsync(broadcasterId, bound, ct));
    }

    /// <summary>Page through the channel's game-play history, filtered.</summary>
    [HttpGet("history")]
    [RequireAction("economy:games:history:read")]
    [ProducesResponseType<PaginatedResponse<GamePlayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        string channelId,
        [FromQuery] GameHistoryFilter filter,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<GamePlayDto>> result = await games.GetGameHistoryAsync(
            broadcasterId,
            filter,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Check whether a viewer has granted 18+ consent on this channel.</summary>
    [HttpGet("consent/{viewerUserId:guid}")]
    [RequireAction("economy:consent:read")]
    public async Task<IActionResult> GetConsent(
        string channelId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await ageConsent.HasGrantedAsync(broadcasterId, viewerUserId, ct));
    }

    /// <summary>Grant 18+ consent for the authenticated caller — the subject is always the caller confirming their own age.</summary>
    [HttpPost("consent")]
    [RequireAction("economy:consent:write")]
    public async Task<IActionResult> GrantConsent(
        string channelId,
        [FromBody] GrantAgeConsentRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        // You confirm your OWN age — the subject is always the caller.
        GrantAgeConsentRequest bound = request with
        {
            ViewerUserId = caller,
        };
        return ResultResponse(await ageConsent.GrantAsync(broadcasterId, bound, ct));
    }

    /// <summary>Revoke a viewer's 18+ consent on this channel.</summary>
    [HttpDelete("consent/{viewerUserId:guid}")]
    [RequireAction("economy:consent:revoke")]
    public async Task<IActionResult> RevokeConsent(
        string channelId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await ageConsent.RevokeAsync(broadcasterId, viewerUserId, ct));
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
