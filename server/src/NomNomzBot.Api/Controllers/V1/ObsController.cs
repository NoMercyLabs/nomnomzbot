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
public class ObsController(
    IObsConnectionService connections,
    IObsControlService control,
    IObsBridgeRegistry bridges,
    IConfiguration configuration
) : BaseController
{
    /// <summary>Live bridge fleet status (instances online, whether a leader executes).</summary>
    [HttpGet("bridge/status")]
    [RequireAction("obs:config:read")]
    [ProducesResponseType<StatusResponseDto<ObsBridgeStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBridgeStatus(Guid channelId, CancellationToken ct) =>
        Ok(
            new StatusResponseDto<ObsBridgeStatusDto>
            {
                Data = await bridges.GetStatusAsync(channelId, ct),
            }
        );

    /// <summary>Live OBS state (current scene, stream/record/replay status).</summary>
    [HttpGet("state")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<ObsStateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetState(Guid channelId, CancellationToken ct) =>
        ResultResponse(await control.GetStateAsync(channelId, ct));

    /// <summary>The scene list (current one flagged).</summary>
    [HttpGet("scenes")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ObsSceneDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScenes(Guid channelId, CancellationToken ct) =>
        ResultResponse(await control.GetScenesAsync(channelId, ct));

    /// <summary>The input list.</summary>
    [HttpGet("inputs")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ObsInputDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInputs(Guid channelId, CancellationToken ct) =>
        ResultResponse(await control.GetInputsAsync(channelId, ct));

    /// <summary>Switch the program scene.</summary>
    [HttpPost("scene")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SwitchScene(
        Guid channelId,
        [FromBody] ObsSceneRequest request,
        CancellationToken ct
    ) => ResultResponse(await control.SwitchSceneAsync(channelId, request.Scene, ct));

    /// <summary>Streaming start/stop/toggle (broadcast-impacting).</summary>
    [HttpPost("streaming")]
    [RequireAction("obs:control:broadcast")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStreaming(
        Guid channelId,
        [FromBody] ObsToggleRequest request,
        CancellationToken ct
    ) => ResultResponse(await control.SetStreamingAsync(channelId, request.Action, ct));

    /// <summary>Recording control (broadcast-impacting).</summary>
    [HttpPost("recording")]
    [RequireAction("obs:control:broadcast")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRecording(
        Guid channelId,
        [FromBody] ObsRecordRequest request,
        CancellationToken ct
    ) => ResultResponse(await control.SetRecordingAsync(channelId, request.Action, ct));

    /// <summary>Raw OBS-WS pass-through (the full surface; broadcast-tier).</summary>
    [HttpPost("request")]
    [RequireAction("obs:control:broadcast")]
    [ProducesResponseType<StatusResponseDto<ObsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RawRequest(
        Guid channelId,
        [FromBody] ObsRequest request,
        CancellationToken ct
    ) => ResultResponse(await control.RequestAsync(channelId, request, ct));

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
