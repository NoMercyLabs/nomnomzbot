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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Entities;
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
            channelEventId: null,
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
            channelEventId: null,
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
                channelEventId: null,
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

    /// <summary>
    /// The regression this guards: a shared per-broadcaster ring buffer that also captured "ChatMessage" filled
    /// its 40-slot window with ordinary chat traffic within seconds on any active channel, evicting the rare,
    /// valuable alert (a follow/sub/raid) a streamer actually wanted to replay before they ever clicked Replay
    /// — reported live as "it was not just this event that refused, it was all of them". ChatMessage is never
    /// captured at all now, however many widgets subscribe to it, so a real alert survives.
    /// </summary>
    [Fact]
    public async Task ChatMessage_is_never_captured_even_with_a_subscribed_widget_and_a_real_correlating_id()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        db.Widgets.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "Chat box",
                IsEnabled = true,
                EventSubscriptions = ["ChatMessage"],
            }
        );
        await db.SaveChangesAsync();

        // A hundred chat messages, each with a real correlating id, must not consume a single capture slot.
        for (int i = 0; i < 100; i++)
            await WidgetAlertDispatch.RouteAsync(
                db,
                widgets,
                channel,
                "ChatMessage",
                new { message = $"msg{i}" },
                channelEventId: Guid.NewGuid().ToString(),
                CancellationToken.None
            );

        (await db.RenderedAlertCaptures.CountAsync(c => c.BroadcasterId == channel)).Should().Be(0);
    }

    /// <summary>S-REPLAY-CORRELATION's done-when proof: a real alert (FollowEvent, routed through the actual
    /// FollowBroadcastHandler — not a raw RouteAsync call) whose EventId matches a real, independently-seeded
    /// ChannelEvent row (the same convergent id TwitchAlertHandlerBase/TwitchChannelEventLogProjection key their
    /// rows by) produces a RenderedAlertCapture that is queryable BY THAT CHANNELEVENT.ID — a real join against
    /// the ChannelEvents table, not string equality in isolation — and carries the right payload. Type+recency
    /// alone could never answer "which capture came from THIS activity-feed item"; this proves the exact-id path.
    /// </summary>
    [Fact]
    public async Task Follow_alert_capture_is_queryable_by_the_ChannelEvent_row_that_produced_it()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid followEventId = Guid.CreateVersion7();
        string channelEventId = followEventId.ToString();

        // The activity-feed row the replay button will live next to (DashboardController.GetActivity),
        // written independently of the broadcast handler — exactly as TwitchAlertHandlerBase does today,
        // keyed by the SAME domain-event EventId.
        db.ChannelEvents.Add(
            new()
            {
                Id = channelEventId,
                ChannelId = channel,
                Type = "channel.follow",
                Data = "{}",
            }
        );
        db.Widgets.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "Alert box",
                IsEnabled = true,
                EventSubscriptions = ["follow"],
            }
        );
        await db.SaveChangesAsync();

        FollowBroadcastHandler handler = new(
            Substitute.For<IDashboardNotifier>(),
            Substitute.For<IHubUserEnricher>(),
            db,
            Substitute.For<IWidgetNotifier>()
        );
        FollowEvent followEvent = new()
        {
            EventId = followEventId,
            BroadcasterId = channel,
            UserId = "999",
            UserDisplayName = "PogChamp42",
            UserLogin = "pogchamp42",
            FollowedAt = DateTimeOffset.UtcNow,
        };

        await handler.HandleAsync(followEvent, CancellationToken.None);

        // The lookup replay needs: given the ChannelEvent.Id from the activity feed, find its capture(s).
        // Joins through the real ChannelEvents table, not a string comparison against a literal.
        ChannelEvent feedRow = await db.ChannelEvents.SingleAsync(e => e.ChannelId == channel);
        RenderedAlertCapture capture = await db.RenderedAlertCaptures.SingleAsync(c =>
            c.BroadcasterId == channel && c.ChannelEventId == feedRow.Id
        );
        capture.EventType.Should().Be("follow");

        JsonElement payload = JsonSerializer.SerializeToElement(
            JsonSerializer.Deserialize<JsonElement>(capture.Payload),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        payload.GetProperty("DisplayName").GetString().Should().Be("PogChamp42");

        // Same lookup surfaced by ChannelEventId alone (what the replay endpoint actually queries by) — not
        // type+recency, which is the gap this slice closes.
        (await db.RenderedAlertCaptures.CountAsync(c => c.ChannelEventId == channelEventId))
            .Should()
            .Be(1);
    }
}
