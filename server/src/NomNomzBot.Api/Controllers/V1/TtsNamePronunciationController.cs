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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>How TTS says a channel's own name, set by the streamer from the dashboard.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/tts/name-pronunciation")]
[Authorize]
[Tags("TTS")]
public class TtsNamePronunciationController : BaseController
{
    private readonly IChannelNamePronunciationService _service;

    public TtsNamePronunciationController(IChannelNamePronunciationService service)
    {
        _service = service;
    }

    /// <summary>The channel's name and how TTS says it.</summary>
    [HttpGet]
    [RequireAction("tts:config:read")]
    [ProducesResponseType<StatusResponseDto<ChannelNamePronunciationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid id))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await _service.GetAsync(id, ct));
    }

    /// <summary>Set how TTS says the channel's name. Empty clears it.</summary>
    [HttpPut]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<ChannelNamePronunciationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        string channelId,
        [FromBody] SetChannelNamePronunciationDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid id))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await _service.SetAsync(id, request.Pronunciation, ct));
    }
}
