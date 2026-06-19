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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize]
[Tags("Users")]
public class UsersController : BaseController
{
    private readonly IUserService _userService;
    private readonly IGdprService _gdpr;

    public UsersController(IUserService userService, IGdprService gdpr)
    {
        _userService = userService;
        _gdpr = gdpr;
    }

    [HttpGet]
    [ProducesResponseType<PaginatedResponse<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string? query,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequestResponse("A search query is required.");

        PaginationParams pagination = new(
            request.Page,
            request.Take,
            request.Sort,
            request.Order
        );
        Result<PagedList<UserSearchResult>> result = await _userService.SearchAsync(query, pagination, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpGet("{userId}")]
    [ProducesResponseType<StatusResponseDto<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUser(string userId, CancellationToken ct)
    {
        Result<UserDto> result = await _userService.GetAsync(userId, ct);
        return ResultResponse(result);
    }

    [HttpGet("{userId}/profile")]
    [ProducesResponseType<StatusResponseDto<UserProfileDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserProfile(string userId, CancellationToken ct)
    {
        Result<UserProfileDto> result = await _userService.GetProfileAsync(userId, ct);
        return ResultResponse(result);
    }

    [HttpPut("{userId}/profile")]
    [ProducesResponseType<StatusResponseDto<UserProfileDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateUserProfile(
        string userId,
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken ct
    )
    {
        Result<UserProfileDto> result = await _userService.UpdateProfileAsync(userId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<UserProfileDto> { Data = result.Value });
    }

    [HttpGet("{userId}/channels")]
    [ProducesResponseType<StatusResponseDto<List<NomNomzBot.Application.Identity.Dtos.UserChannelAppearanceDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserChannels(string userId, CancellationToken ct)
    {
        string? callerId =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User.IsInRole("admin");
        if (callerId != userId && !isAdmin)
            return UnauthorizedResponse("You may only view your own channel list.");

        Result<List<NomNomzBot.Application.Identity.Dtos.UserChannelAppearanceDto>> result =
            await _userService.GetUserChannelsAsync(userId, ct);
        return ResultResponse(result);
    }

    // ─── GDPR endpoints ───────────────────────────────────────────────────────

    /// <summary>Returns a summary of the user's data (GDPR data summary).</summary>
    [HttpGet("{userId}/stats")]
    [ProducesResponseType<StatusResponseDto<UserStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserStats(string userId, CancellationToken ct)
    {
        string? callerId =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (callerId != userId)
            return UnauthorizedResponse("You may only view your own stats.");
        Result<UserStatsDto> result = await _userService.GetStatsAsync(userId, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Export all personal data for the specified user (GDPR right of access).
    /// Returns a JSON file download containing all data we hold for this user.
    /// </summary>
    [HttpGet("{userId}/data-export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportUserData(string userId, CancellationToken ct)
    {
        string? callerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User.IsInRole("admin");
        if (callerId != userId && !isAdmin)
            return UnauthorizedResponse("You may only export your own data.");

        Result<string> result = await _gdpr.ExportUserDataAsync(userId, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(result.Value);
        return File(
            bytes,
            "application/json",
            $"user-data-export-{userId}-{DateTime.UtcNow:yyyyMMdd}.json"
        );
    }

    /// <summary>
    /// Delete all personal data for the specified user (GDPR right to erasure).
    /// This action is irreversible.
    /// </summary>
    [HttpDelete("{userId}/data")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteUserData(string userId, CancellationToken ct)
    {
        string? callerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        bool isAdmin = User.IsInRole("admin");
        if (callerId != userId && !isAdmin)
            return UnauthorizedResponse("You may only delete your own data.");

        Result result = await _gdpr.DeleteUserDataAsync(userId, ct);
        return ResultResponse(result);
    }
}
