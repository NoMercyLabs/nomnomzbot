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
using NomNomzBot.Application.Contracts.Tts;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The moderator approval queue for chat-triggered TTS (tts.md P.1a). When a channel runs with mod approval on,
/// utterances land here instead of playing; a moderator lists the pending queue and approves (it is then synthesized
/// and played) or rejects (it is discarded). All actions gate on <c>tts:queue:review</c>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/tts/queue")]
[Authorize]
[Tags("TTS")]
public class TtsQueueController : BaseController
{
    private readonly ITtsDispatchService _dispatch;
    private readonly ICurrentUserService _currentUser;

    public TtsQueueController(ITtsDispatchService dispatch, ICurrentUserService currentUser)
    {
        _dispatch = dispatch;
        _currentUser = currentUser;
    }

    /// <summary>List the channel's pending TTS utterances awaiting moderator approval, newest-first.</summary>
    [HttpGet]
    [RequireAction("tts:queue:review")]
    [ProducesResponseType<PaginatedResponse<TtsQueueEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(
        string channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("A valid channel id is required.");

        Result<PagedList<TtsQueueEntryDto>> result = await _dispatch.GetPendingQueueAsync(
            broadcasterId,
            page,
            pageSize,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        PagedList<TtsQueueEntryDto> paged = result.Value;
        return Ok(
            new PaginatedResponse<TtsQueueEntryDto>
            {
                Data = paged.Items,
                NextPage = paged.HasNextPage ? paged.Page + 1 : null,
                HasMore = paged.HasNextPage,
            }
        );
    }

    /// <summary>Approve a pending utterance — it is synthesized and played on the overlay.</summary>
    [HttpPost("{entryId:guid}/approve")]
    [RequireAction("tts:queue:review")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(string channelId, Guid entryId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("A valid channel id is required.");
        if (!Guid.TryParse(_currentUser.UserId, out Guid reviewerId))
            return UnauthenticatedResponse();

        Result result = await _dispatch.ApproveAsync(broadcasterId, entryId, reviewerId, ct);
        return ResultResponse(result);
    }

    /// <summary>Reject a pending utterance — it is discarded; nothing is spoken.</summary>
    [HttpPost("{entryId:guid}/reject")]
    [RequireAction("tts:queue:review")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(string channelId, Guid entryId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("A valid channel id is required.");
        if (!Guid.TryParse(_currentUser.UserId, out Guid reviewerId))
            return UnauthenticatedResponse();

        Result result = await _dispatch.RejectAsync(broadcasterId, entryId, reviewerId, ct);
        return ResultResponse(result);
    }
}
