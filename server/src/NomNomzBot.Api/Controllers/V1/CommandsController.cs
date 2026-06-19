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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/commands")]
[Authorize]
[Tags("Commands")]
public class CommandsController : BaseController
{
    private readonly ICommandService _commandService;

    public CommandsController(ICommandService commandService)
    {
        _commandService = commandService;
    }

    [HttpGet]
    [ProducesResponseType<PaginatedResponse<CommandDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCommands(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<CommandListItem>> result = await _commandService.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpGet("{commandName}")]
    [ProducesResponseType<StatusResponseDto<CommandDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommand(
        string channelId,
        string commandName,
        CancellationToken ct
    )
    {
        Result<CommandDto> result = await _commandService.GetAsync(channelId, commandName, ct);
        return ResultResponse(result);
    }

    [HttpPost]
    [ProducesResponseType<StatusResponseDto<CommandDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCommand(
        string channelId,
        [FromBody] CreateCommandDto request,
        CancellationToken ct
    )
    {
        Result<CommandDto> result = await _commandService.CreateAsync(channelId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetCommand),
            new { channelId, commandName = result.Value.Name },
            new StatusResponseDto<CommandDto>
            {
                Data = result.Value,
                Message = "Command created successfully.",
            }
        );
    }

    [HttpPut("{commandName}")]
    [ProducesResponseType<StatusResponseDto<CommandDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCommand(
        string channelId,
        string commandName,
        [FromBody] UpdateCommandDto request,
        CancellationToken ct
    )
    {
        Result<CommandDto> result = await _commandService.UpdateAsync(
            channelId,
            commandName,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<CommandDto> { Data = result.Value });
    }

    [HttpDelete("{commandName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCommand(
        string channelId,
        string commandName,
        CancellationToken ct
    )
    {
        Result result = await _commandService.DeleteAsync(channelId, commandName, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }
}
