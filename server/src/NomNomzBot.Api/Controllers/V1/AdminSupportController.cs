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
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The cross-tenant support desk (S-ADMIN-7a) — find a person anywhere on the platform and read their real
/// state. A read-only sibling of <see cref="PlatformAdminController"/> on the same <c>admin</c> route prefix,
/// gated on its OWN key (<c>user:support:view</c>): seeing a subject's state and ACTING as them
/// (<c>user:impersonate</c>) are deliberately separate grants. Both actions require a justification, which
/// the service records on the audit row alongside the subject.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/support")]
[Authorize]
[Tags("Admin")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class AdminSupportController(
    IAdminSupportService support,
    ICurrentUserService currentUser,
    IIamCallerPrincipalResolverService actingPrincipalResolver
) : BaseController
{
    /// <summary>Finds people by username / display name / platform id across EVERY tenant.</summary>
    [HttpGet("people")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.UserSupportView)]
    [ProducesResponseType<PaginatedResponse<SupportPersonSearchResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchPeople(
        [FromQuery] string search,
        [FromQuery] string justification,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PagedList<SupportPersonSearchResultDto>>(null!));

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<SupportPersonSearchResultDto>> result = await support.SearchPeopleAsync(
            acting.Value,
            search,
            justification,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>One person's real state across every tenant — each fact labeled with the tenant it belongs to.</summary>
    [HttpGet("people/{subjectUserId:guid}")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.UserSupportView)]
    [ProducesResponseType<StatusResponseDto<SupportPersonViewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPerson(
        Guid subjectUserId,
        [FromQuery] string justification,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<SupportPersonViewDto>(null!));
        return ResultResponse(
            await support.GetPersonAsync(acting.Value, subjectUserId, justification, ct)
        );
    }

    /// <summary>
    /// Replays what actually happened to this person — the real events recorded for them across every tenant,
    /// newest first, each labeled with the tenant it happened in. No history is an empty page, never an error.
    /// </summary>
    [HttpGet("people/{subjectUserId:guid}/history")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.UserSupportView)]
    [ProducesResponseType<PaginatedResponse<SupportPersonHistoryEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPersonHistory(
        Guid subjectUserId,
        [FromQuery] string justification,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PagedList<SupportPersonHistoryEntryDto>>(null!));

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<SupportPersonHistoryEntryDto>> result =
            await support.GetPersonHistoryAsync(
                acting.Value,
                subjectUserId,
                justification,
                pagination,
                ct
            );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>The caller's IAM principal id — a resolve failure DENIES rather than substituting <c>Guid.Empty</c>.</summary>
    private Task<Result<Guid>> ActingPrincipalIdAsync(CancellationToken ct) =>
        actingPrincipalResolver.ResolveActingPrincipalIdAsync(currentUser.UserId, ct);
}
