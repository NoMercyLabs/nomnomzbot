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
/// A raid recap installed with the old 20 second hold and never edited follows the new 60 second default exactly
/// once. A recap whose streamer changed any setting, or whose hold is already their own number, is left alone, and
/// a streamer who later types 20 again is never moved back.
/// </summary>
public sealed class RaidRecapHoldDefaultSeederTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static Dictionary<string, object> OldDefaults()
    {
        Dictionary<string, object> settings = new(
            FirstPartyWidgetCatalogue.All.Single(w => w.Key == "raid_recap").DefaultSettings
        )
        {
            ["holdSeconds"] = 20,
        };
        return settings;
    }

    private static async Task<Guid> InstallAsync(
        WidgetSqliteTestDatabase database,
        Dictionary<string, object> settings
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        await new FirstPartyWidgetCatalogueSeeder(db).SeedAsync();
        if (!await db.Channels.AnyAsync())
        {
            db.Channels.Add(
                new()
                {
                    Id = Broadcaster,
                    OwnerUserId = Guid.CreateVersion7(),
                    TwitchChannelId = Broadcaster.ToString("N")[..12],
                    Name = "teststreamer",
                    NameNormalized = "teststreamer",
                    OverlayToken = Broadcaster.ToString("N"),
                }
            );
            await db.SaveChangesAsync();
        }
        WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i =>
            i.NaturalKey == "raid_recap"
        );
        Widget widget = new()
        {
            BroadcasterId = Broadcaster,
            Name = "Raid Recap",
            Framework = "vue",
            Source = "first_party",
            GalleryItemId = item.Id,
            Settings = settings,
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget.Id;
    }

    private static async Task<Widget> ReadAsync(WidgetSqliteTestDatabase database, Guid id)
    {
        await using WidgetTestDbContext db = database.NewContext();
        return await db.Widgets.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    private static async Task SeedAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
        await new RaidRecapHoldDefaultSeeder(db).SeedAsync();
    }

    [Fact]
    public async Task An_unedited_recap_with_the_old_hold_moves_to_sixty_once()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid id = await InstallAsync(database, OldDefaults());

        await SeedAsync(database);

        Widget widget = await ReadAsync(database, id);
        Convert.ToInt32(widget.Settings["holdSeconds"].ToString()).Should().Be(60);
        widget.ConfigSchemaVersion.Should().Be(2);
    }

    [Fact]
    public async Task A_recap_with_another_edited_setting_keeps_its_hold()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Dictionary<string, object> edited = OldDefaults();
        edited["showBits"] = false;
        Guid id = await InstallAsync(database, edited);

        await SeedAsync(database);

        Widget widget = await ReadAsync(database, id);
        Convert.ToInt32(widget.Settings["holdSeconds"].ToString()).Should().Be(20);
        widget.ConfigSchemaVersion.Should().Be(2, "the decision is made once");
    }

    [Fact]
    public async Task A_recap_with_its_own_hold_number_is_left_alone()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Dictionary<string, object> own = OldDefaults();
        own["holdSeconds"] = 45;
        Guid id = await InstallAsync(database, own);

        await SeedAsync(database);

        Widget widget = await ReadAsync(database, id);
        Convert.ToInt32(widget.Settings["holdSeconds"].ToString()).Should().Be(45);
    }

    [Fact]
    public async Task A_hold_the_streamer_sets_back_to_twenty_after_the_move_stays_twenty()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid id = await InstallAsync(database, OldDefaults());
        await SeedAsync(database);

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Widget widget = await db.Widgets.SingleAsync(w => w.Id == id);
            widget.Settings = OldDefaults();
            await db.SaveChangesAsync();
        }

        await SeedAsync(database);

        Widget after = await ReadAsync(database, id);
        Convert.ToInt32(after.Settings["holdSeconds"].ToString()).Should().Be(20);
    }
}
