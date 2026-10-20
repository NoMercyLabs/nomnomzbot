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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Spoken messages play on the TTS page when one is open; sound clips stay on the Bot Audio page. With no TTS
/// page open, TTS falls back to the Bot Audio page so the stream still hears it.
/// </summary>
public sealed class OverlayTtsRoutingTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000a02");

    private sealed record Pages(
        OverlayPresenceRegistry Registry,
        WidgetNotifier Notifier,
        IOverlayClient Audio,
        IOverlayClient Tts
    );

    private static Pages AudioAndTtsPages()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("audio-conn", Broadcaster);
        registry.MarkAudioSource("audio-conn");
        registry.RegisterOverlay("tts-conn", Broadcaster);
        registry.MarkTtsSource("tts-conn");

        IOverlayClient audio = Substitute.For<IOverlayClient>();
        IOverlayClient tts = Substitute.For<IOverlayClient>();
        IHubClients<IOverlayClient> clients = Substitute.For<IHubClients<IOverlayClient>>();
        clients.Client("audio-conn").Returns(audio);
        clients.Client("tts-conn").Returns(tts);
        IHubContext<OverlayHub, IOverlayClient> hub = Substitute.For<
            IHubContext<OverlayHub, IOverlayClient>
        >();
        hub.Clients.Returns(clients);
        return new(registry, new(hub, registry), audio, tts);
    }

    private static TtsSpeakPayload Line(string text) =>
        new(Broadcaster, text, "v1", "azure", null, null, null, "data:audio/mpeg;base64,AQID");

    [Fact]
    public async Task A_tts_line_and_its_queue_control_play_on_the_tts_page_not_the_audio_page()
    {
        Pages pages = AudioAndTtsPages();

        await pages.Notifier.TtsSpeakAsync(Broadcaster.ToString(), Line("hello"));
        await pages.Notifier.TtsQueueControlAsync(Broadcaster.ToString(), "skip");

        await pages.Tts.Received(1).TtsSpeak(Arg.Is<TtsSpeakPayload>(p => p.Text == "hello"));
        await pages
            .Tts.Received(1)
            .TtsQueueControl(Arg.Is<TtsQueueControlPayload>(p => p.Action == "skip"));
        await pages.Audio.DidNotReceiveWithAnyArgs().TtsSpeak(default!);
        await pages.Audio.DidNotReceiveWithAnyArgs().TtsQueueControl(default!);
    }

    [Fact]
    public async Task A_sound_clip_still_plays_on_the_audio_page_when_a_tts_page_is_open()
    {
        Pages pages = AudioAndTtsPages();

        await pages.Notifier.PlaySoundAsync(
            Broadcaster.ToString(),
            new("https://x/a.mp3", 80, "h1")
        );

        await pages.Audio.Received(1).PlaySound(Arg.Is<PlaySoundPayload>(p => p.Handle == "h1"));
        await pages.Tts.DidNotReceiveWithAnyArgs().PlaySound(default!);
    }

    [Fact]
    public async Task When_the_tts_page_closes_tts_falls_back_to_the_audio_page()
    {
        Pages pages = AudioAndTtsPages();

        pages.Registry.Drop("tts-conn");
        await pages.Notifier.TtsSpeakAsync(Broadcaster.ToString(), Line("back"));

        pages.Registry.GetTtsTarget(Broadcaster).Should().Be("audio-conn");
        await pages.Audio.Received(1).TtsSpeak(Arg.Is<TtsSpeakPayload>(p => p.Text == "back"));
    }

    [Fact]
    public void A_tts_page_alone_is_the_tts_target_and_not_an_audio_source()
    {
        OverlayPresenceRegistry registry = new();
        registry.RegisterOverlay("tts-conn", Broadcaster);
        registry.MarkTtsSource("tts-conn");

        registry.GetTtsTarget(Broadcaster).Should().Be("tts-conn");
        registry.GetAudioTarget(Broadcaster).Should().BeNull();
    }
}
