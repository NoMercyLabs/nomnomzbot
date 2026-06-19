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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[Authorize(Roles = "admin")]
[Tags("Admin")]
public class AdminController : BaseController
{
    private readonly IAdminService _adminService;
    private readonly IApplicationDbContext _db;

    public AdminController(IAdminService adminService, IApplicationDbContext db)
    {
        _adminService = adminService;
        _db = db;
    }

    public record ServiceHealthResponseDto(string Name, string Status);

    public record PlatformEventDto(string Message, string Time, string Type);

    /// <summary>Returns aggregate statistics for the admin dashboard.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<StatusResponseDto<AdminStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdminStats(CancellationToken ct)
    {
        Result<AdminStatsDto> result = await _adminService.GetStatsAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>Returns all channels with their current status.</summary>
    [HttpGet("channels")]
    [ProducesResponseType<PaginatedResponse<AdminChannelDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListChannels(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminChannelDto>> result = await _adminService.ListChannelsAsync(
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Returns all registered users.</summary>
    [HttpGet("users")]
    [ProducesResponseType<PaginatedResponse<AdminUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUsers(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminUserDto>> result = await _adminService.ListUsersAsync(pagination, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Returns system health and process metrics.</summary>
    [HttpGet("system")]
    [ProducesResponseType<StatusResponseDto<AdminSystemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSystemHealth(CancellationToken ct)
    {
        Result<AdminSystemDto> result = await _adminService.GetSystemHealthAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>Returns service health list (for dashboard health panel).</summary>
    [HttpGet("health")]
    [ProducesResponseType<StatusResponseDto<List<ServiceHealthResponseDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        Result<AdminSystemDto> result = await _adminService.GetSystemHealthAsync(ct);
        if (result.IsFailure)
            return ResultResponse(result);

        List<ServiceHealthResponseDto> services = result.Value.Services
            .Select(s => new ServiceHealthResponseDto(s.Name, s.Status))
            .ToList();

        return Ok(new StatusResponseDto<List<ServiceHealthResponseDto>> { Data = services });
    }

    /// <summary>Returns recent platform events for the admin dashboard.</summary>
    [HttpGet("events")]
    [ProducesResponseType<StatusResponseDto<List<PlatformEventDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents(CancellationToken ct)
    {
        var events = await _db.ChannelEvents
            .OrderByDescending(e => e.CreatedAt)
            .Take(20)
            .Select(e => new
            {
                e.Type,
                e.CreatedAt,
                Username = e.User != null ? e.User.DisplayName : null,
            })
            .ToListAsync(ct);

        List<PlatformEventDto> dtos = events.Select(e =>
        {
            string message = e.Username is not null
                ? $"{e.Username}: {e.Type}"
                : e.Type;

            string eventType = e.Type.Contains("sub") ? "success"
                : e.Type.Contains("ban") || e.Type.Contains("timeout") ? "warning"
                : "info";

            return new PlatformEventDto(message, e.CreatedAt.ToString("HH:mm"), eventType);
        }).ToList();

        return Ok(new StatusResponseDto<List<PlatformEventDto>> { Data = dtos });
    }
}
