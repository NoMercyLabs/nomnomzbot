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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Sound;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Audio plays on exactly one overlay page. The registry names that page (the newest Audio source, else the
/// newest overlay page), the notifier sends sound and TTS only to it, and the TTS handler keeps the audio off
/// the widget event so a caption page can never play it.
/// </summary>
public sealed class OverlayAudioRoutingTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000a01");

    [Fact]
    public void The_audio_target_is_the_audio_source_even_when_a_caption_page_joined_before_it()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("caption", Broadcaster);
        registry.RegisterOverlay("audio", Broadcaster);
        registry.MarkAudioSource("audio");

        registry.GetAudioTarget(Broadcaster).Should().Be("audio");
        registry.IsAudioSourceConnected(Broadcaster).Should().BeTrue();
    }

    [Fact]
    public void The_audio_target_ignores_a_caption_page_that_joined_after_the_audio_source()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("audio", Broadcaster);
        registry.MarkAudioSource("audio");
        registry.RegisterOverlay("caption", Broadcaster);

        registry.GetAudioTarget(Broadcaster).Should().Be("audio");
    }

    [Fact]
    public void When_the_audio_source_disconnects_the_audio_falls_back_to_the_caption_page()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("caption", Broadcaster);
        registry.RegisterOverlay("audio", Broadcaster);
        registry.MarkAudioSource("audio");

        registry.Drop("audio");

        registry.GetAudioTarget(Broadcaster).Should().Be("caption");
        registry.IsAudioSourceConnected(Broadcaster).Should().BeFalse();
    }

    [Fact]
    public void With_no_page_open_there_is_no_audio_target()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("other-channel", Guid.NewGuid());
        registry.RegisterOverlay("gone", Broadcaster);
        registry.Drop("gone");

        registry.GetAudioTarget(Broadcaster).Should().BeNull();
    }

    [Fact]
    public void Of_two_audio_sources_the_newest_one_plays()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("first", Broadcaster);
        registry.MarkAudioSource("first");
        registry.RegisterOverlay("second", Broadcaster);
        registry.MarkAudioSource("second");

        registry.GetAudioTarget(Broadcaster).Should().Be("second");
    }

    private sealed record Pages(
        WidgetNotifier Notifier,
        IHubClients<IOverlayClient> Clients,
        IOverlayClient Audio,
        IOverlayClient Caption
    );

    private static Pages TwoPages()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("caption-conn", Broadcaster);
        registry.RegisterOverlay("audio-conn", Broadcaster);
        registry.MarkAudioSource("audio-conn");

        IOverlayClient audio = Substitute.For<IOverlayClient>();
        IOverlayClient caption = Substitute.For<IOverlayClient>();
        IHubClients<IOverlayClient> clients = Substitute.For<IHubClients<IOverlayClient>>();
        clients.Client("audio-conn").Returns(audio);
        clients.Client("caption-conn").Returns(caption);
        IHubContext<OverlayHub, IOverlayClient> hub = Substitute.For<
            IHubContext<OverlayHub, IOverlayClient>
        >();
        hub.Clients.Returns(clients);
        return new(new(hub, registry), clients, audio, caption);
    }

    [Fact]
    public async Task A_sound_clip_reaches_only_the_audio_page()
    {
        Pages pages = TwoPages();

        await pages.Notifier.PlaySoundAsync(
            Broadcaster.ToString(),
            new("https://x/clip.mp3", 80, "h1")
        );

        await pages.Audio.Received(1).PlaySound(Arg.Is<PlaySoundPayload>(p => p.Handle == "h1"));
        await pages.Caption.DidNotReceiveWithAnyArgs().PlaySound(default!);
        pages.Clients.DidNotReceiveWithAnyArgs().Group(default!);
    }

    [Fact]
    public async Task A_tts_line_and_a_stop_reach_only_the_audio_page()
    {
        Pages pages = TwoPages();

        await pages.Notifier.TtsSpeakAsync(
            Broadcaster.ToString(),
            new(
                Broadcaster,
                "hello",
                "v1",
                "azure",
                null,
                null,
                null,
                "data:audio/mpeg;base64,AQID"
            )
        );
        await pages.Notifier.StopSoundAsync(Broadcaster.ToString(), new(null, true));
        await pages.Notifier.TtsQueueControlAsync(Broadcaster.ToString(), "skip");

        await pages
            .Audio.Received(1)
            .TtsSpeak(
                Arg.Is<TtsSpeakPayload>(p =>
                    p.Text == "hello" && p.AudioUrl == "data:audio/mpeg;base64,AQID"
                )
            );
        await pages.Audio.Received(1).StopSound(Arg.Is<StopSoundPayload>(p => p.All));
        await pages
            .Audio.Received(1)
            .TtsQueueControl(Arg.Is<TtsQueueControlPayload>(p => p.Action == "skip"));
        await pages.Caption.DidNotReceiveWithAnyArgs().TtsSpeak(default!);
        await pages.Caption.DidNotReceiveWithAnyArgs().StopSound(default!);
        await pages.Caption.DidNotReceiveWithAnyArgs().TtsQueueControl(default!);
    }

    [Fact]
    public async Task With_no_page_open_nothing_is_sent_anywhere()
    {
        IHubClients<IOverlayClient> clients = Substitute.For<IHubClients<IOverlayClient>>();
        IHubContext<OverlayHub, IOverlayClient> hub = Substitute.For<
            IHubContext<OverlayHub, IOverlayClient>
        >();
        hub.Clients.Returns(clients);
        WidgetNotifier notifier = new(hub, new OverlayPresenceRegistry());

        await notifier.PlaySoundAsync(Broadcaster.ToString(), new("https://x/a.mp3", 100, null));

        clients.ReceivedCalls().Should().BeEmpty();
    }

    // ── JoinWidget: role detection and the SDK version gate ──

    private sealed record Joined(
        OverlayHub Hub,
        OverlayPresenceRegistry Registry,
        IOverlayClient Caller,
        IActionRequiredChangeNotifier Notifier
    );

    private static async Task<Joined> ConnectedPageAsync(
        WidgetTestDbContext db,
        string connectionId
    )
    {
        OverlayTicketService tickets = new(new FakeTimeProvider());
        string ticket = tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null));
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        DefaultHttpContext http = new();
        http.Request.QueryString = new("?ticket=" + Uri.EscapeDataString(ticket));
        context.GetHttpContext().Returns(http);
        context.Items.Returns(new Dictionary<object, object?>());
        IOverlayClient caller = Substitute.For<IOverlayClient>();
        IHubCallerClients<IOverlayClient> clients = Substitute.For<
            IHubCallerClients<IOverlayClient>
        >();
        clients.Caller.Returns(caller);
        OverlayPresenceRegistry registry = new();
        IActionRequiredChangeNotifier notifier = Substitute.For<IActionRequiredChangeNotifier>();
        OverlayHub hub = new(
            db,
            Substitute.For<IWidgetService>(),
            tickets,
            registry,
            Substitute.For<IChannelRegistry>(),
            notifier,
            Substitute.For<IEventBus>(),
            NullLogger<OverlayHub>.Instance
        )
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = clients,
        };
        await hub.OnConnectedAsync();
        return new(hub, registry, caller, notifier);
    }

    private static async Task<Guid> SeedWidgetAsync(WidgetTestDbContext db, string naturalKey)
    {
        WidgetGalleryItem item = new() { Name = naturalKey, NaturalKey = naturalKey };
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = naturalKey,
            IsEnabled = true,
            GalleryItemId = item.Id,
        };
        db.WidgetGalleryItems.Add(item);
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget.Id;
    }

    [Fact]
    public async Task Joining_the_audio_source_widget_makes_that_page_the_audio_target()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid audioWidget = await SeedWidgetAsync(db, "tts_audio");
        Joined page = await ConnectedPageAsync(db, "audio-conn");

        JoinWidgetResponse response = await page.Hub.JoinWidgetWithSdk(
            audioWidget.ToString(),
            "v1"
        );

        response.Success.Should().BeTrue();
        page.Registry.IsAudioSourceConnected(Broadcaster).Should().BeTrue();
        page.Registry.GetAudioTarget(Broadcaster).Should().Be("audio-conn");
        await page.Caller.DidNotReceive().WidgetReload();
    }

    [Fact]
    public async Task An_audio_source_joining_and_leaving_signals_the_inbox_each_time()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid audioWidget = await SeedWidgetAsync(db, "tts_audio");
        Joined page = await ConnectedPageAsync(db, "audio-conn");
        page.Notifier.ClearReceivedCalls();

        await page.Hub.JoinWidgetWithSdk(audioWidget.ToString(), "v1");

        page.Registry.IsAudioSourceConnected(Broadcaster).Should().BeTrue();
        page.Notifier.Received(1).NotifyChanged(Broadcaster);
        page.Notifier.ClearReceivedCalls();

        await page.Hub.OnDisconnectedAsync(null);

        page.Registry.IsAudioSourceConnected(Broadcaster).Should().BeFalse();
        page.Notifier.Received(1).NotifyChanged(Broadcaster);
    }

    [Fact]
    public async Task A_page_connecting_signals_the_inbox_so_a_missing_audio_source_shows_at_once()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();

        Joined page = await ConnectedPageAsync(db, "caption-conn");

        page.Registry.IsOverlayConnected(Broadcaster).Should().BeTrue();
        page.Notifier.Received(1).NotifyChanged(Broadcaster);
    }

    [Fact]
    public async Task Joining_a_caption_widget_does_not_make_it_an_audio_source()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid captionWidget = await SeedWidgetAsync(db, "tts_caption");
        Joined page = await ConnectedPageAsync(db, "caption-conn");

        await page.Hub.JoinWidgetWithSdk(captionWidget.ToString(), "v1");

        page.Registry.IsAudioSourceConnected(Broadcaster).Should().BeFalse();
    }

    [Fact]
    public async Task A_join_without_an_sdk_version_is_told_to_reload_to_pick_up_the_new_sdk()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid captionWidget = await SeedWidgetAsync(db, "tts_caption");
        Joined page = await ConnectedPageAsync(db, "old-sdk-conn");

        await page.Hub.JoinWidget(captionWidget.ToString());

        await page.Caller.Received(1).WidgetReload();
    }

    // ── TtsSpeakBroadcastHandler: audio rides the raw target only ──

    [Fact]
    public async Task Server_audio_goes_to_the_audio_page_and_the_widget_event_carries_none()
    {
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Widget caption = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "caption",
            IsEnabled = true,
            EventSubscriptions = ["tts_speak"],
        };
        db.Widgets.Add(caption);
        await db.SaveChangesAsync();
        TtsSpeakBroadcastHandler handler = new(
            db,
            notifier,
            Substitute.For<IOverlayPresenceRegistry>(),
            Substitute.For<IDashboardNotifier>(),
            new ChannelAudioMixService(db),
            NullLogger<TtsSpeakBroadcastHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Broadcaster,
                Text = "hello chat",
                VoiceId = "en-US-AvaNeural",
                Provider = "azure",
                CharacterCount = 10,
                DurationMs = 2500,
                RequestedByTwitchUserId = "u1",
                DispatchMode = "self_host",
                AudioUrl = "data:audio/mpeg;base64,AQIDBA==",
            }
        );

        await notifier
            .Received(1)
            .TtsSpeakAsync(
                Broadcaster.ToString(),
                Arg.Is<TtsSpeakPayload>(p =>
                    p.Text == "hello chat"
                    && p.VoiceId == "en-US-AvaNeural"
                    && p.AudioUrl == "data:audio/mpeg;base64,AQIDBA=="
                ),
                Arg.Any<CancellationToken>()
            );
        await notifier
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster.ToString(),
                caption.Id.ToString(),
                Arg.Is<WidgetEventDto>(e =>
                    e.EventType == "tts_speak"
                    && ((TtsSpeakWidgetPayload)e.Data!).AudioUrl == null
                    && ((TtsSpeakWidgetPayload)e.Data!).Text == "hello chat"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
