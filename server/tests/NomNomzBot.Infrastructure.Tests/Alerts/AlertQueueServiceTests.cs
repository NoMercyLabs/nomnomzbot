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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Alerts.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Alerts.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Alerts.Services;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Tests.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Alerts;

/// <summary>
/// S059 (widgets-overlays.md §1.2): "the ONE alert queue across every platform connection" — proves the queue
/// exists independently of the widget gallery (Done-when 1), is genuinely cross-platform (Done-when 2), and
/// never claims delivery it did not achieve (Done-when 3, the overlay-only-output presence-check law TTS and
/// the widget test-button both paid to learn). <see cref="AlertQueueService"/> is exercised against a REAL
/// SQLite-backed <see cref="IApplicationDbContext"/> and a REAL <see cref="WidgetService"/> (so
/// <c>EnsureSystemWidgetAsync</c>'s get-or-create genuinely runs), with only the overlay-presence and
/// notifier seams substituted.
/// </summary>
public sealed class AlertQueueServiceTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewWidgetService(
        WidgetTestDbContext db,
        IOverlayPresenceRegistry presence
    )
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WidgetBuildOutput("BUNDLE", "hash", "")));
        return new(
            db,
            EmptyConfig,
            Substitute.For<IEventBus>(),
            build,
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            presence
        );
    }

    private static async Task SeedChannelAsync(WidgetSqliteTestDatabase database, Guid channelId)
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = channelId.ToString("N")[..12],
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = channelId.ToString("N"),
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedAlertsGalleryItemAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.WidgetGalleryItems.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                Name = "Alerts",
                Description = "System alert surface",
                Framework = "vue",
                TrustTier = "first_party",
                SourceKind = "in_repo",
                NaturalKey = "alerts",
                SourceCode = "ALERTS_SOURCE",
                ReviewStatus = "verified",
                AvailableInSaaS = true,
                DefaultEventSubscriptions = ["follow", "supporter.tip", "supporter.merch"],
                DefaultSettings = new() { ["durationMs"] = 6000 },
            }
        );
        await db.SaveChangesAsync();
    }

    /// <summary>A presence registry the test fully controls — real hub group logic is proven elsewhere.</summary>
    private sealed class FakePresenceRegistry : IOverlayPresenceRegistry
    {
        public bool Attached { get; set; }

        public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId) => Attached;
    }

    [Fact]
    public async Task Enqueue_writes_a_queued_alert_with_zero_widgets_ever_installed_manually()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedAlertsGalleryItemAsync(database);

        FakePresenceRegistry presence = new() { Attached = false };
        IWidgetEventNotifier notifier = Substitute.For<IWidgetEventNotifier>();

        await using WidgetTestDbContext db = database.NewContext();
        // No widget row exists yet for this channel at all — the streamer never browsed the gallery.
        (await db.Widgets.CountAsync(w => w.BroadcasterId == channel))
            .Should()
            .Be(0);

        AlertQueueService service = new(
            db,
            NewWidgetService(db, presence),
            presence,
            notifier,
            Clock
        );

        Result<AlertQueueEntryDto> result = await service.EnqueueAsync(
            channel,
            "patreon",
            "supporter.tip",
            new { user = "GenerousGoat", amount = 5.00m }
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Provider.Should().Be("patreon");
        result.Value.Kind.Should().Be("supporter.tip");
        result.Value.Status.Should().Be(AlertQueueStatus.Queued);

        // The row is real and durable — proven by re-reading it from a fresh context, not the same instance
        // that wrote it.
        await using WidgetTestDbContext verifyDb = database.NewContext();
        AlertQueueEntry stored = await verifyDb.AlertQueueEntries.SingleAsync(e =>
            e.BroadcasterId == channel
        );
        stored.Provider.Should().Be("patreon");
        stored.Status.Should().Be(AlertQueueStatus.Queued);

        // The alert surface was auto-provisioned as a side effect (never a manual gallery install) —
        // exactly one system widget row now exists, with Source stamped first_party like every other
        // auto-provisioned system surface.
        Widget surface = await verifyDb.Widgets.SingleAsync(w => w.BroadcasterId == channel);
        surface.Source.Should().Be("first_party");
    }

    [Fact]
    public async Task Two_different_providers_land_in_one_ordered_queue_each_attributed_to_its_own_provider()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedAlertsGalleryItemAsync(database);

        FakePresenceRegistry presence = new() { Attached = false };
        IWidgetEventNotifier notifier = Substitute.For<IWidgetEventNotifier>();

        await using WidgetTestDbContext db = database.NewContext();
        AlertQueueService service = new(
            db,
            NewWidgetService(db, presence),
            presence,
            notifier,
            Clock
        );

        await service.EnqueueAsync(channel, "patreon", "supporter.tip", new { user = "A" });
        Clock.Advance(TimeSpan.FromSeconds(1));
        await service.EnqueueAsync(channel, "shopify", "supporter.merch", new { user = "B" });

        Result<AlertQueueDto> queue = await service.GetQueueAsync(channel);

        queue.IsSuccess.Should().BeTrue(queue.ErrorMessage);
        // ONE queue, not two per-platform lists — both providers show up in the same result set.
        queue.Value.Entries.Should().HaveCount(2);
        queue.Value.Entries.Select(e => e.Provider).Should().BeEquivalentTo(["patreon", "shopify"]);
        // Most-recent-first ordering, and each row keeps its own provider — a Kick sub and a Patreon
        // pledge are never merged or re-labelled into a single generic "alert" origin.
        queue.Value.Entries[0].Provider.Should().Be("shopify");
        queue.Value.Entries[0].Kind.Should().Be("supporter.merch");
        queue.Value.Entries[1].Provider.Should().Be("patreon");
        queue.Value.Entries[1].Kind.Should().Be("supporter.tip");
    }

    [Fact]
    public async Task No_overlay_connected_reports_queued_never_delivered_and_never_pushes()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedAlertsGalleryItemAsync(database);

        FakePresenceRegistry presence = new() { Attached = false };
        IWidgetEventNotifier notifier = Substitute.For<IWidgetEventNotifier>();

        await using WidgetTestDbContext db = database.NewContext();
        AlertQueueService service = new(
            db,
            NewWidgetService(db, presence),
            presence,
            notifier,
            Clock
        );

        Result<AlertQueueEntryDto> result = await service.EnqueueAsync(
            channel,
            "treatstream",
            "supporter.tip",
            new { user = "NoOneIsWatching" }
        );

        result.Value.Status.Should().Be(AlertQueueStatus.Queued);
        result.Value.DeliveredAt.Should().BeNull();
        await notifier
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default, default, default!, default, default);

        Result<AlertQueueDto> queue = await service.GetQueueAsync(channel);
        queue.Value.OverlayConnected.Should().BeFalse();
    }

    [Fact]
    public async Task A_connected_overlay_actually_receives_the_push_and_only_then_is_marked_delivered()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedAlertsGalleryItemAsync(database);

        FakePresenceRegistry presence = new() { Attached = true };
        IWidgetEventNotifier notifier = Substitute.For<IWidgetEventNotifier>();

        await using WidgetTestDbContext db = database.NewContext();
        AlertQueueService service = new(
            db,
            NewWidgetService(db, presence),
            presence,
            notifier,
            Clock
        );

        Result<AlertQueueEntryDto> result = await service.EnqueueAsync(
            channel,
            "twitch",
            "follow",
            new { user = "PogChamp42" }
        );

        result.Value.Status.Should().Be(AlertQueueStatus.Delivered);
        result.Value.DeliveredAt.Should().NotBeNull();
        await notifier
            .Received(1)
            .SendWidgetEventAsync(
                channel,
                Arg.Any<Guid>(),
                "follow",
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );

        Result<AlertQueueDto> queue = await service.GetQueueAsync(channel);
        queue.Value.OverlayConnected.Should().BeTrue();
    }
}
