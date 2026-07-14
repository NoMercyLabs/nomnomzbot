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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The public, JWT-less browse surface for the first-party widget catalogue (widgets-overlays.md §5c). The gallery
/// tables are GLOBAL, so any dashboard can list and preview verified widgets pre-auth; only <c>verified</c> items are
/// exposed. Installing/cloning one is authorized elsewhere (channel-scoped <c>WidgetsController</c>).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/widget-gallery")]
[AllowAnonymous]
[Tags("Widget Gallery")]
public class WidgetGalleryController : BaseController
{
    private readonly IWidgetGalleryService _galleryService;

    public WidgetGalleryController(IWidgetGalleryService galleryService)
    {
        _galleryService = galleryService;
    }

    /// <summary>Browse the verified widget catalogue, paginated, filterable by framework and trust tier.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<GalleryItemSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListGalleryItems(
        [FromQuery] GalleryListRequest filter,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<GalleryItemSummary>> result = await _galleryService.ListAsync(
            filter,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>
    /// Get one verified gallery item in full (incl. its source, for preview). The <c>galleryItemId</c> route param is
    /// a <see cref="Guid"/>, so the registered <c>UlidGuidModelBinder</c> decodes both a 26-char ULID and a raw guid;
    /// an unknown / non-verified id returns 404.
    /// </summary>
    [HttpGet("{galleryItemId}")]
    [ProducesResponseType<StatusResponseDto<GalleryItemDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGalleryItem(Guid galleryItemId, CancellationToken ct)
    {
        Result<GalleryItemDetail> result = await _galleryService.GetAsync(
            galleryItemId.ToString(),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<GalleryItemDetail> { Data = result.Value });
    }
}
