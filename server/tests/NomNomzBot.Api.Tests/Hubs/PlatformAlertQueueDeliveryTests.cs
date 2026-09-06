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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Alerts.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Alerts.Entities;
using NomNomzBot.Infrastructure.Alerts.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// S059b (widgets-overlays.md §1.2): platform-native alerts (follow/sub/cheer/raid/gift/resub — routed
/// through the shared <see cref="OverlayAlertBroadcast"/> choke point) now join the SAME cross-platform alert
/// queue a supporter event writes to, on the SAME presence-checked delivery path
/// (<see cref="SupporterWidgetEventHandler"/> is the pattern followed here). Exercises the REAL
/// <see cref="AlertQueueService"/> against a REAL SQLite-backed <see cref="WidgetTestDbContext"/> — only the
/// overlay-presence registry and the alerts-surface resolution are substituted — so a "double delivery" or a
/// "false delivered" claim would be a genuine bug, not a mock artifact.
/// </summary>
public sealed class PlatformAlertQueueDeliveryTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));

    private sealed record Fixture(
        WidgetTestDbContext Db,
        Guid Channel,
        Guid AlertsWidgetId,
        Guid CustomWidgetId,
        IAlertQueueService AlertQueue,
        IWidgetService WidgetService,
        IWidgetNotifier DirectNotifier,
        List<Guid> PushedWidgetIds
    );

    /// <summary>
    /// Seeds the auto-provisioned "alerts" system surface (subscribed to "follow"/"subscription", like the
    /// real seeder's <c>DefaultEventSubscriptions</c>) PLUS an ordinary custom widget also subscribed to both —
    /// standing in for a streamer who has installed their own follow/sub alert widget alongside the built-in
    /// surface. Wires a REAL <see cref="AlertQueueService"/> whose <see cref="IWidgetEventNotifier"/> and the
    /// direct-push <see cref="IWidgetNotifier"/> both record into the SAME list, so double delivery to one
    /// widget id is visible as a count > 1 regardless of which of the two push paths caused it.
    /// </summary>
    private static async Task<Fixture> BuildAsync(bool overlayConnected)
    {
        WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid alertsWidgetId = Guid.CreateVersion7();
        Guid customWidgetId = Guid.CreateVersion7();

        db.Widgets.Add(
            new()
            {
                Id = alertsWidgetId,
                BroadcasterId = channel,
                Name = "Alerts",
                Source = "first_party",
                IsEnabled = true,
                EventSubscriptions = ["follow", "subscription"],
            }
        );
        db.Widgets.Add(
            new()
            {
                Id = customWidgetId,
                BroadcasterId = channel,
                Name = "My follow overlay",
                IsEnabled = true,
                EventSubscriptions = ["follow", "subscription"],
            }
        );
        await db.SaveChangesAsync();

        List<Guid> pushedWidgetIds = [];

        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .EnsureSystemWidgetAsync(channel.ToString(), "alerts", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetDetail(
                        alertsWidgetId,
                        "Alerts",
                        null,
                        "vue",
                        "first_party",
                        true,
                        null,
                        null,
                        null,
                        new(),
                        ["follow", "subscription"],
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow,
                        false,
                        overlayConnected
                    )
                )
            );

        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(channel, alertsWidgetId).Returns(overlayConnected);

        IWidgetEventNotifier queueDeliveryNotifier = Substitute.For<IWidgetEventNotifier>();
        queueDeliveryNotifier
            .When(n =>
                n.SendWidgetEventAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(ci => pushedWidgetIds.Add(ci.ArgAt<Guid>(1)));

        IAlertQueueService alertQueue = new AlertQueueService(
            db,
            widgetService,
            presence,
            queueDeliveryNotifier,
            Clock
        );

        IWidgetNotifier directNotifier = Substitute.For<IWidgetNotifier>();
        directNotifier
            .When(n =>
                n.SendWidgetEventAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<WidgetEventDto>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(ci => pushedWidgetIds.Add(Guid.Parse(ci.ArgAt<string>(1))));

        return new(
            db,
            channel,
            alertsWidgetId,
            customWidgetId,
            alertQueue,
            widgetService,
            directNotifier,
            pushedWidgetIds
        );
    }

    [Fact]
    public async Task Follow_writes_an_alert_queue_entry_attributed_to_its_platform()
    {
        Fixture f = await BuildAsync(overlayConnected: false);
        await using WidgetTestDbContext _ = f.Db;

        await OverlayAlertBroadcast.ToOverlaysAsync(
            f.Db,
            f.DirectNotifier,
            f.AlertQueue,
            f.WidgetService,
            f.Channel,
            "twitch",
            "follow",
            new { user = "PogChamp42" },
            channelEventId: "evt-1",
            cancellationToken: CancellationToken.None
        );

        // Exactly the same shape a supporter event writes: one queue row, attributed to its source.
        AlertQueueEntry stored = await f.Db.AlertQueueEntries.SingleAsync(e =>
            e.BroadcasterId == f.Channel
        );
        stored.Provider.Should().Be("twitch");
        stored.Kind.Should().Be("follow");
    }

    [Fact]
    public async Task Connected_overlay_receives_exactly_one_delivery_never_two()
    {
        Fixture f = await BuildAsync(overlayConnected: true);
        await using WidgetTestDbContext _ = f.Db;

        await OverlayAlertBroadcast.ToOverlaysAsync(
            f.Db,
            f.DirectNotifier,
            f.AlertQueue,
            f.WidgetService,
            f.Channel,
            "twitch",
            "follow",
            new { user = "PogChamp42" },
            channelEventId: "evt-2",
            cancellationToken: CancellationToken.None
        );

        // The alerts surface is reachable through BOTH the queue's own delivery attempt and the shared
        // widget fan-out below it — a real double-delivery bug shows up here as a count of 2.
        f.PushedWidgetIds.Count(id => id == f.AlertsWidgetId).Should().Be(1);

        AlertQueueEntry stored = await f.Db.AlertQueueEntries.SingleAsync(e =>
            e.BroadcasterId == f.Channel
        );
        stored.Status.Should().Be(AlertQueueStatus.Delivered);
        stored.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task No_overlay_connected_reports_queued_never_delivered_on_the_native_path_too()
    {
        Fixture f = await BuildAsync(overlayConnected: false);
        await using WidgetTestDbContext _ = f.Db;

        await OverlayAlertBroadcast.ToOverlaysAsync(
            f.Db,
            f.DirectNotifier,
            f.AlertQueue,
            f.WidgetService,
            f.Channel,
            "twitch",
            "follow",
            new { user = "NoOneIsWatching" },
            channelEventId: "evt-3",
            cancellationToken: CancellationToken.None
        );

        AlertQueueEntry stored = await f.Db.AlertQueueEntries.SingleAsync(e =>
            e.BroadcasterId == f.Channel
        );
        stored.Status.Should().Be(AlertQueueStatus.Queued);
        stored.DeliveredAt.Should().BeNull();
        // Never claimed delivered to the alerts surface either — the overlay-only-output presence-check
        // law applies to the native path exactly as it does to a supporter event.
        f.PushedWidgetIds.Should().NotContain(f.AlertsWidgetId);
    }

    [Fact]
    public async Task An_existing_custom_widget_still_receives_its_alert_regardless_of_the_alerts_surface()
    {
        // Whether the built-in alerts surface is connected or not, a streamer's own custom widget
        // subscribed to "follow" must keep receiving its push exactly as before this change — this is the
        // regression guard: closing the platform-alert queue gap must never silently break every alert
        // overlay already in the field.
        Fixture f = await BuildAsync(overlayConnected: false);
        await using WidgetTestDbContext _ = f.Db;

        await OverlayAlertBroadcast.ToOverlaysAsync(
            f.Db,
            f.DirectNotifier,
            f.AlertQueue,
            f.WidgetService,
            f.Channel,
            "twitch",
            "follow",
            new { user = "PogChamp42" },
            channelEventId: "evt-4",
            cancellationToken: CancellationToken.None
        );

        f.PushedWidgetIds.Count(id => id == f.CustomWidgetId).Should().Be(1);
    }
}
