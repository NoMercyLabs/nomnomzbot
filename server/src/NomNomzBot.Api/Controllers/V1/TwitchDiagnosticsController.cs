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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Twitch connection diagnostics (twitch-helix.md §5). One read endpoint surfacing the channel's scope/
/// connection health so a missing scope is observable instead of silently degrading a feature.
/// <para>
/// Gate 1 is <c>[Authorize]</c> + tenant resolution from the JWT (<see cref="ICurrentTenantService"/>) — the
/// diagnostics are inherently "my own channel". The per-route Gate-2 floor (<c>twitch:diagnostics:read</c>)
/// and its seeded <c>ActionDefinitions</c> row are DEFERRED to the roles-permissions subsystem
/// (<c>IActionAuthorizationService</c>), consistent with every other controller — it is not built yet, so
/// self-host collapses to "owner = full".
/// </para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/twitch/diagnostics")]
[Authorize]
[Tags("Twitch Diagnostics")]
public class TwitchDiagnosticsController : BaseController
{
    private readonly ITwitchScopeDiagnosticsService _diagnostics;
    private readonly ICurrentTenantService _tenant;

    public TwitchDiagnosticsController(
        ITwitchScopeDiagnosticsService diagnostics,
        ICurrentTenantService tenant
    )
    {
        _diagnostics = diagnostics;
        _tenant = tenant;
    }

    /// <summary>
    /// The current channel's Twitch scope/connection matrix: connection status, granted scopes, and the
    /// per-feature granted/missing requirement rows. <c>404</c> when this channel has no Twitch connection.
    /// </summary>
    [HttpGet("scopes")]
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
}
