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

    [HttpGet("config")]
    [RequireAction("tts:config:read")]
    [ProducesResponseType<StatusResponseDto<TtsConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfig(string channelId, CancellationToken ct)
    {
        Result<TtsConfigDto> result = await _ttsConfigService.GetConfigAsync(channelId, ct);
        return ResultResponse(result);
    }

    [HttpPut("config")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<TtsConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig(
        string channelId,
        [FromBody] UpdateTtsConfigDto request,
        CancellationToken ct
    )
    {
        Result<TtsConfigDto> result = await _ttsConfigService.UpdateConfigAsync(
            channelId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TtsConfigDto> { Data = result.Value });
    }

    [HttpGet("voices")]
    [RequireAction("tts:voice:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TtsVoiceDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVoices(string channelId, CancellationToken ct)
    {
        Result<IReadOnlyList<TtsVoiceDto>> result = await _ttsConfigService.GetVoicesAsync(ct);
        return ResultResponse(result);
    }

    [HttpPost("test")]
    [RequireAction("tts:voice:test")]
    [ProducesResponseType<StatusResponseDto<TtsTestResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestVoice(
        string channelId,
        [FromBody] TtsTestRequestDto request,
        CancellationToken ct
    )
    {
        Result<TtsTestResultDto> result = await _ttsConfigService.TestVoiceAsync(
            channelId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TtsTestResultDto> { Data = result.Value });
    }
}
