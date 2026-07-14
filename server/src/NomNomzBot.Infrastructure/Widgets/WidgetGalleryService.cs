// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Read side of the public widget catalogue. The gallery tables are GLOBAL (no tenant query filter), so these reads
/// need no channel scope; the soft-delete global filter still applies. Every query is pinned to
/// <c>ReviewStatus == "verified"</c> — the verified-only invariant that keeps unverified/in-review submissions off
/// this anonymous surface (widgets-overlays.md §5c). The <c>AvailableInSaaS</c> gate is a later profile-aware slice.
/// </summary>
public class WidgetGalleryService : IWidgetGalleryService
{
    private const string VerifiedStatus = "verified";

    private readonly IApplicationDbContext _db;

    public WidgetGalleryService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PagedList<GalleryItemSummary>>> ListAsync(
        GalleryListRequest request,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<WidgetGalleryItem> query = _db.WidgetGalleryItems.Where(i =>
            i.ReviewStatus == VerifiedStatus
        );

        if (!string.IsNullOrWhiteSpace(request.Framework))
            query = query.Where(i => i.Framework == request.Framework);
        if (!string.IsNullOrWhiteSpace(request.TrustTier))
            query = query.Where(i => i.TrustTier == request.TrustTier);

        int total = await query.CountAsync(cancellationToken);

        List<WidgetGalleryItem> items = await query
            .OrderByDescending(i => i.InstallCount)
            .ThenByDescending(i => i.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        List<GalleryItemSummary> summaries = items.Select(ToSummary).ToList();
        return Result.Success(
            new PagedList<GalleryItemSummary>(
                summaries,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<GalleryItemDetail>> GetAsync(
        string galleryItemId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(galleryItemId, out Guid id))
            return Errors.NotFound<GalleryItemDetail>("WidgetGalleryItem", galleryItemId);

        WidgetGalleryItem? item = await _db.WidgetGalleryItems.FirstOrDefaultAsync(
            i => i.Id == id && i.ReviewStatus == VerifiedStatus,
            cancellationToken
        );

        if (item is null)
            return Errors.NotFound<GalleryItemDetail>("WidgetGalleryItem", galleryItemId);

        return Result.Success(ToDetail(item));
    }

    private static GalleryItemSummary ToSummary(WidgetGalleryItem i) =>
        new(
            i.Id,
            i.Name,
            i.Description,
            i.Framework,
            i.TrustTier,
            i.InstallCount,
            i.AvailableInSaaS
        );

    private static GalleryItemDetail ToDetail(WidgetGalleryItem i) =>
        new(
            i.Id,
            i.Name,
            i.Description,
            i.Framework,
            i.TrustTier,
            i.InstallCount,
            i.AvailableInSaaS,
            i.SourceKind,
            new Dictionary<string, object>(i.DefaultSettings),
            [.. i.DefaultEventSubscriptions],
            i.SourceCode
        );
}
