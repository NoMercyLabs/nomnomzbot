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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Economy leaderboards (economy.md §5): config CRUD (management), the live ranking (community — Everyone when
/// the config is public), and the viewer opt-out / opt-in toggles.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/economy/leaderboards")]
[Authorize]
[Tags("Economy — Leaderboards")]
public class EconomyLeaderboardsController(IEconomyLeaderboardService leaderboards) : BaseController
{
    /// <summary>List the channel's leaderboard configurations.</summary>
    [HttpGet("configs")]
    [RequireAction("economy:leaderboards:config:read")]
    public async Task<IActionResult> ListConfigs(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await leaderboards.ListConfigsAsync(broadcasterId, ct));
    }

    /// <summary>Create or update a leaderboard configuration for the channel.</summary>
    [HttpPut("configs")]
    [RequireAction("economy:leaderboards:config:write")]
    public async Task<IActionResult> UpsertConfig(
        string channelId,
        [FromBody] UpsertLeaderboardConfigRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await leaderboards.UpsertConfigAsync(broadcasterId, request, ct));
    }

    /// <summary>Delete a leaderboard configuration by id.</summary>
    [HttpDelete("configs/{configId:guid}")]
    [RequireAction("economy:leaderboards:config:delete")]
    public async Task<IActionResult> DeleteConfig(
        string channelId,
        Guid configId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await leaderboards.DeleteConfigAsync(broadcasterId, configId, ct));
    }

    /// <summary>Read the live ranking for a leaderboard, optionally limited to the top N entries.</summary>
    [HttpGet("{configId:guid}")]
    [RequireAction("economy:leaderboards:read")]
    public async Task<IActionResult> GetRanking(
        string channelId,
        Guid configId,
        [FromQuery] int? top,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await leaderboards.GetRankingAsync(broadcasterId, configId, top, ct));
    }

    /// <summary>Opt a viewer out of the channel's leaderboards, hiding them from rankings.</summary>
    [HttpPost("opt-out/{viewerUserId:guid}")]
    [RequireAction("economy:leaderboards:opt-out")]
    public async Task<IActionResult> OptOut(
        string channelId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await leaderboards.OptOutAsync(broadcasterId, viewerUserId, ct));
    }

    /// <summary>Opt a viewer back into the channel's leaderboards.</summary>
    [HttpPost("opt-in/{viewerUserId:guid}")]
    [RequireAction("economy:leaderboards:opt-in")]
    public async Task<IActionResult> OptIn(
        string channelId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await leaderboards.OptInAsync(broadcasterId, viewerUserId, ct));
    }
}
