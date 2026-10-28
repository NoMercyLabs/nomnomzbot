// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
