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
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Middleware;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.PlatformDefaults.Services;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Platform defaults for Gate-2 action levels (plan item A4) — Plane C, gated on
/// <c>platform:defaults:manage</c>. The admin replaces an action's shipped default level for every channel
/// without its own override, at runtime: list, preview the counted blast radius, then save (the save echoes
/// the previewed count and is refused as stale when it moved).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/platform-defaults/actions")]
[PlatformPlane]
[Authorize(Policy = IamPermissionKeys.PlatformDefaultsManage)]
[Tags("Platform Defaults")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class ActionDefaultsAdminController(
    IActionDefaultsAdminService defaults,
    ICurrentUserService currentUser,
    IEventBus eventBus
) : BaseController
{
    /// <summary>Every gateable action with its shipped, platform and effective default level.</summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ActionDefaultDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> List(CancellationToken ct) =>
        ResultResponse(await defaults.ListAsync(ct));

    /// <summary>
    /// The counted blast radius of moving an action's default to <paramref name="level"/> (omit it to preview
    /// going back to the shipped default).
    /// </summary>
    [HttpGet("{actionKey}/blast-radius")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<PlatformDefaultBlastRadiusDto>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> Preview(
        string actionKey,
        [FromQuery] int? level,
        CancellationToken ct
    ) => ResultResponse(await defaults.PreviewAsync(actionKey, level, ct));

    /// <summary>Sets (or clears, with a null level) an action's platform default; returns the saved row.</summary>
    [HttpPut("{actionKey}")]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<ActionDefaultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        string actionKey,
        [FromBody] SetActionDefaultRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(currentUser.UserId, out Guid caller))
            return UnauthenticatedResponse();
        Result<ActionDefaultDto> saved = await defaults.SetAsync(actionKey, request, caller, ct);
        if (saved.IsSuccess)
            await eventBus.PublishAsync(
                new PlatformDefaultsChangedEvent { Set = "actions", Key = actionKey },
                ct
            );
        return ResultResponse(saved);
    }
}
