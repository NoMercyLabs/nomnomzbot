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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Application.Vts.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// VTube Studio — configuration surface (vtube-studio.md §5, channel-routed as built like every
/// other management controller). The plugin token never appears in any response; the interactive
/// authorize flow (which mints it) and live control arrive with the transport slice.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/vts")]
[Authorize]
[Tags("VTube Studio")]
public class VtsController(
    IVtsConnectionService connections,
    IVtsPluginAuthorizer authorizer,
    IVtsControlService control
) : BaseController
{
    /// <summary>
    /// Run the one-time plugin approval: VTube Studio pops its Allow dialog and this call waits for
    /// the streamer's click (up to ~60s); a granted token is sealed onto the connection.
    /// </summary>
    [HttpPost("connection/authorize")]
    [RequireAction("vts:config:write")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Authorize(Guid channelId, CancellationToken ct)
    {
        Result result = await authorizer.AuthorizeAsync(channelId, ct);
        return ResultResponse(result);
    }

    /// <summary>Models + current-model hotkeys + expressions, for the editor pickers.</summary>
    [HttpGet("inventory")]
    [RequireAction("vts:config:read")]
    [ProducesResponseType<StatusResponseDto<VtsModelInventory>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInventory(Guid channelId, CancellationToken ct) =>
        ResultResponse(await control.GetInventoryAsync(channelId, ct));

    /// <summary>Raw VTS API pass-through (the full surface).</summary>
    [HttpPost("control")]
    [RequireAction("vts:control")]
    [ProducesResponseType<StatusResponseDto<VtsRequestResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Control(
        Guid channelId,
        [FromBody] VtsControlRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await control.SendAsync(channelId, request.RequestType, request.PayloadJson, ct)
        );

    /// <summary>The channel's VTS connection configuration (defaults when none is stored yet).</summary>
    [HttpGet("connection")]
    [RequireAction("vts:config:read")]
    [ProducesResponseType<StatusResponseDto<VtsConnectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnection(Guid channelId, CancellationToken ct) =>
        ResultResponse(await connections.GetAsync(channelId, ct));

    /// <summary>Create-or-update the VTS connection (mode/endpoint/mask/enabled; the token is untouched).</summary>
    [HttpPut("connection")]
    [RequireAction("vts:config:write")]
    [ProducesResponseType<StatusResponseDto<VtsConnectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertConnection(
        Guid channelId,
        [FromBody] UpsertVtsConnectionRequest request,
        CancellationToken ct
    ) => ResultResponse(await connections.UpsertAsync(channelId, request, ct));

    /// <summary>Rotate the bridge credential; the previous one stops authenticating immediately.</summary>
    [HttpPost("connection/rotate-bridge-token")]
    [RequireAction("vts:config:write")]
    [ProducesResponseType<StatusResponseDto<VtsConnectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateBridgeToken(Guid channelId, CancellationToken ct) =>
        ResultResponse(await connections.RotateBridgeTokenAsync(channelId, ct));
}
