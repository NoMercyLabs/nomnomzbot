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
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.PlatformDefaults.Services;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Platform defaults for Gate-2 action levels (plan item A4) — Plane C, gated on
/// <c>platform:defaults:manage</c>. The admin replaces an action's shipped default level for every channel
/// without its own override, at runtime: list, preview the counted blast radius, then save (the save echoes
/// the previewed count and is refused as stale when it moved).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/platform-defaults/actions")]
[Authorize(Policy = IamPermissionKeys.PlatformDefaultsManage)]
[Tags("Platform Defaults")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class ActionDefaultsAdminController(
    IActionDefaultsAdminService defaults,
    ICurrentUserService currentUser
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
        return ResultResponse(await defaults.SetAsync(actionKey, request, caller, ct));
    }
}
