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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Voice triggers: spoken-word counters + overlay stickers. A streamer's voice-listener page (a real Chrome
/// tab running <c>webkitSpeechRecognition</c> — see <c>VoiceListenerPageController</c>) reports a heard word
/// through the separate anonymous <c>VoiceTriggerReportController</c>; this controller is the authenticated
/// management surface (list/create/update/delete), gated the same floor as <c>ChatTriggersController</c>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/voice-triggers")]
[Authorize]
[Tags("VoiceTriggers")]
public class VoiceTriggersController : BaseController
{
    private readonly IVoiceTriggerService _triggers;
    private readonly IConfiguration _configuration;

    public VoiceTriggersController(IVoiceTriggerService triggers, IConfiguration configuration)
    {
        _triggers = triggers;
        _configuration = configuration;
    }

    /// <summary>
    /// The streamer's voice-listener page link, ready to open in a real Chrome tab — same origin-resolution as
    /// every OBS overlay URL (<see cref="Api.Extensions.PublicOriginExtensions.ResolvePublicOrigin"/>), so a
    /// copied link works from wherever the operator actually reached the dashboard.
    /// </summary>
    [RequireAction("voicetriggers:read")]
    [HttpGet("listener-link")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetListenerLink(string channelId, CancellationToken ct)
    {
        Result<string> token = await _triggers.GetOverlayTokenAsync(channelId, ct);
        if (token.IsFailure)
            return ResultResponse(token);

        string origin = Request.ResolvePublicOrigin(_configuration);
        string url = $"{origin}/voice-listener?token={Uri.EscapeDataString(token.Value)}";
        return Ok(new StatusResponseDto<string> { Data = url });
    }

    /// <summary>List the channel's voice triggers, with their live counts.</summary>
    [RequireAction("voicetriggers:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<VoiceTriggerDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListTriggers(string channelId, CancellationToken ct)
    {
        Result<IReadOnlyList<VoiceTriggerDto>> result = await _triggers.ListAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<IReadOnlyList<VoiceTriggerDto>> { Data = result.Value });
    }

    /// <summary>Create a voice trigger. <c>StartingCount</c> seeds the live count once, at creation.</summary>
    [RequireAction("voicetriggers:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<VoiceTriggerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTrigger(
        string channelId,
        [FromBody] CreateVoiceTriggerRequest request,
        CancellationToken ct
    ) => ResultResponse(await _triggers.CreateAsync(channelId, request, ct));

    /// <summary>Update a voice trigger (partial — absent fields stay unchanged).</summary>
    [RequireAction("voicetriggers:write")]
    [HttpPatch("{triggerId:guid}")]
    [ProducesResponseType<StatusResponseDto<VoiceTriggerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTrigger(
        string channelId,
        Guid triggerId,
        [FromBody] UpdateVoiceTriggerRequest request,
        CancellationToken ct
    ) => ResultResponse(await _triggers.UpdateAsync(channelId, triggerId, request, ct));

    /// <summary>Delete a voice trigger.</summary>
    [RequireAction("voicetriggers:write")]
    [NotDestructive(
        "Deletes one VoiceTrigger row; the sticker it referenced (ChannelAsset) is untouched, and no other entity carries a VoiceTriggerId FK."
    )]
    [HttpDelete("{triggerId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTrigger(
        string channelId,
        Guid triggerId,
        CancellationToken ct
    )
    {
        Result result = await _triggers.DeleteAsync(channelId, triggerId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }
}
