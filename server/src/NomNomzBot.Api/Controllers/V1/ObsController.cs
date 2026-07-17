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
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// OBS control — configuration surface (obs-control.md §7, channel-routed as built like every other
/// management controller). The OBS-WS password is write-only (sealed at rest, never echoed); the
/// bridge credential is only ever surfaced inside the setup URL. Live state/control routes arrive
/// with the transport slice.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/obs")]
[Authorize]
[Tags("OBS")]
public class ObsController(IObsConnectionService connections, IConfiguration configuration)
    : BaseController
{
    /// <summary>The channel's OBS connection configuration (defaults when none is stored yet).</summary>
    [HttpGet("connection")]
    [RequireAction("obs:config:read")]
    [ProducesResponseType<StatusResponseDto<ObsConnectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnection(Guid channelId, CancellationToken ct) =>
        ResultResponse(await connections.GetAsync(channelId, ct));

    /// <summary>Create-or-update the OBS connection; the password field is write-only.</summary>
    [HttpPut("connection")]
    [RequireAction("obs:config:write")]
    [ProducesResponseType<StatusResponseDto<ObsConnectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertConnection(
        Guid channelId,
        [FromBody] UpsertObsConnectionRequest request,
        CancellationToken ct
    ) => ResultResponse(await connections.UpsertAsync(channelId, request, ct));

    /// <summary>The browser-source bridge install URL (mints the bridge credential on first ask).</summary>
    [HttpGet("bridge/setup")]
    [RequireAction("obs:config:write")]
    [ProducesResponseType<StatusResponseDto<ObsBridgeSetupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBridgeSetup(Guid channelId, CancellationToken ct) =>
        ResultResponse(
            await connections.GetBridgeSetupAsync(
                channelId,
                Request.ResolvePublicOrigin(configuration),
                ct
            )
        );

    /// <summary>Rotate the bridge credential; the previous setup URL stops authenticating immediately.</summary>
    [HttpPost("bridge/rotate-token")]
    [RequireAction("obs:config:write")]
    [ProducesResponseType<StatusResponseDto<ObsBridgeSetupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateBridgeToken(Guid channelId, CancellationToken ct) =>
        ResultResponse(
            await connections.RotateBridgeTokenAsync(
                channelId,
                Request.ResolvePublicOrigin(configuration),
                ct
            )
        );
}
