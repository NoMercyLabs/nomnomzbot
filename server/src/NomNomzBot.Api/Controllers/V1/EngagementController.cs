// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _engagement.GetConfigAsync(broadcasterId, ct));
    }

    /// <summary>The config a channel with nothing saved resolves to, so the dashboard shows the real defaults.</summary>
    [RequireAction("engagement:read")]
    [HttpGet("config/defaults")]
    [ProducesResponseType<StatusResponseDto<EngagementConfigDto>>(StatusCodes.Status200OK)]
    public IActionResult GetConfigDefaults() =>
        Ok(new StatusResponseDto<EngagementConfigDto> { Data = _engagement.GetDefaults() });

    /// <summary>Enable/disable the trigger moments and tune milestones + greet cooldown.</summary>
    [RequireAction("engagement:write")]
    [HttpPut("config")]
    [ProducesResponseType<StatusResponseDto<EngagementConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig(
        [FromBody] UpdateEngagementConfigRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _engagement.UpdateConfigAsync(broadcasterId, request, ct));
    }
}
