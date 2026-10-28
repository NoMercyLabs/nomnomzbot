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

/// <summary>
/// Bot-run chat polls: viewers vote by typing the option number in chat, on EVERY platform (no
/// affiliate gate) — the custom counterpart to the Helix-native polls on the live-ops page. Live
/// tallies at read time; closing announces the result in chat.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/chat-polls")]
[Authorize]
[Tags("ChatPolls")]
public class ChatPollsController : BaseController
{
    private readonly IChatPollService _polls;

    public ChatPollsController(IChatPollService polls)
    {
        _polls = polls;
    }

    /// <summary>The channel's polls: the open one (live tallies) first, then recent history.</summary>
    [RequireAction("chatpolls:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ChatPollDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPolls(string channelId, CancellationToken ct)
    {
        Result<IReadOnlyList<ChatPollDto>> result = await _polls.ListAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<IReadOnlyList<ChatPollDto>> { Data = result.Value });
    }

    /// <summary>One poll with its live tallies.</summary>
    [RequireAction("chatpolls:read")]
    [HttpGet("{pollId:guid}")]
    [ProducesResponseType<StatusResponseDto<ChatPollDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPoll(string channelId, Guid pollId, CancellationToken ct) =>
        ResultResponse(await _polls.GetAsync(channelId, pollId, ct));

    /// <summary>Open a poll (one per channel at a time; optionally announces it in chat).</summary>
    [RequireAction("chatpolls:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<ChatPollDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenPoll(
        string channelId,
        [FromBody] OpenChatPollRequest request,
        CancellationToken ct
    ) => ResultResponse(await _polls.OpenAsync(channelId, request, ct));

    /// <summary>Close the poll now — announces the winner in chat and keeps the poll as history.</summary>
    [RequireAction("chatpolls:write")]
    [HttpPost("{pollId:guid}/close")]
    [ProducesResponseType<StatusResponseDto<ChatPollDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClosePoll(
        string channelId,
        Guid pollId,
        CancellationToken ct
    ) => ResultResponse(await _polls.CloseAsync(channelId, pollId, ct));
}
