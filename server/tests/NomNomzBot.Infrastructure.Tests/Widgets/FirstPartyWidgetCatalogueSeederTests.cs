// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the first-party widget catalogue seeder writes the fourteen first-party overlay widgets (the thirteen
/// spec §1.1 items plus <c>drop_game</c>) as global gallery items — each a verified, SaaS-available <c>vue</c>
/// in-repo item carrying its real SFC source — and that a re-seed is idempotent: it refreshes source/metadata in
/// place while preserving each row's <c>Id</c> and <c>InstallCount</c>, never duplicating. Runs on the real
/// relational SQLite harness with the production <see cref="WidgetGalleryItemConfiguration"/> so the JSON
/// converters on the settings/subscription columns are exercised.
/// </summary>
public sealed class FirstPartyWidgetCatalogueSeederTests
{
    private static readonly string[] ExpectedKeys =
    [
        "alerts",
        "goal_bar",
        "labels",
        "drop_game",
        "event_ticker",
        "chat_box",
        "now_playing",
        "sr_queue",
        "tts_caption",
        "poll_prediction",
        "redemption_alert",
        "countdown_timer",
        "emote_wall",
        "custom_data",
    ];

    private static async Task SeedAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
        await new FirstPartyWidgetCatalogueSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Seeds_the_fourteen_first_party_vue_widgets_with_their_real_source()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();

        await SeedAsync(database);

        await using WidgetTestDbContext db = database.NewContext();
        List<WidgetGalleryItem> items = await db.WidgetGalleryItems.ToListAsync();

        items.Should().HaveCount(14);
        items.Select(i => i.NaturalKey).Should().BeEquivalentTo(ExpectedKeys);

        // Every seeded item is a verified, SaaS-available, platform-owned in-repo Vue widget with real source.
        items.Should().OnlyContain(i => i.Framework == "vue");
        items.Should().OnlyContain(i => i.TrustTier == "first_party");
        items.Should().OnlyContain(i => i.SourceKind == "in_repo");
        items.Should().OnlyContain(i => i.ReviewStatus == "verified");
        items.Should().OnlyContain(i => i.AvailableInSaaS);
        items.Should().OnlyContain(i => i.SubmitterUserId == null);
        items.Should().OnlyContain(i => i.InstallCount == 0);
        items
            .Should()
            .OnlyContain(i => !string.IsNullOrWhiteSpace(i.Name) && i.Description != null);

        // The source is the actual embedded SFC, not a placeholder.
        WidgetGalleryItem alerts = items.Single(i => i.NaturalKey == "alerts");
        alerts.SourceCode.Should().NotBeNullOrWhiteSpace();
        alerts.SourceCode!.Should().Contain("<script setup").And.Contain("<template>");

        // The item's declared config keys are all present in its DefaultSettings.
        alerts
            .DefaultSettings.Keys.Should()
            .Contain(
                new[]
                {
                    "events",
                    "textTemplate",
                    "durationMs",
                    "minBits",
                    "minGiftCount",
                    "minAmount",
                    "accentColor",
                }
            );

        // The default event subscriptions carry the alert types the widget renders.
        alerts
            .DefaultEventSubscriptions.Should()
            .Contain("follow")
            .And.Contain("cheer")
            .And.Contain("supporter.tip");
    }

    [Fact]
    public async Task Re_seed_is_idempotent_and_preserves_id_and_install_count()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();

        await SeedAsync(database);

        // Capture the seeded ids and simulate real installs bumping the count on one item.
        Dictionary<string, Guid> idsByKey;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            List<WidgetGalleryItem> seeded = await db.WidgetGalleryItems.ToListAsync();
            idsByKey = seeded.ToDictionary(i => i.NaturalKey!, i => i.Id);

            WidgetGalleryItem alerts = seeded.Single(i => i.NaturalKey == "alerts");
            alerts.InstallCount = 7;
            await db.SaveChangesAsync();
        }

        // Re-run the seeder over the already-seeded catalogue.
        await SeedAsync(database);

        await using WidgetTestDbContext read = database.NewContext();
        List<WidgetGalleryItem> after = await read.WidgetGalleryItems.ToListAsync();

        // No duplicates — still exactly one row per natural key, with the same ids.
        after.Should().HaveCount(14);
        after.ToDictionary(i => i.NaturalKey!, i => i.Id).Should().BeEquivalentTo(idsByKey);

        // The install count set between seeds survived the re-seed (metadata refreshed, counters preserved).
        after.Single(i => i.NaturalKey == "alerts").InstallCount.Should().Be(7);
    }

    [Fact]
    public void Orders_with_global_reference_data()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        using WidgetTestDbContext db = database.NewContext();

        new FirstPartyWidgetCatalogueSeeder(db).Order.Should().Be(10);
    }
}
