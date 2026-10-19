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

/// <summary>Reads what a channel's text-to-speech has spoken, for the dashboard operator reviewing the TTS history.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/tts/history")]
[Authorize]
[Tags("TTS")]
public class TtsHistoryController(ITtsHistoryService history) : BaseController
{
    /// <summary>
    /// The spoken history, newest first. <c>limit</c> (default 50, at most 100) sizes the page; <c>before</c> is the
    /// <c>nextBefore</c> of the previous page and returns only older rows.
    /// </summary>
    [HttpGet]
    [RequireAction("tts:config:read")]
    [ProducesResponseType<StatusResponseDto<TtsHistoryPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        string channelId,
        [FromQuery] int limit = 50,
        [FromQuery] int? before = null,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TtsHistoryPageDto> result = await history.ListAsync(
            broadcasterId,
            limit,
            before,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TtsHistoryPageDto> { Data = result.Value });
    }
}
