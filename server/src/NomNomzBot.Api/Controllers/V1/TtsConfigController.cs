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
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages a channel's text-to-speech provider, voice, and playback settings, for the dashboard operator configuring chat-triggered TTS.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/tts")]
[Authorize]
[Tags("TTS")]
public class TtsConfigController : BaseController
{
    private readonly ITtsConfigService _ttsConfigService;

    public TtsConfigController(ITtsConfigService ttsConfigService)
    {
        _ttsConfigService = ttsConfigService;
    }

    /// <summary>Get the channel's TTS configuration.</summary>
    [HttpGet("config")]
    [RequireAction("tts:config:read")]
    [ProducesResponseType<StatusResponseDto<TtsConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfig(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsConfigDto> result = await _ttsConfigService.GetConfigAsync(broadcasterId, ct);
        return ResultResponse(result);
    }

    /// <summary>Update the channel's TTS configuration.</summary>
    [HttpPut("config")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<TtsConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig(
        string channelId,
        [FromBody] UpdateTtsConfigDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsConfigDto> result = await _ttsConfigService.UpdateConfigAsync(
            broadcasterId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TtsConfigDto> { Data = result.Value });
    }

    /// <summary>List the voices available from the configured TTS provider.</summary>
    [HttpGet("voices")]
    [RequireAction("tts:voice:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TtsVoiceDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVoices(string channelId, CancellationToken ct)
    {
        Result<IReadOnlyList<TtsVoiceDto>> result = await _ttsConfigService.GetVoicesAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>Generate a short test TTS clip to preview a voice.</summary>
    [HttpPost("test")]
    [RequireAction("tts:voice:test")]
    [ProducesResponseType<StatusResponseDto<TtsTestResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestVoice(
        string channelId,
        [FromBody] TtsTestRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsTestResultDto> result = await _ttsConfigService.TestVoiceAsync(
            broadcasterId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TtsTestResultDto> { Data = result.Value });
    }

    /// <summary>Get a viewer's assigned TTS voice (404 when they use the channel default).</summary>
    [HttpGet("users/{userId}/voice")]
    [RequireAction("tts:voice:read")]
    [ProducesResponseType<StatusResponseDto<UserTtsVoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserVoice(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<UserTtsVoiceDto> result = await _ttsConfigService.GetUserVoiceAsync(
            broadcasterId,
            userId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Assign a specific TTS voice to a viewer.</summary>
    [HttpPut("users/{userId}/voice")]
    [RequireAction("tts:uservoice:write")]
    [ProducesResponseType<StatusResponseDto<UserTtsVoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetUserVoice(
        string channelId,
        string userId,
        [FromBody] SetUserVoiceDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<UserTtsVoiceDto> result = await _ttsConfigService.SetUserVoiceAsync(
            broadcasterId,
            userId,
            request,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Remove a viewer's voice assignment so they fall back to the channel default.</summary>
    [HttpDelete("users/{userId}/voice")]
    [RequireAction("tts:uservoice:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearUserVoice(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result result = await _ttsConfigService.ClearUserVoiceAsync(broadcasterId, userId, ct);
        return ResultResponse(result);
    }
}
