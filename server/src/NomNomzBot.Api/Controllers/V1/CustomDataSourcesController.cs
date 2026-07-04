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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages custom data sources for dynamic event integration.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/custom-data-sources")]
[Authorize]
[Tags("CustomEvents")]
public sealed class CustomDataSourcesController : BaseController
{
    private readonly ICustomDataSourceService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public CustomDataSourcesController(
        ICustomDataSourceService service,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant
    )
    {
        _service = service;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    private bool TryGetIds(out Guid broadcasterId, out Guid userId)
    {
        broadcasterId = _currentTenant.BroadcasterId ?? Guid.Empty;
        if (broadcasterId == Guid.Empty || !Guid.TryParse(_currentUser.UserId, out userId))
        {
            userId = Guid.Empty;
            return false;
        }
        return true;
    }

    // ── GET /custom-data-sources ─────────────────────────────────────────────

    /// <summary>List all custom data sources for the channel, paginated.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<CustomDataSourceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] PageRequestDto pagination,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid _))
            return Unauthorized();

        Result<PagedList<CustomDataSourceDto>> result = await _service.ListAsync(
            broadcasterId,
            new PaginationParams(pagination.Page, pagination.Take),
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });

        PagedList<CustomDataSourceDto> page = result.Value;
        return Ok(
            new PaginatedResponse<CustomDataSourceDto>
            {
                Data = page.Items,
                Total = page.TotalCount,
                HasMore = page.HasNextPage,
            }
        );
    }

    // ── GET /custom-data-sources/presets ────────────────────────────────────

    /// <summary>List available custom data source presets.</summary>
    [HttpGet("presets")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<CustomDataSourcePresetDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListPresets(CancellationToken ct)
    {
        Result<IReadOnlyList<CustomDataSourcePresetDto>> result = await _service.ListPresetsAsync(
            ct
        );

        return result.IsSuccess
            ? Ok(
                new StatusResponseDto<IReadOnlyList<CustomDataSourcePresetDto>>
                {
                    Data = result.Value!,
                }
            )
            : Problem(result.ErrorMessage ?? "Failed to list presets", statusCode: 500);
    }

    // ── GET /custom-data-sources/{id} ────────────────────────────────────────

    /// <summary>Retrieve a custom data source by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<CustomDataSourceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid _))
            return Unauthorized();

        Result<CustomDataSourceDto> result = await _service.GetAsync(broadcasterId, id, ct);
        return result.IsSuccess
            ? Ok(new StatusResponseDto<CustomDataSourceDto> { Data = result.Value })
            : NotFound(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }

    // ── POST /custom-data-sources ────────────────────────────────────────────

    /// <summary>Create a new custom data source.</summary>
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<CustomDataSourceDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] UpsertCustomDataSourceRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid userId))
            return Unauthorized();

        Result<CustomDataSourceDto> result = await _service.CreateAsync(
            broadcasterId,
            userId,
            request,
            ct
        );

        return result.IsSuccess
            ? CreatedAtAction(
                nameof(Get),
                new { id = result.Value!.Id },
                new StatusResponseDto<CustomDataSourceDto> { Data = result.Value }
            )
            : BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }

    // ── PUT /custom-data-sources/{id} ────────────────────────────────────────

    /// <summary>Update an existing custom data source.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<CustomDataSourceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpsertCustomDataSourceRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid userId))
            return Unauthorized();

        Result<CustomDataSourceDto> result = await _service.UpdateAsync(
            broadcasterId,
            id,
            userId,
            request,
            ct
        );

        if (!result.IsSuccess)
        {
            return result.ErrorMessage!.Contains("NOT_FOUND")
                ? NotFound(new StatusResponseDto<object> { Message = result.ErrorMessage })
                : BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });
        }

        return Ok(new StatusResponseDto<CustomDataSourceDto> { Data = result.Value });
    }

    // ── DELETE /custom-data-sources/{id} ─────────────────────────────────────

    /// <summary>Delete a custom data source.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid userId))
            return Unauthorized();

        Result result = await _service.DeleteAsync(broadcasterId, id, userId, ct);

        return result.IsSuccess
            ? Ok(new StatusResponseDto<bool> { Data = true })
            : NotFound(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }

    // ── POST /custom-data-sources/{id}/test ──────────────────────────────────

    /// <summary>Test a custom data source with sample payload.</summary>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Test(
        Guid id,
        [FromBody] TestCustomDataSourceRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid _))
            return Unauthorized();

        Result result = await _service.TestAsync(broadcasterId, id, request.SamplePayload, ct);

        return result.IsSuccess
            ? Ok(new StatusResponseDto<bool> { Data = true })
            : BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public sealed record TestCustomDataSourceRequest(string SamplePayload);
