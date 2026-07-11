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
using NomNomzBot.Application.Engagement.Dtos;
using NomNomzBot.Application.Engagement.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Engagement-trigger configuration (engagement.md §5) — the auto-greeting / loyalty-recognition layer.
/// The trigger BINDINGS themselves are ordinary event-responses/pipelines (gated by the pipeline editor);
/// this surface just toggles which moments the detector fires and tunes the streak milestones + cooldown.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/engagement")]
[Authorize]
[Tags("Engagement")]
public class EngagementController : BaseController
{
    private readonly IEngagementService _engagement;
    private readonly ICurrentTenantService _tenant;

    public EngagementController(IEngagementService engagement, ICurrentTenantService tenant)
    {
        _engagement = engagement;
        _tenant = tenant;
    }

    /// <summary>The channel's engagement-trigger config (defaults — all off — when never set).</summary>
    [RequireAction("engagement:read")]
    [HttpGet("config")]
    [ProducesResponseType<StatusResponseDto<EngagementConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _engagement.GetConfigAsync(broadcasterId, ct));
    }

    /// <summary>Enable/disable the trigger moments and tune milestones + greet cooldown.</summary>
    [RequireAction("engagement:write")]
    [HttpPut("config")]
    [ProducesResponseType<StatusResponseDto<EngagementConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig(
        [FromBody] UpdateEngagementConfigRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _engagement.UpdateConfigAsync(broadcasterId, request, ct));
    }
}
