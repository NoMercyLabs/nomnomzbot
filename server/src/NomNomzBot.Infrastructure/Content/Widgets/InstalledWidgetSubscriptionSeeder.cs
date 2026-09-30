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
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// Brings every installed first-party widget up to its catalogue's default event subscriptions. An install copies
/// the defaults once, so a topic the catalogue gains later (chat_box's moderation events) never reached older
/// installs, and subscription-routed events for that topic were silently dropped. Additive only: a subscription the
/// install already holds is never removed or reordered. Runs after <see cref="FirstPartyWidgetCatalogueSeeder"/>.
/// Does not call <c>SaveChanges</c> (the seed runner owns the single transaction).
/// </summary>
public sealed class InstalledWidgetSubscriptionSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public InstalledWidgetSubscriptionSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 11;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        Dictionary<string, List<string>> defaultsByKey = FirstPartyWidgetCatalogue.All.ToDictionary(
            widget => widget.Key,
            widget => widget.DefaultEventSubscriptions,
            StringComparer.Ordinal
        );
        List<string> keys = [.. defaultsByKey.Keys];

        Dictionary<Guid, string> keyByItemId = await _db
            .WidgetGalleryItems.IgnoreQueryFilters()
            .Where(item => item.TrustTier == "first_party" && keys.Contains(item.NaturalKey!))
            .ToDictionaryAsync(item => item.Id, item => item.NaturalKey!, ct);
        List<Guid> itemIds = [.. keyByItemId.Keys];

        List<Widget> installs = await _db
            .Widgets.IgnoreQueryFilters()
            .Where(widget =>
                widget.GalleryItemId != null && itemIds.Contains(widget.GalleryItemId.Value)
            )
            .ToListAsync(ct);

        foreach (Widget widget in installs)
        {
            List<string> defaults = defaultsByKey[keyByItemId[widget.GalleryItemId!.Value]];
            List<string> missing =
            [
                .. defaults.Except(widget.EventSubscriptions, StringComparer.Ordinal),
            ];
            if (missing.Count > 0)
                widget.EventSubscriptions = [.. widget.EventSubscriptions, .. missing];
        }
    }
}
