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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The widget gallery (widgets-overlays.md §5c). Browsing is public and JWT-less (verified items only);
/// submitting a community widget takes any authenticated user; the review/pin moderation surface is
/// platform-IAM gated on <c>gallery:review</c>. Installing/cloning is authorized elsewhere (the
/// channel-scoped <c>WidgetsController</c>).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/widget-gallery")]
[Tags("Widget Gallery")]
public class WidgetGalleryController(
    IWidgetGalleryService galleryService,
    IAuthorizationService authorization,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>
    /// Browse the verified widget catalogue, paginated, filterable by framework and trust tier. A caller
    /// holding <c>gallery:review</c> may additionally filter by <c>reviewStatus</c> to read the moderation queue.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PaginatedResponse<GalleryItemSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListGalleryItems(
        [FromQuery] GalleryListRequest filter,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<GalleryItemSummary>> result = await galleryService.ListAsync(
            filter,
            pagination,
            await IsReviewerAsync(),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>
    /// Get one verified gallery item in full (incl. its source, for preview). The <c>galleryItemId</c> route
    /// param is a <see cref="Guid"/>, so the registered <c>UlidGuidModelBinder</c> decodes both a 26-char ULID
    /// and a raw guid; an unknown / non-verified id returns 404 (a <c>gallery:review</c> caller also reads
    /// non-verified submissions).
    /// </summary>
    [HttpGet("{galleryItemId}")]
    [AllowAnonymous]
    [ProducesResponseType<StatusResponseDto<GalleryItemDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGalleryItem(Guid galleryItemId, CancellationToken ct)
    {
        Result<GalleryItemDetail> result = await galleryService.GetAsync(
            galleryItemId.ToString(),
            await IsReviewerAsync(),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<GalleryItemDetail> { Data = result.Value });
    }

    /// <summary>Submit a community widget (GitHub-pinned; enters the moderation queue as <c>submitted</c>/<c>unverified</c>).</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType<StatusResponseDto<GalleryItemDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> SubmitGalleryItem(
        [FromBody] SubmitGalleryItemRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        Result<GalleryItemDetail> result = await galleryService.SubmitAsync(caller, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return StatusCode(
            StatusCodes.Status201Created,
            new StatusResponseDto<GalleryItemDetail> { Data = result.Value }
        );
    }

    /// <summary>Review a submission: <c>in_review</c> | <c>verified</c> | <c>rejected</c> (platform moderation).</summary>
    [HttpPost("{galleryItemId}/review")]
    [Authorize(Policy = IamPermissionKeys.GalleryReview)]
    public async Task<IActionResult> ReviewGalleryItem(
        Guid galleryItemId,
        [FromBody] ReviewGalleryItemRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await galleryService.ReviewAsync(caller, galleryItemId, request, ct));
    }

    /// <summary>Re-pin a submission to a new commit/tag — always forces it back through review.</summary>
    [HttpPost("{galleryItemId}/pin")]
    [Authorize(Policy = IamPermissionKeys.GalleryReview)]
    public async Task<IActionResult> UpdateGalleryItemPin(
        Guid galleryItemId,
        [FromBody] UpdatePinRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(
            await galleryService.UpdatePinAsync(caller, galleryItemId, request, ct)
        );
    }

    /// <summary>The anonymous reads widen to the moderation queue only for a <c>gallery:review</c> principal.</summary>
    private async Task<bool> IsReviewerAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
            return false;
        AuthorizationResult verdict = await authorization.AuthorizeAsync(
            User,
            IamPermissionKeys.GalleryReview
        );
        return verdict.Succeeded;
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
