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

/// <summary>Read-only pipeline run history (H.4 PipelineExecution, S008b/S008c) — lets the dashboard
/// operator see why a command or event-response pipeline misbehaved: per-run outcome and, on the
/// detail read, the per-step logs that pinpoint the failing step.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/pipeline-executions")]
[Authorize]
[Tags("Pipelines")]
public class PipelineExecutionsController : BaseController
{
    private readonly IPipelineExecutionQueryService _queryService;

    public PipelineExecutionsController(IPipelineExecutionQueryService queryService)
    {
        _queryService = queryService;
    }

    /// <summary>List the channel's pipeline runs, newest-first, paginated. <c>failuresOnly=true</c>
    /// restricts to non-success outcomes.</summary>
    [RequireAction("pipelines:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<PipelineExecutionSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListExecutions(
        string channelId,
        [FromQuery] PageRequestDto request,
        [FromQuery] bool failuresOnly,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<PipelineExecutionSummaryDto>> result = await _queryService.ListAsync(
            channelId,
            pagination,
            failuresOnly,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Get one run's detail, including its ordered per-step logs, so the failing step is
    /// identifiable.</summary>
    [RequireAction("pipelines:read")]
    [HttpGet("{id:long}")]
    [ProducesResponseType<StatusResponseDto<PipelineExecutionDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExecution(string channelId, long id, CancellationToken ct)
    {
        Result<PipelineExecutionDetailDto> result = await _queryService.GetDetailAsync(
            channelId,
            id,
            ct
        );
        return ResultResponse(result);
    }
}
