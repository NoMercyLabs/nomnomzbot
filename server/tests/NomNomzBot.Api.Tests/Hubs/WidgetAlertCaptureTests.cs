// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <c>WidgetAlertDispatch.RouteAsync</c> captures the exact widget-event push it makes — the
/// S-REPLAY-CAPTURE foundation a later replay endpoint re-broadcasts verbatim (never re-deriving a
/// persistent side effect like a currency grant). <see cref="WidgetNowPlayingHandlerTests"/> exercises
/// the same dispatch path; this suite asserts on the captured row's own content, not just its existence.
/// </summary>
public sealed class WidgetAlertCaptureTests
{
    [Fact]
    public async Task Dispatch_to_a_subscribed_widget_captures_the_exact_event_type_and_payload_pushed()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget alertBox = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Alert box",
            IsEnabled = true,
            EventSubscriptions = ["follow"],
        };
        db.Widgets.Add(alertBox);
        await db.SaveChangesAsync();

        await WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            channel,
            "follow",
            new { user = "PogChamp42", followedAt = "2026-08-29T12:00:00Z" },
            CancellationToken.None
        );

        RenderedAlertCapture capture = await db.RenderedAlertCaptures.SingleAsync(c =>
            c.BroadcasterId == channel
        );
        capture.EventType.Should().Be("follow");

        JsonElement payload = JsonSerializer.SerializeToElement(
            JsonSerializer.Deserialize<JsonElement>(capture.Payload),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        payload.GetProperty("user").GetString().Should().Be("PogChamp42");
        payload.GetProperty("followedAt").GetString().Should().Be("2026-08-29T12:00:00Z");
    }

    [Fact]
    public async Task Dispatch_with_no_subscribing_widget_captures_nothing()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();

        await WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            channel,
            "follow",
            new { user = "Unheard" },
            CancellationToken.None
        );

        (await db.RenderedAlertCaptures.CountAsync(c => c.BroadcasterId == channel)).Should().Be(0);
    }

    [Fact]
    public async Task Captures_beyond_the_activity_feed_recency_window_are_pruned_on_write()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget alertBox = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Alert box",
            IsEnabled = true,
            EventSubscriptions = ["follow"],
        };
        db.Widgets.Add(alertBox);
        await db.SaveChangesAsync();

        // One more than the 40-row window the dashboard activity feed surfaces.
        for (int i = 0; i < 41; i++)
            await WidgetAlertDispatch.RouteAsync(
                db,
                widgets,
                channel,
                "follow",
                new { user = $"user{i}" },
                CancellationToken.None
            );

        int remaining = await db.RenderedAlertCaptures.CountAsync(c => c.BroadcasterId == channel);
        remaining.Should().Be(40);

        // The oldest capture (user0) is the one pruned; the newest (user40) survives.
        bool anyUser0 = await db.RenderedAlertCaptures.AnyAsync(c =>
            c.BroadcasterId == channel && c.Payload.Contains("user0\"")
        );
        anyUser0.Should().BeFalse();
        bool anyUser40 = await db.RenderedAlertCaptures.AnyAsync(c =>
            c.BroadcasterId == channel && c.Payload.Contains("user40")
        );
        anyUser40.Should().BeTrue();
    }
}
