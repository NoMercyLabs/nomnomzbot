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
/// Per-event-type automated response configuration (follow, subscribe, raid, and more) for a
/// channel — the dashboard operator's event responses page.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/event-responses")]
[Authorize]
[Tags("EventResponses")]
public class EventResponsesController : BaseController
{
    private readonly IEventResponseService _eventResponseService;

    public EventResponsesController(IEventResponseService eventResponseService)
    {
        _eventResponseService = eventResponseService;
    }

    /// <summary>List the channel's configured event responses, paginated, for the dashboard's event responses page.</summary>
    [RequireAction("eventresponses:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<EventResponseListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEventResponses(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<EventResponseListItem>> result = await _eventResponseService.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>
    /// The event-response preset catalog: every configurable event type with its ready-to-use default
    /// template and the exact template variables that event seeds — the dashboard pre-fills the message
    /// input from this instead of presenting an empty field.
    /// </summary>
    [RequireAction("eventresponses:read")]
    [HttpGet("catalog")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<EventResponsePresetDto>>>(
        StatusCodes.Status200OK
    )]
    public IActionResult GetCatalog() =>
        Ok(
            new StatusResponseDto<IReadOnlyList<EventResponsePresetDto>>
            {
                Data = EventResponsePresetCatalog.Presets,
            }
        );

    /// <summary>Get the configured response for a single event type.</summary>
    [RequireAction("eventresponses:read")]
    [HttpGet("{eventType}")]
    [ProducesResponseType<StatusResponseDto<EventResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEventResponse(
        string channelId,
        string eventType,
        CancellationToken ct
    )
    {
        Result<EventResponseDto> result = await _eventResponseService.GetByEventTypeAsync(
            channelId,
            eventType,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Create or replace the response configured for an event type.</summary>
    [RequireAction("eventresponses:write")]
    [HttpPut("{eventType}")]
    [ProducesResponseType<StatusResponseDto<EventResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertEventResponse(
        string channelId,
        string eventType,
        [FromBody] UpdateEventResponseDto request,
        CancellationToken ct
    )
    {
        Result<EventResponseDto> result = await _eventResponseService.UpsertAsync(
            channelId,
            eventType,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<EventResponseDto> { Data = result.Value });
    }

    /// <summary>Delete the configured response for an event type.</summary>
    [RequireAction("eventresponses:write")]
    [HttpDelete("{eventType}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteEventResponse(
        string channelId,
        string eventType,
        CancellationToken ct
    )
    {
        Result result = await _eventResponseService.DeleteAsync(channelId, eventType, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }
}
