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

    private static async Task<WidgetTestEventResult> FireAsync(
        ApiTestDbContext db,
        IOverlayPresenceRegistry presence,
        Guid widgetId
    )
    {
        WidgetTestEventController controller = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            presence,
            TimeProvider.System
        );
        IActionResult result = await controller.Fire(
            Broadcaster.ToString(),
            widgetId.ToString(),
            new WidgetTestEventRequest("tts_speak", null),
            CancellationToken.None
        );
        return ((StatusResponseDto<WidgetTestEventResult>)((ObjectResult)result).Value!).Data!;
    }

    private static async Task<Widget> SeedSubscriberAsync(
        ApiTestDbContext db,
        string name = "TTS Audio"
    )
    {
        Widget widget = new()
        {
            BroadcasterId = Broadcaster,
            Name = name,
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
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );

        await controller.Fire(
            Broadcaster.ToString(),
            widget.Id.ToString(),
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
    public async Task A_test_reaches_only_the_overlay_it_targets_not_every_subscriber()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        Widget target = await SeedSubscriberAsync(db, "Emote Wall");
        Widget other = await SeedSubscriberAsync(db, "Chat Box");
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetTestEventController controller = new(
            db,
            notifier,
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );

        await controller.Fire(
            Broadcaster.ToString(),
            target.Id.ToString(),
            new WidgetTestEventRequest("tts_speak", null),
            CancellationToken.None
        );

        List<string> receivers = notifier
            .ReceivedCalls()
            .Select(call => (string)call.GetArguments()[1]!)
            .ToList();
        receivers.Should().Equal(target.Id.ToString());
        receivers.Should().NotContain(other.Id.ToString());
    }

    // Owner 2026-10-15: "test data should never persist in the active overlays" — a custom payload is a test too.
    [Fact]
    public async Task A_custom_test_payload_reaches_the_overlay_marked_as_a_test()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        Widget target = await SeedSubscriberAsync(db, "Emote Wall");
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetTestEventController controller = new(
            db,
            notifier,
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );

        await controller.Fire(
            Broadcaster.ToString(),
            target.Id.ToString(),
            new WidgetTestEventRequest(
                "tts_speak",
                new Dictionary<string, object?> { ["text"] = "custom line" }
            ),
            CancellationToken.None
        );

        List<WidgetEventDto> sent = notifier
            .ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<WidgetEventDto>()
            .ToList();
        sent.Should().ContainSingle().Which.IsTest.Should().BeTrue();
    }

    [Fact]
    public async Task An_overlay_that_does_not_react_to_the_event_is_not_sent_it()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        Widget silent = new() { BroadcasterId = Broadcaster, Name = "Goal" };
        db.Widgets.Add(silent);
        await db.SaveChangesAsync();
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetTestEventController controller = new(
            db,
            notifier,
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );

        IActionResult result = await controller.Fire(
            Broadcaster.ToString(),
            silent.Id.ToString(),
            new WidgetTestEventRequest("tts_speak", null),
            CancellationToken.None
        );

        StatusResponseDto<WidgetTestEventResult> body =
            (StatusResponseDto<WidgetTestEventResult>)((ObjectResult)result).Value!;
        body.Data.Should().Be(new WidgetTestEventResult(false, false));
        notifier.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_widget_of_another_channel_is_not_found_and_nothing_is_sent()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        Widget foreign = new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            Name = "Theirs",
            EventSubscriptions = ["tts_speak"],
        };
        db.Widgets.Add(foreign);
        await db.SaveChangesAsync();
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetTestEventController controller = new(
            db,
            notifier,
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );

        IActionResult result = await controller.Fire(
            Broadcaster.ToString(),
            foreign.Id.ToString(),
            new WidgetTestEventRequest("tts_speak", null),
            CancellationToken.None
        );

        ((ObjectResult)result).StatusCode.Should().Be(404);
        notifier.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_subscribing_widget_with_no_browser_source_open_is_sent_but_not_attached()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        Widget widget = await SeedSubscriberAsync(db);
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(false);

        WidgetTestEventResult answer = await FireAsync(db, presence, widget.Id);

        answer.Should().Be(new WidgetTestEventResult(true, false));
    }

    [Fact]
    public async Task An_attached_browser_source_is_reported_as_attached()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        Widget widget = await SeedSubscriberAsync(db);
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Broadcaster, widget.Id).Returns(true);

        WidgetTestEventResult answer = await FireAsync(db, presence, widget.Id);

        answer.Should().Be(new WidgetTestEventResult(true, true));
    }
}
