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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Per-channel custom chat filters (moderation.md J.6) — the regex / blocklist / link-policy rules the bot runs
/// against every incoming message, applying each filter's action (delete / timeout / hold / flag / escalate) on
/// the hot path via the chat-filter execution handler. This controller is the streamer's management surface for
/// that catalogue: list, get, create, update, and delete filters. Gated with the shared moderation filter action
/// keys (<c>moderation:filter:read</c> / <c>moderation:filter:write</c>) — the same keys the auto-mod rules CRUD
/// uses, since a chat filter is itself a moderation filter (no chat-filter-specific action key exists).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/moderation/chat-filters")]
[Authorize]
[Tags("Moderation")]
public class ChatFiltersController : BaseController
{
    private readonly IChatFilterService _chatFilters;

    public ChatFiltersController(IChatFilterService chatFilters)
    {
        _chatFilters = chatFilters;
    }

    /// <summary>List the channel's chat filters, newest first, paginated.</summary>
    [RequireAction("moderation:filter:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<ChatFilterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFilters(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return BadRequestResponse("Invalid channel id.");

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<ChatFilterDto>> result = await _chatFilters.ListAsync(
            broadcaster,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Get a single chat filter by id.</summary>
    [RequireAction("moderation:filter:read")]
    [HttpGet("{filterId:guid}")]
    [ProducesResponseType<StatusResponseDto<ChatFilterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilter(
        string channelId,
        Guid filterId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return BadRequestResponse("Invalid channel id.");

        return ResultResponse(await _chatFilters.GetAsync(broadcaster, filterId, ct));
    }

    /// <summary>Create a new chat filter for the channel.</summary>
    [RequireAction("moderation:filter:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<ChatFilterDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateFilter(
        string channelId,
        [FromBody] CreateChatFilterRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return BadRequestResponse("Invalid channel id.");

        Result<ChatFilterDto> result = await _chatFilters.CreateAsync(broadcaster, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetFilter),
            new { channelId, filterId = result.Value.Id },
            new StatusResponseDto<ChatFilterDto>
            {
                Data = result.Value,
                Message = "Chat filter created successfully.",
            }
        );
    }

    /// <summary>Update an existing chat filter — only the supplied fields change.</summary>
    [RequireAction("moderation:filter:write")]
    [HttpPut("{filterId:guid}")]
    [ProducesResponseType<StatusResponseDto<ChatFilterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateFilter(
        string channelId,
        Guid filterId,
        [FromBody] UpdateChatFilterRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return BadRequestResponse("Invalid channel id.");

        return ResultResponse(await _chatFilters.UpdateAsync(broadcaster, filterId, request, ct));
    }

    /// <summary>Delete (soft) a chat filter.</summary>
    [RequireAction("moderation:filter:write")]
    [HttpDelete("{filterId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteFilter(
        string channelId,
        Guid filterId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _chatFilters.DeleteAsync(broadcaster, filterId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }
}
