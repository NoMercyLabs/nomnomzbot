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
    ICurrentUserService currentUser,
    IEventBus eventBus
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
        Result<EventResponseDefaultDto> saved = await defaults.SetAsync(
            eventType,
            request,
            caller,
            ct
        );
        if (saved.IsSuccess)
            await eventBus.PublishAsync(
                new PlatformDefaultsChangedEvent { Set = "event-responses", Key = eventType },
                ct
            );
        return ResultResponse(saved);
    }
}
