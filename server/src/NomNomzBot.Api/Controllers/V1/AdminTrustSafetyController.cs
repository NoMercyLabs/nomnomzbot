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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The platform-wide trust &amp; safety desk (S-ADMIN-8a): correlate one actor's spam-defence detections
/// across EVERY tenant of this deployment, and review/confirm/overturn the account actions the
/// spam-defence engine took automatically. Gated on its own key, <c>trust-safety:review</c> — crossing
/// the tenant boundary and reversing the platform's own automatic calls are what make this privileged.
/// Sibling of <see cref="AdminSpamDefenseController"/> (the platform-defaults editor) and
/// <see cref="AdminSupportController"/> (the read-only per-person desk); a justification is mandatory on
/// every action and lands on the IAM audit row naming the acting operator.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/trust-safety")]
[Authorize]
[Tags("Admin")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class AdminTrustSafetyController(
    ITrustSafetyReviewService trustSafety,
    ICurrentUserService currentUser,
    IIamCallerPrincipalResolverService actingPrincipalResolver
) : BaseController
{
    /// <summary>
    /// Every actor whose spam-defence detections were recorded in two or more tenants, each with the
    /// real detections that back the correlation.
    /// </summary>
    [HttpGet("cross-tenant-signals")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.TrustSafetyReview)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<CrossTenantAbuseSignalDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetCrossTenantSignals(
        [FromQuery] string justification,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(
                acting.WithValue<IReadOnlyList<CrossTenantAbuseSignalDto>>(null!)
            );
        return ResultResponse(
            await trustSafety.GetCrossTenantSignalsAsync(acting.Value, justification, ct)
        );
    }

    /// <summary>The queue of automatic account actions awaiting review, newest first.</summary>
    [HttpGet("review-queue")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.TrustSafetyReview)]
    [ProducesResponseType<PaginatedResponse<TrustSafetyReviewItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviewQueue(
        [FromQuery] string justification,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PagedList<TrustSafetyReviewItemDto>>(null!));

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<TrustSafetyReviewItemDto>> result = await trustSafety.GetReviewQueueAsync(
            acting.Value,
            justification,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>The operator agrees with an automatic action: closes the review, the action is untouched.</summary>
    [HttpPost("review-queue/{detectionId:guid}/confirm")]
    [Authorize(Policy = IamPermissionKeys.TrustSafetyReview)]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(
        Guid detectionId,
        [FromQuery] string justification,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(
            await trustSafety.ConfirmAsync(acting.Value, detectionId, justification, ct)
        );
    }

    /// <summary>
    /// The operator disagrees: reverses the real account action before marking the detection overturned.
    /// </summary>
    [HttpPost("review-queue/{detectionId:guid}/overturn")]
    [Authorize(Policy = IamPermissionKeys.TrustSafetyReview)]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Overturn(
        Guid detectionId,
        [FromQuery] string justification,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(
            await trustSafety.OverturnAsync(acting.Value, detectionId, justification, ct)
        );
    }

    /// <summary>The caller's IAM principal id — a resolve failure DENIES rather than substituting <c>Guid.Empty</c>.</summary>
    private Task<Result<Guid>> ActingPrincipalIdAsync(CancellationToken ct) =>
        actingPrincipalResolver.ResolveActingPrincipalIdAsync(currentUser.UserId, ct);
}
