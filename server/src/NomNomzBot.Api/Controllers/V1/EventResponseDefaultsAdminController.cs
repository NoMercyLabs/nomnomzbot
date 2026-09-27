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
using NomNomzBot.Api.Middleware;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.PlatformDefaults.Services;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Platform defaults for event responses (plan item A4) — Plane C, gated on <c>platform:defaults:manage</c>.
/// What every channel that never saved its own response does when an event fires: list, preview the counted
/// blast radius of a change, then save (echoing the previewed count; refused as stale when it moved).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/platform-defaults/event-responses")]
[PlatformPlane]
[Authorize(Policy = IamPermissionKeys.PlatformDefaultsManage)]
[Tags("Platform Defaults")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class EventResponseDefaultsAdminController(
    IEventResponseDefaultsAdminService defaults,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>Every event type with its platform default and how many channels follow it.</summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<EventResponseDefaultDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> List(CancellationToken ct) =>
        ResultResponse(await defaults.ListAsync(ct));

    /// <summary>The counted blast radius of applying a proposed default to one event type.</summary>
    [HttpPost("{eventType}/blast-radius")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<PlatformDefaultBlastRadiusDto>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> Preview(
        string eventType,
        [FromBody] EventResponseDefaultChange change,
        CancellationToken ct
    ) => ResultResponse(await defaults.PreviewAsync(eventType, change, ct));

    /// <summary>Saves an event type's platform default; returns the saved default.</summary>
    [HttpPut("{eventType}")]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<EventResponseDefaultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        string eventType,
        [FromBody] SetEventResponseDefaultRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(currentUser.UserId, out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await defaults.SetAsync(eventType, request, caller, ct));
    }
}
