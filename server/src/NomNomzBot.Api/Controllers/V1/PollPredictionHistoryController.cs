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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Community.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Every poll and prediction the channel ever ran, with its final result.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/live-ops/history")]
[Authorize]
[Tags("LiveOps")]
public class PollPredictionHistoryController : BaseController
{
    private readonly IPollPredictionHistoryService _history;

    public PollPredictionHistoryController(IPollPredictionHistoryService history)
    {
        _history = history;
    }

    /// <summary>The channel's polls and predictions from the event journal, newest first.</summary>
    [RequireAction("live-ops:polls:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<PollPredictionHistoryItemDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetHistory(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<IReadOnlyList<PollPredictionHistoryItemDto>> result = await _history.ListAsync(
            broadcasterId,
            ct
        );
        return result.IsFailure
            ? ResultResponse(result)
            : Ok(
                new StatusResponseDto<IReadOnlyList<PollPredictionHistoryItemDto>>
                {
                    Data = result.Value,
                }
            );
    }
}
