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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Twitch connection diagnostics (twitch-helix.md §5). One read endpoint surfacing the channel's scope/
/// connection health so a missing scope is observable instead of silently degrading a feature.
/// <para>
/// Gate 1 is <c>[Authorize]</c> + tenant resolution from the JWT (<see cref="ICurrentTenantService"/>) — the
/// diagnostics are inherently "my own channel". Gate 2 is the per-route <c>[RequireAction]</c> floor
/// (<c>twitch:diagnostics:read</c>), enforced by <c>IActionAuthorizationService</c>. Self-host collapses to
/// "owner = full".
/// </para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/twitch/diagnostics")]
[Authorize]
[Tags("Twitch Diagnostics")]
public class TwitchDiagnosticsController : BaseController
{
    private readonly ITwitchScopeDiagnosticsService _diagnostics;
    private readonly IScopeNotificationService _scopeNotifications;
    private readonly IAuthService _authService;
    private readonly ICurrentTenantService _tenant;

    public TwitchDiagnosticsController(
        ITwitchScopeDiagnosticsService diagnostics,
        IScopeNotificationService scopeNotifications,
        IAuthService authService,
        ICurrentTenantService tenant
    )
    {
        _diagnostics = diagnostics;
        _scopeNotifications = scopeNotifications;
        _authService = authService;
        _tenant = tenant;
    }

    /// <summary>
    /// The current channel's Twitch scope/connection matrix: connection status, granted scopes, and the
    /// per-feature granted/missing requirement rows. <c>404</c> when this channel has no Twitch connection.
    /// </summary>
    [HttpGet("scopes")]
    [RequireAction("twitch:diagnostics:read")]
    [ProducesResponseType<StatusResponseDto<TwitchScopeDiagnosticsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScopeDiagnostics(CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<TwitchScopeDiagnosticsDto> result = await _diagnostics.GetScopeDiagnosticsAsync(
            broadcasterId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// The channel's outstanding Twitch scope gaps — the deduplicated set of scopes the streamer token is missing
    /// (proactive feature-gated gaps ∪ reactive gaps a real Helix call surfaced), each with the feature(s) it
    /// blocks and whether the streamer was already told in chat. The dashboard renders the "grant '&lt;scope&gt;'"
    /// banner from this. <c>404</c> when this channel has no Twitch connection.
    /// </summary>
    [HttpGet("missing-scopes")]
    [RequireAction("twitch:diagnostics:read")]
    [ProducesResponseType<StatusResponseDto<MissingScopesDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMissingScopes(CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<MissingScopesDto> result = await _scopeNotifications.GetMissingScopesAsync(
            broadcasterId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Start the one-click additive scope re-grant: a secret-free streamer Device Code Flow requesting
    /// <c>granted ∪ missing</c>, so the operator re-consents to the full set and the existing grant is never
    /// dropped. The client shows the user code + verification URL and polls the normal streamer device poll
    /// (<c>POST /auth/twitch/device/poll</c>); on approval the widened grant reconciles onto the connection and the
    /// gaps clear. <c>404</c> when there is no Twitch connection; <c>409</c> when nothing is missing.
    /// </summary>
    [HttpPost("regrant")]
    [RequireAction("twitch:diagnostics:read")]
    [ProducesResponseType<StatusResponseDto<ScopeRegrantStartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartScopeRegrant(CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<IReadOnlyList<string>> scopeSet =
            await _scopeNotifications.BuildRegrantScopeSetAsync(broadcasterId, ct);
        if (scopeSet.IsFailure)
            return ResultResponse(scopeSet);

        Result<DeviceCodeStartDto> device = await _authService.StartTwitchDeviceLoginForScopesAsync(
            scopeSet.Value,
            ct
        );
        if (device.IsFailure)
            return ResultResponse(device);

        DeviceCodeStartDto code = device.Value;
        return Ok(
            new StatusResponseDto<ScopeRegrantStartDto>
            {
                Data = new ScopeRegrantStartDto(
                    code.DeviceCode,
                    code.UserCode,
                    code.VerificationUri,
                    code.Interval,
                    code.ExpiresIn,
                    scopeSet.Value
                ),
            }
        );
    }
}
