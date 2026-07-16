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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Keyword chat triggers ("someone says X → the bot reacts"): auto-replies on ordinary chat lines —
/// contains / exact / starts-with / regex matching, template response or a bound pipeline for chained
/// reactions, per-trigger cooldown and role floor.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/chat-triggers")]
[Authorize]
[Tags("ChatTriggers")]
public class ChatTriggersController : BaseController
{
    private readonly IChatTriggerService _triggers;

    public ChatTriggersController(IChatTriggerService triggers)
    {
        _triggers = triggers;
    }

    /// <summary>List the channel's keyword triggers.</summary>
    [RequireAction("chattriggers:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ChatTriggerDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListTriggers(string channelId, CancellationToken ct)
    {
        Result<IReadOnlyList<ChatTriggerDto>> result = await _triggers.ListAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<IReadOnlyList<ChatTriggerDto>> { Data = result.Value });
    }

    /// <summary>Create a keyword trigger (a regex pattern is compile-checked here).</summary>
    [RequireAction("chattriggers:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<ChatTriggerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTrigger(
        string channelId,
        [FromBody] CreateChatTriggerRequest request,
        CancellationToken ct
    ) => ResultResponse(await _triggers.CreateAsync(channelId, request, ct));

    /// <summary>Update a keyword trigger (partial — absent fields stay unchanged).</summary>
    [RequireAction("chattriggers:write")]
    [HttpPatch("{triggerId:guid}")]
    [ProducesResponseType<StatusResponseDto<ChatTriggerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTrigger(
        string channelId,
        Guid triggerId,
        [FromBody] UpdateChatTriggerRequest request,
        CancellationToken ct
    ) => ResultResponse(await _triggers.UpdateAsync(channelId, triggerId, request, ct));

    /// <summary>Delete a keyword trigger.</summary>
    [RequireAction("chattriggers:write")]
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
