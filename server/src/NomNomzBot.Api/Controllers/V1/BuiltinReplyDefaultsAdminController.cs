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
/// Platform defaults for built-in reply texts (plan item A4) — Plane C, gated on <c>platform:defaults:manage</c>.
/// The wording of one built-in response slot for every channel: list, preview the counted blast radius of a
/// change, then save (echoing the previewed count; refused as stale when it moved). A null text restores the
/// shipped wording.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/platform-defaults/builtin-replies")]
[PlatformPlane]
[Authorize(Policy = IamPermissionKeys.PlatformDefaultsManage)]
[Tags("Platform Defaults")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class BuiltinReplyDefaultsAdminController(
    IBuiltinReplyDefaultsAdminService defaults,
    ICurrentUserService currentUser,
    IEventBus eventBus
) : BaseController
{
    /// <summary>Every editable built-in reply slot with its shipped and platform wording.</summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<BuiltinReplyDefaultDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> List(CancellationToken ct) =>
        ResultResponse(await defaults.ListAsync(ct));

    /// <summary>The counted blast radius of re-wording one built-in reply slot.</summary>
    [HttpPost("{builtinKey}/{slot}/blast-radius")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<PlatformDefaultBlastRadiusDto>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> Preview(
        string builtinKey,
        string slot,
        [FromBody] BuiltinReplyDefaultChange change,
        CancellationToken ct
    ) => ResultResponse(await defaults.PreviewAsync(builtinKey, slot, change, ct));

    /// <summary>Saves (or, with a null text, clears) a slot's platform wording; returns the saved slot.</summary>
    [HttpPut("{builtinKey}/{slot}")]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<BuiltinReplyDefaultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        string builtinKey,
        string slot,
        [FromBody] SetBuiltinReplyDefaultRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(currentUser.UserId, out Guid caller))
            return UnauthenticatedResponse();
        Result<BuiltinReplyDefaultDto> saved = await defaults.SetAsync(
            builtinKey,
            slot,
            request,
            caller,
            ct
        );
        if (saved.IsSuccess)
            await eventBus.PublishAsync(
                new PlatformDefaultsChangedEvent
                {
                    Set = "builtin-replies",
                    Key = $"{builtinKey}/{slot}",
                },
                ct
            );
        return ResultResponse(saved);
    }
}
