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

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/timers")]
[Authorize]
[Tags("Timers")]
public class TimersController : BaseController
{
    private readonly ITimerManagementService _timerService;

    public TimersController(ITimerManagementService timerService)
    {
        _timerService = timerService;
    }

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

    [RequireAction("timers:read")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<TimerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimer(string channelId, Guid id, CancellationToken ct)
    {
        Result<TimerDto> result = await _timerService.GetAsync(channelId, id, ct);
        return ResultResponse(result);
    }

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

    [RequireAction("timers:write")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTimer(string channelId, Guid id, CancellationToken ct)
    {
        Result result = await _timerService.DeleteAsync(channelId, id, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

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
