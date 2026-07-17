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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// A channel's generic named pick-lists — the reusable primitive behind the <c>{list.pick.&lt;name&gt;}</c> template
/// variable. Gate 1 is <c>[Authorize]</c> + tenant resolution; Gate 2 is the per-route <c>[RequireAction]</c> floor:
/// reading + curating (create/edit) sit at Moderator with a Vip floor so a trusted VIP can help build lists, while
/// deleting stays Moderator-floored (real data loss) — mirroring the Quotes library.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/picklists")]
[Authorize]
[Tags("PickLists")]
public class PickListsController : BaseController
{
    private readonly IPickListService _pickLists;
    private readonly ICurrentTenantService _tenant;

    public PickListsController(IPickListService pickLists, ICurrentTenantService tenant)
    {
        _pickLists = pickLists;
        _tenant = tenant;
    }

    /// <summary>List the channel's pick-lists with optional name/description search, paginated.</summary>
    [RequireAction("picklists:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<PickListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPickLists(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        PickListSearch search = new(request.Search);
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<PickListDto>> result = await _pickLists.ListAsync(
            broadcasterId,
            search,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Read a single pick-list by its id.</summary>
    [RequireAction("picklists:read")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<PickListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPickList(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<PickListDto> result = await _pickLists.GetAsync(broadcasterId, id, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Sample one random entry from a pick-list by its id — the read the dashboard's "Test" button calls to preview
    /// what <c>{list.pick.&lt;name&gt;}</c> would draw. <c>NOT_FOUND</c> when the list is missing, <c>PICKLIST_EMPTY</c>
    /// (also 404) when it has no entries.
    /// </summary>
    [RequireAction("picklists:read")]
    [HttpGet("{id:guid}/pick")]
    [ProducesResponseType<StatusResponseDto<PickListPreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewPick(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        // Resolve the list by id first so we sample the exact list the dashboard opened (and 404 a bad id), then
        // pick by its unique name — the same read the template variable rides on.
        Result<PickListDto> list = await _pickLists.GetAsync(broadcasterId, id, ct);
        if (list.IsFailure)
            return ResultResponse(list);

        Result<string> pick = await _pickLists.PickRandomAsync(broadcasterId, list.Value.Name, ct);
        if (pick.IsFailure)
            return ResultResponse(pick);

        return Ok(
            new StatusResponseDto<PickListPreviewDto> { Data = new PickListPreviewDto(pick.Value) }
        );
    }

    /// <summary>Create a new pick-list, returning 201 with its id.</summary>
    [RequireAction("picklists:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<PickListDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePickList(
        [FromBody] CreatePickListRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<PickListDto> result = await _pickLists.CreateAsync(broadcasterId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetPickList),
            new { id = result.Value.Id },
            new StatusResponseDto<PickListDto>
            {
                Data = result.Value,
                Message = $"Pick list '{result.Value.Name}' created.",
            }
        );
    }

    /// <summary>Update an existing pick-list by its id.</summary>
    [RequireAction("picklists:write")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<PickListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePickList(
        Guid id,
        [FromBody] UpdatePickListRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<PickListDto> result = await _pickLists.UpdateAsync(broadcasterId, id, request, ct);
        return ResultResponse(result);
    }

    /// <summary>Delete a pick-list by its id.</summary>
    [RequireAction("picklists:delete")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<PickListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePickList(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result result = await _pickLists.DeleteAsync(broadcasterId, id, ct);
        return ResultResponse(result);
    }
}
