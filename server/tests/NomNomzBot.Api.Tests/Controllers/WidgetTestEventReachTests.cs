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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The "test this overlay" button answered a flat "fired {eventType}" whether the event reached a live browser
/// source, reached a widget nobody had open, or reached nothing at all — so two identically configured channels
/// reported the same success while only one of them could actually play anything. The response must name which
/// of those three happened.
/// </summary>
public sealed class WidgetTestEventReachTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static async Task<string> FireAsync(
        ApiTestDbContext db,
        IOverlayPresenceRegistry presence
    )
    {
        WidgetTestEventController controller = new(db, Substitute.For<IWidgetNotifier>(), presence);
        IActionResult result = await controller.Fire(
            Broadcaster.ToString(),
            new WidgetTestEventRequest("tts_speak", null),
            CancellationToken.None
        );
        return ((StatusResponseDto<string>)((ObjectResult)result).Value!).Data!;
    }

    private static async Task<Widget> SeedSubscriberAsync(ApiTestDbContext db)
    {
        Widget widget = new()
        {
            BroadcasterId = Broadcaster,
            Name = "TTS Audio",
            EventSubscriptions = ["tts_speak"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget;
    }

    [Fact]
    public async Task The_chat_sample_carries_a_native_gif_fragment_so_Test_exercises_that_path()
    {
        // The Test button is the only way to drive an overlay without waiting for a real viewer. A sample made
        // only of text and emotes can rehearse a chat box that would still print a GIF as its caption text —
        // exactly the defect reported on 2026-09-06 — and report success while doing it. Serialize the pushed
        // payload the way the hub does, and assert the fragment a GIF actually needs is in there.
        using ApiTestDbContext db = ApiTestDbContext.New();
        Widget widget = new()
        {
            BroadcasterId = Broadcaster,
            Name = "Chat Box",
            EventSubscriptions = ["ChatMessage"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetTestEventController controller = new(
            db,
            notifier,
            Substitute.For<IOverlayPresenceRegistry>()
        );

        await controller.Fire(
            Broadcaster.ToString(),
            new WidgetTestEventRequest("ChatMessage", null),
            CancellationToken.None
        );

        WidgetEventDto pushed = (WidgetEventDto)
            notifier.ReceivedCalls().Single().GetArguments()[2]!;
        JsonElement payload = JsonSerializer.SerializeToElement(
            pushed.Data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

        JsonElement gif = payload
            .GetProperty("fragments")
            .EnumerateArray()
            .Single(fragment => fragment.GetProperty("type").GetString() == "gif");

        // A url is what every renderer keys its gif branch on; the caption text alone is what it falls back to.
        gif.GetProperty("gif").GetProperty("url").GetString().Should().StartWith("https://");
        gif.GetProperty("gif").GetProperty("gifId").GetString().Should().NotBeNullOrEmpty();
        gif.GetProperty("text").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_channel_with_no_subscribing_widget_is_told_so()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();

        string report = await FireAsync(db, presence);

        report.Should().Contain("no widget on this channel subscribes it");
    }

    [Fact]
    public async Task A_subscribing_widget_with_no_browser_source_open_is_reported_as_unheard()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        await SeedSubscriberAsync(db);
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(false);

        string report = await FireAsync(db, presence);

        report.Should().Contain("1 widget(s)");
        report.Should().Contain("nothing played it");
    }

    [Fact]
    public async Task An_attached_browser_source_is_reported_as_reached()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        Widget widget = await SeedSubscriberAsync(db);
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Broadcaster, widget.Id).Returns(true);

        string report = await FireAsync(db, presence);

        report.Should().Contain("1 with a browser source open");
        report.Should().NotContain("nothing played it");
    }
}
