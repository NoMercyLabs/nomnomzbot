// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// Seeds the first-party overlay widgets into the GLOBAL gallery catalogue (schema §P.8, Order 10 — global
/// reference data, no FK dependencies). Each is a <c>vue</c> SFC that ships in-repo (its source embedded as
/// <c>Content/Widgets/Assets/{key}.vue</c>) and is copied verbatim into
/// <see cref="WidgetGalleryItem.SourceCode"/> so a channel can install or clone-to-edit it. The per-widget
/// metadata + default settings come from the shared <see cref="FirstPartyWidgetCatalogue"/> (also read by the
/// settings-schema provider, so a key can never drift between the two). Idempotent: upserts by
/// <see cref="WidgetGalleryItem.NaturalKey"/> (the widget key), so a re-run refreshes each item's source + metadata
/// while preserving its <see cref="WidgetGalleryItem.Id"/> and <see cref="WidgetGalleryItem.InstallCount"/> — never
/// a duplicate, never an error. Does not call <c>SaveChanges</c> (the seed runner owns the single transaction).
/// </summary>
public sealed class FirstPartyWidgetCatalogueSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public FirstPartyWidgetCatalogueSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 10;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Ignore the soft-delete filter so a previously-deleted first-party row is refreshed + resurrected rather
        // than colliding with the unique NaturalKey index on a fresh insert.
        List<WidgetGalleryItem> existing = await _db
            .WidgetGalleryItems.IgnoreQueryFilters()
            .Where(item => item.NaturalKey != null)
            .ToListAsync(ct);

        Dictionary<string, WidgetGalleryItem> byKey = existing
            .Where(item => item.NaturalKey is not null)
            .GroupBy(item => item.NaturalKey!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (FirstPartyWidgetDefinition widget in FirstPartyWidgetCatalogue.All)
        {
            string source = ReadAsset(widget.Key);

            if (byKey.TryGetValue(widget.Key, out WidgetGalleryItem? row))
            {
                // Re-seed: refresh source + metadata in place, preserving Id and InstallCount.
                Apply(row, widget, source);
            }
            else
            {
                WidgetGalleryItem created = new() { NaturalKey = widget.Key };
                Apply(created, widget, source);
                _db.WidgetGalleryItems.Add(created);
            }
        }
    }

    // Copies the first-party metadata + source onto the row. Never touches Id or InstallCount; clears any prior
    // soft-delete so a resurrected first-party item is live again. The default settings + subscriptions are copied
    // (a fresh dictionary/list per row) so the shared static catalogue is never captured by an EF-tracked entity.
    private static void Apply(
        WidgetGalleryItem row,
        FirstPartyWidgetDefinition widget,
        string source
    )
    {
        row.Name = widget.Name;
        row.Description = widget.Description;
        row.Framework = "vue";
        row.SourceKind = "in_repo";
        row.TrustTier = "first_party";
        row.ReviewStatus = "verified";
        row.AvailableInSaaS = true;
        row.SubmitterUserId = null;
        row.SourceCode = source;
        row.DefaultSettings = new Dictionary<string, object>(widget.DefaultSettings);
        row.DefaultEventSubscriptions = [.. widget.DefaultEventSubscriptions];
        row.DeletedAt = null;
    }

    // Reads the embedded SFC source for a widget key. The manifest name follows the default derivation for the
    // Content/Widgets/Assets/*.vue embedded resources. Stream/StreamReader are fully qualified because
    // NomNomzBot.Domain.Stream shadows the System.IO.Stream type in this namespace.
    private static string ReadAsset(string key)
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        string resourceName = $"NomNomzBot.Infrastructure.Content.Widgets.Assets.{key}.vue";
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded first-party widget asset '{resourceName}' was not found in "
                    + $"{assembly.GetName().Name}."
            );
        using System.IO.StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
