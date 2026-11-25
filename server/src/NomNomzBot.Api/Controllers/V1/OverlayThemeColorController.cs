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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>The channel's one theme color for all overlays (the configuration key <c>overlay.themeColor</c>).</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/overlay-theme-color")]
[Authorize]
[Tags("Widgets")]
public sealed class OverlayThemeColorController : BaseController
{
    private readonly IOverlayThemeColorService _service;
    private readonly ICurrentTenantService _currentTenant;

    public OverlayThemeColorController(
        IOverlayThemeColorService service,
        ICurrentTenantService currentTenant
    )
    {
        _service = service;
        _currentTenant = currentTenant;
    }

    /// <summary>Read the channel's theme color; empty means each overlay uses its own theme's color.</summary>
    [HttpGet]
    [RequireAction("widget:read")]
    [ProducesResponseType<StatusResponseDto<OverlayThemeColor>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Guid broadcasterId = _currentTenant.BroadcasterId ?? Guid.Empty;
        if (broadcasterId == Guid.Empty)
            return Unauthorized();

        Result<OverlayThemeColor> result = await _service.GetAsync(broadcasterId, ct);
        return ToResponse(result);
    }

    /// <summary>Set the theme color (<c>#RRGGBB</c>), or send an empty color to reset it.</summary>
    [HttpPut]
    [RequireAction("widget:write")]
    [ProducesResponseType<StatusResponseDto<OverlayThemeColor>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateOverlayThemeColorRequest request,
        CancellationToken ct
    )
    {
        Guid broadcasterId = _currentTenant.BroadcasterId ?? Guid.Empty;
        if (broadcasterId == Guid.Empty)
            return Unauthorized();

        Result<OverlayThemeColor> result = await _service.SetAsync(broadcasterId, request, ct);
        return ToResponse(result);
    }

    private IActionResult ToResponse(Result<OverlayThemeColor> result) =>
        result.IsSuccess
            ? Ok(new StatusResponseDto<OverlayThemeColor> { Data = result.Value })
            : BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });
}
