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
/// <see cref="WidgetGalleryItem.SourceCode"/> so a channel can install or clone-to-edit it. Idempotent: upserts by
/// <see cref="WidgetGalleryItem.NaturalKey"/> (the widget key), so a re-run refreshes each item's source + metadata
/// while preserving its <see cref="WidgetGalleryItem.Id"/> and <see cref="WidgetGalleryItem.InstallCount"/> — never
/// a duplicate, never an error. Does not call <c>SaveChanges</c> (the seed runner owns the single transaction).
/// </summary>
public sealed class FirstPartyWidgetCatalogueSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public FirstPartyWidgetCatalogueSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 10;

    /// <summary>The immutable per-widget metadata; <see cref="WidgetGalleryItem.SourceCode"/> is read at seed time
    /// from the embedded SFC asset keyed by <see cref="Key"/>.</summary>
    private sealed record FirstPartyWidget(
        string Key,
        string Name,
        string Description,
        Dictionary<string, object> DefaultSettings,
        List<string> DefaultEventSubscriptions
    );

    /// <summary>Every Twitch + supporter alert type the alerts widget and ticker default to listening for.
    /// Declared before <see cref="Widgets"/> so its value is initialized when the catalogue references it (static
    /// field initializers run in textual order).</summary>
    private static readonly string[] SupporterAndTwitchEvents =
    [
        "follow",
        "subscription",
        "resub",
        "gift",
        "cheer",
        "raid",
        "supporter.tip",
        "supporter.membership",
        "supporter.merch",
        "supporter.charity",
    ];

    private static readonly IReadOnlyList<FirstPartyWidget> Widgets =
    [
        new(
            Key: "alerts",
            Name: "Alerts",
            Description: "A one-at-a-time queue of animated alert cards for follows, subs, resubs, gift subs, cheers, "
                + "raids, and supporter events, with per-type amount thresholds and a custom text template.",
            DefaultSettings: new()
            {
                ["events"] = new List<string>(SupporterAndTwitchEvents),
                ["textTemplate"] = "",
                ["durationMs"] = 6000,
                ["minBits"] = 0,
                ["minGiftCount"] = 0,
                ["minAmount"] = 0,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: new(SupporterAndTwitchEvents)
        ),
        new(
            Key: "goal_bar",
            Name: "Goal Bar",
            Description: "An animated progress bar toward a follower, sub, or bits goal. Reads the authoritative total from "
                + "goal events and live-increments on matching follows, subs, gifts, and cheers.",
            DefaultSettings: new()
            {
                ["metric"] = "followers",
                ["target"] = 100,
                ["start"] = 0,
                ["resetCadence"] = "",
                ["colors"] = new Dictionary<string, object>(),
                ["labels"] = new Dictionary<string, object>(),
            },
            DefaultEventSubscriptions: ["goal", "follow", "subscription", "gift", "cheer"]
        ),
        new(
            Key: "labels",
            Name: "Labels",
            Description: "A single live stat rendered as styled text — latest follower, latest sub, top cheerer, follower "
                + "count, or sub count — with an optional format string.",
            DefaultSettings: new()
            {
                ["label"] = "latest_follower",
                ["formatString"] = "",
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["follow", "subscription", "resub", "gift", "cheer", "goal"]
        ),
        new(
            Key: "drop_game",
            Name: "Drop Game",
            Description: "The live drop-game round on a horizontal track: the target zone, every chatter's landing "
                + "marker as they type !drop, and the payout scoreboard when the round resolves.",
            DefaultSettings: new() { ["accentColor"] = "#9146ff", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "event_ticker",
            Name: "Event Ticker",
            Description: "A horizontal scrolling ticker of recent channel events as compact chips, newest appended and the "
                + "oldest retired past a retained count, at a configurable speed.",
            DefaultSettings: new()
            {
                ["events"] = new List<string>(SupporterAndTwitchEvents),
                ["speed"] = 60,
                ["count"] = 20,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: new(SupporterAndTwitchEvents)
        ),
    ];

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

        foreach (FirstPartyWidget widget in Widgets)
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
    // soft-delete so a resurrected first-party item is live again.
    private static void Apply(WidgetGalleryItem row, FirstPartyWidget widget, string source)
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
        row.DefaultSettings = widget.DefaultSettings;
        row.DefaultEventSubscriptions = widget.DefaultEventSubscriptions;
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
