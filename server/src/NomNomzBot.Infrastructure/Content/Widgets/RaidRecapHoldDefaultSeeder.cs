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

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// Moves an installed raid recap that still carries the OLD default hold (20 seconds) to the new default (60 seconds)
/// exactly once. An installed widget keeps the settings it was installed with, so raising the catalogue default
/// never reached a channel that already had the widget. A recap counts as never edited when every setting equals the
/// old default (the translated <c>labels</c> text aside); a recap the streamer changed in any way keeps its own hold.
/// <see cref="Widget.ConfigSchemaVersion"/> is the once-only marker: 1 means the move was never decided, 2 means it
/// was, so a streamer who later sets 20 again is not moved back. Runs after the gallery seeder (Order 10).
/// </summary>
public sealed class RaidRecapHoldDefaultSeeder(IApplicationDbContext db) : ISeeder
{
    private const string WidgetKey = "raid_recap";
    private const string HoldKey = "holdSeconds";
    private const string LabelsKey = "labels";
    private const int OldHoldSeconds = 20;
    private const int DecidedVersion = 2;

    public int Order => 13;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<Guid> itemIds = await db
            .WidgetGalleryItems.IgnoreQueryFilters()
            .Where(item => item.NaturalKey == WidgetKey)
            .Select(item => item.Id)
            .ToListAsync(ct);

        List<Widget> installs = await db
            .Widgets.IgnoreQueryFilters()
            .Where(widget =>
                widget.GalleryItemId != null
                && itemIds.Contains(widget.GalleryItemId.Value)
                && widget.ConfigSchemaVersion < DecidedVersion
            )
            .ToListAsync(ct);

        FirstPartyWidgetDefinition recap = FirstPartyWidgetCatalogue.All.Single(w =>
            w.Key == WidgetKey
        );
        int newHold = Convert.ToInt32(recap.DefaultSettings[HoldKey]);
        JsonObject oldDefaults = Without(recap.DefaultSettings, LabelsKey);
        oldDefaults[HoldKey] = OldHoldSeconds;

        foreach (Widget widget in installs)
        {
            if (JsonNode.DeepEquals(Without(widget.Settings, LabelsKey), oldDefaults))
            {
                Dictionary<string, object> settings = new(widget.Settings) { [HoldKey] = newHold };
                widget.Settings = settings;
            }

            widget.ConfigSchemaVersion = DecidedVersion;
        }

        await db.SaveChangesAsync(ct);
    }

    private static JsonObject Without(IDictionary<string, object> settings, string key)
    {
        JsonObject node = (JsonObject)JsonSerializer.SerializeToNode(settings)!;
        node.Remove(key);
        return node;
    }
}
