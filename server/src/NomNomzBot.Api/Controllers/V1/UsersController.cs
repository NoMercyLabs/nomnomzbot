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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Manages user profiles, search, and GDPR data requests. User reads are self-or-Gate-2: a user always
/// reads/edits their OWN row (the Me page), while reading ANOTHER user requires <c>community:read</c> on the
/// resolved tenant (dashboard viewer tooling). GDPR export/delete and profile writes are strictly
/// self-or-platform-admin.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize]
[Tags("Users")]
public class UsersController : BaseController
{
    private readonly IUserService _userService;
    private readonly IGdprService _gdpr;
    private readonly TimeProvider _timeProvider;
    private readonly IActionAuthorizationService _authorization;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public UsersController(
        IUserService userService,
        IGdprService gdpr,
        TimeProvider timeProvider,
        IActionAuthorizationService authorization,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant
    )
    {
        _userService = userService;
        _gdpr = gdpr;
        _timeProvider = timeProvider;
        _authorization = authorization;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    /// <summary>Self-or-Gate-2: the subject themselves, or a caller holding <c>community:read</c> on the tenant.</summary>
    private async Task<bool> CanReadUserAsync(string subjectUserId, CancellationToken ct)
    {
        string? callerId = _currentUser.UserId;
        if (string.IsNullOrEmpty(callerId))
            return false;
        if (string.Equals(callerId, subjectUserId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (
            !Guid.TryParse(callerId, out Guid callerGuid)
            || _currentTenant.BroadcasterId is not Guid tenantId
            || tenantId == Guid.Empty
        )
            return false;
        Result<bool> authorized = await _authorization.AuthorizeActionAsync(
            callerGuid,
            tenantId,
            "community:read",
            ct
        );
        return authorized.IsSuccess && authorized.Value;
    }

    /// <summary>Search for users by name or username.</summary>
    [HttpGet]
    [RequireAction("community:read")]
    [ProducesResponseType<PaginatedResponse<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string? query,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequestResponse("A search query is required.");

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<UserSearchResult>> result = await _userService.SearchAsync(
            query,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Retrieve a user by ID (self-or-Gate-2, authorized in-action).</summary>
    [HttpGet("{userId}")]
    [ProducesResponseType<StatusResponseDto<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUser(string userId, CancellationToken ct)
    {
        if (!await CanReadUserAsync(userId, ct))
            return UnauthorizedResponse();
        Result<UserDto> result = await _userService.GetAsync(userId, ct);
        return ResultResponse(result);
    }

    /// <summary>Retrieve a user's profile information (self-or-Gate-2, authorized in-action).</summary>
    [HttpGet("{userId}/profile")]
    [ProducesResponseType<StatusResponseDto<UserProfileDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserProfile(string userId, CancellationToken ct)
    {
        if (!await CanReadUserAsync(userId, ct))
            return UnauthorizedResponse();
        Result<UserProfileDto> result = await _userService.GetProfileAsync(userId, ct);
        return ResultResponse(result);
    }

    /// <summary>Update a user's profile information — strictly the user themselves (or a platform admin).</summary>
    [HttpPut("{userId}/profile")]
    [ProducesResponseType<StatusResponseDto<UserProfileDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateUserProfile(
        string userId,
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken ct
    )
    {
        // Self-only write: a manager may READ a viewer's profile (community:read), but never edit another
        // user's identity (display name / email / pronoun) — that is the subject's own data.
        string? callerId = _currentUser.UserId;
        if (
            !string.Equals(callerId, userId, StringComparison.OrdinalIgnoreCase)
            && !User.IsInRole("admin")
        )
            return UnauthorizedResponse("You may only edit your own profile.");

        Result<UserProfileDto> result = await _userService.UpdateProfileAsync(userId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<UserProfileDto> { Data = result.Value });
    }

    /// <summary>Get all channels a user appears in (as broadcaster or moderation role).</summary>
    [HttpGet("{userId}/channels")]
    [ProducesResponseType<
        StatusResponseDto<List<NomNomzBot.Application.Identity.Dtos.UserChannelAppearanceDto>>
    >(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetUserChannels(string userId, CancellationToken ct)
    {
        string? callerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
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
        string? callerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
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
            $"user-data-export-{userId}-{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd}.json"
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
