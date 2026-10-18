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
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// Brings every installed first-party widget that the channel never edited up to the catalogue's current source.
/// The gallery seeder only bumps <see cref="WidgetGalleryItem.SourceRevision"/>; an installed widget keeps the copy
/// it was installed with, so a fixed overlay (the raid recap) never reached the live channel. A widget with its own
/// saved version (<see cref="Widget.IsSourceCustomized"/>) is left for the streamer's explicit reset. Runs after
/// <see cref="FirstPartyWidgetCatalogueSeeder"/>, so the revisions it compares are current.
/// </summary>
public sealed class InstalledWidgetSourceUpgradeSeeder(
    IApplicationDbContext db,
    IWidgetService widgets
) : ISeeder
{
    public int Order => 12;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        Dictionary<Guid, int> revisionByItemId = await db
            .WidgetGalleryItems.IgnoreQueryFilters()
            .Where(item => item.TrustTier == "first_party" && item.DeletedAt == null)
            .ToDictionaryAsync(item => item.Id, item => item.SourceRevision, ct);
        List<Guid> itemIds = [.. revisionByItemId.Keys];

        List<Widget> installs = await db
            .Widgets.IgnoreQueryFilters()
            .Where(widget =>
                widget.GalleryItemId != null && itemIds.Contains(widget.GalleryItemId.Value)
            )
            .ToListAsync(ct);
        installs =
        [
            .. installs.Where(widget =>
                (widget.InstalledSourceRevision ?? 0)
                < revisionByItemId[widget.GalleryItemId!.Value]
            ),
        ];

        List<Guid> staleIds = [.. installs.Select(widget => widget.Id)];
        Dictionary<Guid, int> latestVersions = await db
            .WidgetVersions.IgnoreQueryFilters()
            .Where(version => staleIds.Contains(version.WidgetId))
            .GroupBy(version => version.WidgetId)
            .Select(group => new { WidgetId = group.Key, Latest = group.Max(v => v.VersionNumber) })
            .ToDictionaryAsync(row => row.WidgetId, row => row.Latest, ct);

        foreach (Widget widget in installs)
        {
            int? latest = latestVersions.TryGetValue(widget.Id, out int number) ? number : null;
            if (widget.IsSourceCustomized(latest))
                continue;

            await widgets.UpdateFromGalleryAsync(
                widget.BroadcasterId.ToString(),
                widget.Id.ToString(),
                ct
            );
        }
    }
}
