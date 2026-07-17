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
using NomNomzBot.Application.Games.Services;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>
/// Widget-key resolution over the widget domain (live-games.md D5): the gallery item whose
/// <c>NaturalKey</c> is the manifest's <c>OverlayWidgetKey</c> → the channel's installed, enabled widget
/// (first-party widgets are seeded as gallery items and installed per channel).
/// </summary>
public sealed class LiveGameOverlayResolver(IApplicationDbContext db) : ILiveGameOverlayResolver
{
    public async Task<Guid?> ResolveAsync(
        Guid broadcasterId,
        string overlayWidgetKey,
        CancellationToken ct = default
    )
    {
        Guid? galleryItemId = await db
            .WidgetGalleryItems.AsNoTracking()
            .Where(i => i.NaturalKey == overlayWidgetKey)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);
        if (galleryItemId is null)
            return null;
        return await db
            .Widgets.AsNoTracking()
            .Where(w =>
                w.BroadcasterId == broadcasterId
                && w.GalleryItemId == galleryItemId
                && w.IsEnabled
                && w.DeletedAt == null
            )
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);
    }
}
