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
/// The platform default TTS voice (plan item A4) — Plane C, gated on <c>platform:defaults:manage</c>. The voice
/// every channel that never picked its own speaks with: read it, preview the counted blast radius of a change,
/// then save (echoing the previewed count; refused as stale when it moved).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/platform-defaults/tts-voice")]
[PlatformPlane]
[Authorize(Policy = IamPermissionKeys.PlatformDefaultsManage)]
[Tags("Platform Defaults")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class TtsVoiceDefaultsAdminController(
    ITtsVoiceDefaultsAdminService defaults,
    ICurrentUserService currentUser,
    IEventBus eventBus
) : BaseController
{
    /// <summary>The platform default voice and how many channels follow it.</summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<TtsVoiceDefaultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        ResultResponse(await defaults.GetAsync(ct));

    /// <summary>The voices the platform default may be set to.</summary>
    [HttpGet("candidates")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TtsVoiceCandidateDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> Candidates(CancellationToken ct) =>
        ResultResponse(await defaults.CandidatesAsync(ct));

    /// <summary>The counted blast radius of making a voice the platform default.</summary>
    [HttpPost("blast-radius")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<PlatformDefaultBlastRadiusDto>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> Preview(
        [FromBody] TtsVoiceDefaultChange change,
        CancellationToken ct
    ) => ResultResponse(await defaults.PreviewAsync(change, ct));

    /// <summary>Makes a voice the platform default; returns the default read back.</summary>
    [HttpPut]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<TtsVoiceDefaultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        [FromBody] SetTtsVoiceDefaultRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(currentUser.UserId, out Guid caller))
            return UnauthenticatedResponse();
        Result<TtsVoiceDefaultDto> saved = await defaults.SetAsync(request, caller, ct);
        if (saved.IsSuccess)
            await eventBus.PublishAsync(
                new PlatformDefaultsChangedEvent { Set = "tts-voice", Key = null },
                ct
            );
        return ResultResponse(saved);
    }
}
