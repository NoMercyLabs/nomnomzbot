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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
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
        ObsReadResponse(
            await control.GetStateAsync(channelId, ct),
            new(null, false, false, false, false, null)
        );

    /// <summary>The scene list (current one flagged).</summary>
    [HttpGet("scenes")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ObsSceneDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScenes(Guid channelId, CancellationToken ct) =>
        ObsReadResponse(await control.GetScenesAsync(channelId, ct), []);

    /// <summary>The input list.</summary>
    [HttpGet("inputs")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ObsInputDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInputs(Guid channelId, CancellationToken ct) =>
        ObsReadResponse(await control.GetInputsAsync(channelId, ct), []);

    /// <summary>The items placed in one scene, with their per-scene visibility — distinct from
    /// <c>GET inputs</c>, which only sees global audio/video inputs, never per-scene placement.</summary>
    [HttpGet("scene-items")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ObsSceneItemDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetSceneItems(
        Guid channelId,
        [FromQuery] string sceneName,
        CancellationToken ct
    ) => ObsReadResponse(await control.GetSceneItemListAsync(channelId, sceneName, ct), []);

    /// <summary>Whether the virtual camera output is currently running.</summary>
    [HttpGet("virtual-cam/status")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<ObsVirtualCamStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVirtualCamStatus(Guid channelId, CancellationToken ct) =>
        ObsReadResponse(await control.GetVirtualCamStatusAsync(channelId, ct), new(false));

    /// <summary>OBS performance stats — CPU/memory load and render/output frame counters.</summary>
    [HttpGet("stats")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<ObsStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(Guid channelId, CancellationToken ct) =>
        ObsReadResponse(await control.GetStatsAsync(channelId, ct), new(0, 0, 0, 0, 0, 0, 0));

    /// <summary>The scene transitions OBS knows about, with the currently active one flagged.</summary>
    [HttpGet("scene-transitions")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ObsTransitionDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetSceneTransitions(Guid channelId, CancellationToken ct) =>
        ObsReadResponse(await control.GetSceneTransitionListAsync(channelId, ct), []);

    /// <summary>The filters attached to one source (scene or input).</summary>
    [HttpGet("source-filters")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ObsFilterDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSourceFilters(
        Guid channelId,
        [FromQuery] string sourceName,
        CancellationToken ct
    ) => ObsReadResponse(await control.GetSourceFilterListAsync(channelId, sourceName, ct), []);

    /// <summary>
    /// Actively probe whether OBS is reachable RIGHT NOW. Unlike the passive state/scenes/inputs reads — which
    /// mask a "not connected yet" as an empty 200 so the page shows its connect prompt, not a 500 (so a 200 there
    /// is not proof of connectivity) — this ATTEMPTS the transport with a harmless <c>GetVersion</c> and reports
    /// the TRUTH: connected, or the real failing code, for the direct socket and the bridge leader alike. Gated at
    /// the Moderator control floor (not the Broadcaster config floor) so a control-only moderator can still tell
    /// whether OBS is live.
    /// </summary>
    [HttpGet("probe")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<ObsProbeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Probe(Guid channelId, CancellationToken ct)
    {
        Result<ObsResponse> result = await control.RequestAsync(
            channelId,
            new("GetVersion", null),
            ct
        );
        ObsProbeDto probe = result.IsSuccess
            ? new(Connected: true, ErrorCode: null, Error: null)
            : new ObsProbeDto(
                Connected: false,
                ErrorCode: result.ErrorCode,
                Error: result.ErrorMessage
            );
        return Ok(new StatusResponseDto<ObsProbeDto> { Data = probe });
    }

    // A read of OBS state when OBS simply is not reachable (disabled, no socket, no bridge leader) is not a
    // server error — it is the normal "not connected yet" state. Return an empty/disconnected payload at 200 so
    // the dashboard renders its connect prompt cleanly instead of a scary "Internal Server Error"; the
    // connection + bridge-status endpoints already tell the UI it is disconnected. Genuine failures still surface.
    private static readonly string[] ObsUnavailableCodes =
    [
        "OBS_DISABLED",
        "OBS_NOT_CONNECTED",
        "OBS_BRIDGE_OFFLINE",
        "OBS_WRONG_MODE",
    ];

    private IActionResult ObsReadResponse<T>(Result<T> result, T disconnectedFallback)
    {
        if (
            result is { IsFailure: true, ErrorCode: not null }
            && ObsUnavailableCodes.Contains(result.ErrorCode)
        )
            return Ok(new StatusResponseDto<T> { Data = disconnectedFallback });
        return ResultResponse(result);
    }

    /// <summary>Switch the program scene.</summary>
    [HttpPost("scene")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SwitchScene(
        Guid channelId,
        [FromBody] ObsSceneRequest request,
        CancellationToken ct
    ) => ResultResponse(await control.SwitchSceneAsync(channelId, request.Scene, ct));

    /// <summary>Audio mixer — set an input's mute state (obs-control.md §3.1).</summary>
    [HttpPost("inputs/mute")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetInputMute(
        Guid channelId,
        [FromBody] ObsInputMuteRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await control.SetInputMuteAsync(channelId, request.InputName, request.Muted, ct)
        );

    /// <summary>Audio mixer — set an input's volume in decibels (obs-control.md §3.1).</summary>
    [HttpPost("inputs/volume")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetInputVolume(
        Guid channelId,
        [FromBody] ObsInputVolumeRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await control.SetInputVolumeAsync(
                channelId,
                request.InputName,
                volumeDb: request.VolumeDb,
                volumeMul: null,
                ct
            )
        );

    /// <summary>Per-source visibility — hide/show one item within one scene, without touching the
    /// underlying input's mute state or its placement in any other scene.</summary>
    [HttpPost("scene-items/visibility")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetSourceVisibility(
        Guid channelId,
        [FromBody] ObsSourceVisibilityRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await control.SetSourceVisibleAsync(
                channelId,
                request.SceneName,
                request.SourceName,
                request.Visible,
                ct
            )
        );

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

    /// <summary>Replay buffer start/stop/toggle — cheap to invoke, but the action name matches this
    /// project's "expensive work" naming convention (S118), so it carries its own write-expensive tier
    /// rather than inheriting the controller default.</summary>
    [HttpPost("replay-buffer")]
    [RequireAction("obs:control")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetReplayBuffer(
        Guid channelId,
        [FromBody] ObsToggleRequest request,
        CancellationToken ct
    ) => ResultResponse(await control.SetReplayBufferAsync(channelId, request.Action, ct));

    /// <summary>Save the last replay-buffer clip to disk.</summary>
    [HttpPost("replay-buffer/save")]
    [RequireAction("obs:control")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveReplayBuffer(Guid channelId, CancellationToken ct) =>
        ResultResponse(await control.SaveReplayBufferAsync(channelId, ct));

    /// <summary>Virtual camera start/stop/toggle — a local output, not broadcast-impacting.</summary>
    [HttpPost("virtual-cam")]
    [RequireAction("obs:control")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetVirtualCam(
        Guid channelId,
        [FromBody] ObsToggleRequest request,
        CancellationToken ct
    ) => ResultResponse(await control.SetVirtualCamAsync(channelId, request.Action, ct));

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
