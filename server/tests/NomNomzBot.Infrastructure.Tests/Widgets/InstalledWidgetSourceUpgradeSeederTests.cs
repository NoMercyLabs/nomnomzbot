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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Content.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A first-party widget the channel never edited must follow the catalogue: the seeder pulls the current source
/// into every stale, untouched install (the raid recap on the live box kept an older copy because the platform
/// never rebuilt an installed widget). A widget with its own saved version, or one already current, is left alone.
/// </summary>
public sealed class InstalledWidgetSourceUpgradeSeederTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static Widget NewWidget(Guid itemId, int installedRevision, int? catalogueVersion) =>
        new()
        {
            BroadcasterId = Broadcaster,
            Name = "Raid Recap",
            Framework = "vue",
            Source = "first_party",
            GalleryItemId = itemId,
            InstalledSourceRevision = installedRevision,
            CatalogueVersionNumber = catalogueVersion,
        };

    private static WidgetVersion NewVersion(Widget widget, int number) =>
        new()
        {
            WidgetId = widget.Id,
            BroadcasterId = Broadcaster,
            VersionNumber = number,
        };

    [Fact]
    public async Task Upgrades_only_stale_untouched_first_party_installs()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Widget stale = NewWidget(Guid.Empty, 1, 1);
        Widget customized = NewWidget(Guid.Empty, 1, 1);
        Widget current = NewWidget(Guid.Empty, 2, 1);
        await using (WidgetTestDbContext db = database.NewContext())
        {
            await new FirstPartyWidgetCatalogueSeeder(db).SeedAsync();
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
            WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i =>
                i.NaturalKey == "raid_recap"
            );
            item.SourceRevision = 2;
            foreach (Widget widget in new[] { stale, customized, current })
                widget.GalleryItemId = item.Id;
            db.Widgets.AddRange(stale, customized, current);
            await db.SaveChangesAsync();
            db.WidgetVersions.AddRange(
                NewVersion(stale, 1),
                NewVersion(customized, 1),
                NewVersion(customized, 2),
                NewVersion(current, 1)
            );
            await db.SaveChangesAsync();
        }

        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .UpdateFromGalleryAsync(default!, default!)
            .ReturnsForAnyArgs(Task.FromResult(Result.Failure<WidgetDetail>("stub", "STUB")));

        await using (WidgetTestDbContext db = database.NewContext())
            await new InstalledWidgetSourceUpgradeSeeder(db, widgets).SeedAsync();

        await widgets
            .Received(1)
            .UpdateFromGalleryAsync(
                Broadcaster.ToString(),
                stale.Id.ToString(),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .DidNotReceive()
            .UpdateFromGalleryAsync(
                Arg.Any<string>(),
                Arg.Is<string>(id => id != stale.Id.ToString()),
                Arg.Any<CancellationToken>()
            );
    }
}
