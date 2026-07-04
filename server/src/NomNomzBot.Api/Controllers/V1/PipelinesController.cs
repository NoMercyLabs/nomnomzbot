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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages a channel's pipelines, the action-chain automations behind custom commands and event responses, for the dashboard operator building them in the visual editor.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/pipelines")]
[Authorize]
[Tags("Pipelines")]
public class PipelinesController : BaseController
{
    private readonly IPipelineService _pipelineService;
    private readonly ICommandConfigValidator _validator;

    public PipelinesController(IPipelineService pipelineService, ICommandConfigValidator validator)
    {
        _pipelineService = pipelineService;
        _validator = validator;
    }

    /// <summary>List the channel's pipelines, paginated.</summary>
    [RequireAction("pipelines:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<PipelineListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPipelines(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<PipelineListItemDto>> result = await _pipelineService.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Get a single pipeline by id, including its action graph.</summary>
    [RequireAction("pipelines:read")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<PipelineDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPipeline(string channelId, Guid id, CancellationToken ct)
    {
        Result<PipelineDto> result = await _pipelineService.GetAsync(channelId, id, ct);
        return ResultResponse(result);
    }

    /// <summary>Create a new pipeline for the channel.</summary>
    [RequireAction("pipelines:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<PipelineDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePipeline(
        string channelId,
        [FromBody] CreatePipelineDto request,
        CancellationToken ct
    )
    {
        Result<PipelineDto> result = await _pipelineService.CreateAsync(channelId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetPipeline),
            new { channelId, id = result.Value.Id },
            new StatusResponseDto<PipelineDto>
            {
                Data = result.Value,
                Message = "Pipeline created successfully.",
            }
        );
    }

    /// <summary>Update an existing pipeline's name, settings, or action graph.</summary>
    [RequireAction("pipelines:write")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<PipelineDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePipeline(
        string channelId,
        Guid id,
        [FromBody] UpdatePipelineDto request,
        CancellationToken ct
    )
    {
        Result<PipelineDto> result = await _pipelineService.UpdateAsync(channelId, id, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<PipelineDto> { Data = result.Value });
    }

    /// <summary>Delete a pipeline.</summary>
    [RequireAction("pipelines:write")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePipeline(string channelId, Guid id, CancellationToken ct)
    {
        Result result = await _pipelineService.DeleteAsync(channelId, id, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    /// <summary>
    /// Validates a pipeline graph without persisting it. Returns a <see cref="PipelineValidationResult"/>
    /// so the editor can surface errors inline before the user hits Save.
    /// </summary>
    [RequireAction("pipelines:validate")]
    [HttpPost("validate")]
    [ProducesResponseType<StatusResponseDto<PipelineValidationResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidatePipeline(
        string channelId,
        [FromBody] PipelineGraphInput body,
        CancellationToken ct
    )
    {
        Result<PipelineValidationResult> result = await _validator.ValidatePipelineAsync(body, ct);
        return ResultResponse(result);
    }
}
