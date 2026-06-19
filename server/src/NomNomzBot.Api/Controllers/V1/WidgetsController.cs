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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/widgets")]
[Authorize]
[Tags("Widgets")]
public class WidgetsController : BaseController
{
    private readonly IWidgetService _widgetService;

    public WidgetsController(IWidgetService widgetService)
    {
        _widgetService = widgetService;
    }

    [HttpGet]
    [ProducesResponseType<PaginatedResponse<WidgetDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWidgets(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(
            request.Page,
            request.Take,
            request.Sort,
            request.Order
        );
        Result<PagedList<WidgetDetail>> result = await _widgetService.ListAsync(channelId, pagination, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpGet("{widgetId}")]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWidget(
        string channelId,
        string widgetId,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.GetAsync(channelId, widgetId, ct);
        return ResultResponse(result);
    }

    [HttpPost]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWidget(
        string channelId,
        [FromBody] CreateWidgetRequest request,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.CreateAsync(channelId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetWidget),
            new { channelId, widgetId = result.Value.Id },
            new StatusResponseDto<WidgetDetail>
            {
                Data = result.Value,
                Message = "Widget created successfully.",
            }
        );
    }

    [HttpPut("{widgetId}")]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateWidget(
        string channelId,
        string widgetId,
        [FromBody] UpdateWidgetRequest request,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.UpdateAsync(channelId, widgetId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<WidgetDetail> { Data = result.Value });
    }

    [HttpDelete("{widgetId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteWidget(
        string channelId,
        string widgetId,
        CancellationToken ct
    )
    {
        Result result = await _widgetService.DeleteAsync(channelId, widgetId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }
}
