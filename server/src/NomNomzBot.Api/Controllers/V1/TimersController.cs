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
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.PlatformContent.Entities;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages a channel's scheduled chat message timers, for the dashboard operator running periodic announcements.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/timers")]
[Authorize]
[Tags("Timers")]
public class TimersController : BaseController
{
    private readonly ITimerManagementService _timerService;
    private readonly IPlatformDefaultRestoreService _defaults;

    public TimersController(
        ITimerManagementService timerService,
        IPlatformDefaultRestoreService defaults
    )
    {
        _timerService = timerService;
        _defaults = defaults;
    }

    /// <summary>
    /// What restoring a timer installed from a platform template would change: every field that differs
    /// from the template's current version. 409 NOT_PLATFORM_CONTENT for a timer the channel made itself.
    /// </summary>
    [RequireAction("timers:read")]
    [HttpGet("{id:guid}/platform-default")]
    [ProducesResponseType<StatusResponseDto<PlatformDefaultPreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlatformDefault(
        string channelId,
        Guid id,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(
            await _defaults.PreviewAsync(broadcasterId, PlatformContentKinds.Timer, id, ct)
        );
    }

    /// <summary>Put a template-installed timer back on the template's current version.</summary>
    [RequireAction("timers:write")]
    [NotDestructive(
        "Overwrites one Timer row's own fields with its template; the row, its id and its bound pipeline stay."
    )]
    [HttpPost("{id:guid}/platform-default/restore")]
    [ProducesResponseType<StatusResponseDto<PlatformDefaultPreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RestorePlatformDefault(
        string channelId,
        Guid id,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(
            await _defaults.RestoreAsync(broadcasterId, PlatformContentKinds.Timer, id, ct)
        );
    }

    /// <summary>List the channel's timers, paginated.</summary>
    [RequireAction("timers:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<TimerListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTimers(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<TimerListItem>> result = await _timerService.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Get a single timer by id.</summary>
    [RequireAction("timers:read")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<TimerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimer(string channelId, Guid id, CancellationToken ct)
    {
        Result<TimerDto> result = await _timerService.GetAsync(channelId, id, ct);
        return ResultResponse(result);
    }

    /// <summary>Create a new scheduled timer for the channel.</summary>
    [RequireAction("timers:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<TimerDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTimer(
        string channelId,
        [FromBody] CreateTimerDto request,
        CancellationToken ct
    )
    {
        Result<TimerDto> result = await _timerService.CreateAsync(channelId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetTimer),
            new { channelId, id = result.Value.Id },
            new StatusResponseDto<TimerDto>
            {
                Data = result.Value,
                Message = "Timer created successfully.",
            }
        );
    }

    /// <summary>Update an existing timer's schedule, message, or enabled state.</summary>
    [RequireAction("timers:write")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<TimerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTimer(
        string channelId,
        Guid id,
        [FromBody] UpdateTimerDto request,
        CancellationToken ct
    )
    {
        Result<TimerDto> result = await _timerService.UpdateAsync(channelId, id, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TimerDto> { Data = result.Value });
    }

    /// <summary>Delete a timer.</summary>
    [RequireAction("timers:write")]
    [NotDestructive(
        "Deletes one Timer row; Timer REFERENCES a pipeline (PipelineId) but no entity carries a TimerId FK - the pipeline it fires is untouched."
    )]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTimer(string channelId, Guid id, CancellationToken ct)
    {
        Result result = await _timerService.DeleteAsync(channelId, id, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    /// <summary>Enable or disable a timer without deleting it.</summary>
    [RequireAction("timers:write")]
    [HttpPost("{id:guid}/toggle")]
    [ProducesResponseType<StatusResponseDto<TimerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ToggleTimer(string channelId, Guid id, CancellationToken ct)
    {
        Result<TimerDto> result = await _timerService.ToggleAsync(channelId, id, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TimerDto> { Data = result.Value });
    }
}
