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
using NomNomzBot.Application.Sound.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>The channel's master and TTS volume, applied by the bot so the balance is the same on every streaming PC.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/audio-mix")]
[Authorize]
[Tags("Sound")]
public sealed class AudioMixController : BaseController
{
    private readonly IChannelAudioMixService _service;
    private readonly ICurrentTenantService _currentTenant;

    public AudioMixController(IChannelAudioMixService service, ICurrentTenantService currentTenant)
    {
        _service = service;
        _currentTenant = currentTenant;
    }

    /// <summary>Read the channel's audio mix (100 and 100 until it is changed).</summary>
    [HttpGet]
    [RequireAction("sounds:read")]
    [ProducesResponseType<StatusResponseDto<ChannelAudioMixDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Guid broadcasterId = _currentTenant.BroadcasterId ?? Guid.Empty;
        if (broadcasterId == Guid.Empty)
            return Unauthorized();

        Result<ChannelAudioMixDto> result = await _service.GetAsync(broadcasterId, ct);
        return ToResponse(result);
    }

    /// <summary>Set the channel's master and TTS volume (0 to 100 each).</summary>
    [HttpPut]
    [RequireAction("sounds:write")]
    [ProducesResponseType<StatusResponseDto<ChannelAudioMixDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateChannelAudioMixRequest request,
        CancellationToken ct
    )
    {
        Guid broadcasterId = _currentTenant.BroadcasterId ?? Guid.Empty;
        if (broadcasterId == Guid.Empty)
            return Unauthorized();

        Result<ChannelAudioMixDto> result = await _service.UpdateAsync(broadcasterId, request, ct);
        return ToResponse(result);
    }

    private IActionResult ToResponse(Result<ChannelAudioMixDto> result) =>
        result.IsSuccess
            ? Ok(new StatusResponseDto<ChannelAudioMixDto> { Data = result.Value })
            : BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });
}
