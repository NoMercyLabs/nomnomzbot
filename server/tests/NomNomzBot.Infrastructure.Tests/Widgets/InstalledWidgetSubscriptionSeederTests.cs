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
/// Owner report 2026-09-30: a message deleted on Twitch stayed on the chat overlay. The installed chat_box row
/// still held the July default <c>["ChatMessage"]</c>, so the subscription-routed <c>MessageDeleted</c> event never
/// reached it (the 09-09 backfill matched only the later default string). These tests prove the seeder brings every
/// installed first-party widget up to its catalogue's default subscriptions — additively, never dropping one.
/// </summary>
public sealed class InstalledWidgetSubscriptionSeederTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static async Task<Guid> SeedCatalogueAndChannelAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
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
        return await db
            .WidgetGalleryItems.Where(item => item.NaturalKey == "chat_box")
            .Select(item => item.Id)
            .SingleAsync();
    }

    private static Widget NewWidget(Guid? galleryItemId, params string[] subscriptions) =>
        new()
        {
            BroadcasterId = Broadcaster,
            Name = "Chat Box",
            Framework = "vue",
            Source = galleryItemId is null ? "custom" : "first_party",
            GalleryItemId = galleryItemId,
            EventSubscriptions = [.. subscriptions],
        };

    private static async Task RunSeederAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
        await new InstalledWidgetSubscriptionSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
    }

    private static async Task<List<string>> SubscriptionsOfAsync(
        WidgetSqliteTestDatabase database,
        Guid widgetId
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        Widget widget = await db.Widgets.SingleAsync(w => w.Id == widgetId);
        return widget.EventSubscriptions;
    }

    [Fact]
    public async Task An_install_holding_an_older_default_gains_the_moderation_events_and_keeps_its_own()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid chatBoxItemId = await SeedCatalogueAndChannelAsync(database);
        Widget legacy = NewWidget(chatBoxItemId, "ChatMessage");
        Widget customized = NewWidget(chatBoxItemId, "ChatMessage", "follow");
        await using (WidgetTestDbContext db = database.NewContext())
        {
            db.Widgets.AddRange(legacy, customized);
            await db.SaveChangesAsync();
        }

        await RunSeederAsync(database);

        string[] catalogueDefault =
        [
            "ChatMessage",
            "ChatMessageEnriched",
            "ChatCleared",
            "MessageDeleted",
            "UserMessagesCleared",
        ];
        (await SubscriptionsOfAsync(database, legacy.Id)).Should().Equal(catalogueDefault);
        (await SubscriptionsOfAsync(database, customized.Id))
            .Should()
            .Equal(
                "ChatMessage",
                "follow",
                "ChatMessageEnriched",
                "ChatCleared",
                "MessageDeleted",
                "UserMessagesCleared"
            );
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid chatBoxItemId = await SeedCatalogueAndChannelAsync(database);
        Widget legacy = NewWidget(chatBoxItemId, "ChatMessage");
        await using (WidgetTestDbContext db = database.NewContext())
        {
            db.Widgets.Add(legacy);
            await db.SaveChangesAsync();
        }

        await RunSeederAsync(database);
        List<string> afterFirst = await SubscriptionsOfAsync(database, legacy.Id);
        await RunSeederAsync(database);

        (await SubscriptionsOfAsync(database, legacy.Id)).Should().Equal(afterFirst);
    }

    [Fact]
    public async Task A_fork_and_a_community_install_are_left_alone()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedCatalogueAndChannelAsync(database);
        WidgetGalleryItem community = new()
        {
            NaturalKey = null,
            Name = "Community Chat",
            Framework = "vue",
            TrustTier = "community",
            SourceKind = "github",
            ReviewStatus = "verified",
            DefaultSettings = [],
            DefaultEventSubscriptions = ["ChatMessage", "MessageDeleted"],
        };
        Widget fork = NewWidget(null, "ChatMessage");
        Widget communityInstall = NewWidget(community.Id, "ChatMessage");
        await using (WidgetTestDbContext db = database.NewContext())
        {
            db.WidgetGalleryItems.Add(community);
            db.Widgets.AddRange(fork, communityInstall);
            await db.SaveChangesAsync();
        }

        await RunSeederAsync(database);

        (await SubscriptionsOfAsync(database, fork.Id)).Should().Equal("ChatMessage");
        (await SubscriptionsOfAsync(database, communityInstall.Id)).Should().Equal("ChatMessage");
    }
}
