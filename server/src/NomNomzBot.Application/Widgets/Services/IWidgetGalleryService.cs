// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The public, read-only browse side of the first-party widget catalogue (widgets-overlays.md §5c). Anonymous and
/// GLOBAL (the gallery tables carry no tenant scope): the dashboard reads it pre-auth to list installable/cloneable
/// widgets and preview one. Only <c>ReviewStatus == "verified"</c> items are ever returned, so unverified community
/// submissions never leak through this surface. The submit/review/pin write side is a separate, IAM-gated service.
/// </summary>
public interface IWidgetGalleryService
{
    /// <summary>
    /// List the verified gallery catalogue, most-installed then newest first, applying the optional
    /// framework/trust-tier filters. Never surfaces a non-verified item.
    /// </summary>
    Task<Result<PagedList<GalleryItemSummary>>> ListAsync(
        GalleryListRequest request,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get one verified gallery item in full (incl. its source, for preview). Returns NOT_FOUND when the id is
    /// unknown, soft-deleted, or the item is not verified.
    /// </summary>
    Task<Result<GalleryItemDetail>> GetAsync(
        string galleryItemId,
        CancellationToken cancellationToken = default
    );
}
