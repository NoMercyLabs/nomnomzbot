// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Sound.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// TTS output is core, not an optional hook: the two overlay notifier contracts carry no default method body,
/// so an implementer that forgets the TTS member fails to compile instead of dropping a clip in silence.
/// </summary>
public sealed class TtsNotifierContractTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000a03");

    [Theory]
    [InlineData(typeof(ISoundClipOverlayNotifier))]
    [InlineData(typeof(IWidgetNotifier))]
    public void A_notifier_contract_has_no_default_method_body(Type contract)
    {
        List<string> withBody = contract
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsAbstract)
            .Select(m => m.Name)
            .ToList();

        withBody
            .Should()
            .BeEmpty(
                "{0} members need an explicit body in every implementer, but these carry a silent default: {1}",
                contract.Name,
                string.Join(", ", withBody)
            );
    }

    [Fact]
    public async Task A_tts_clip_through_the_sound_clip_adapter_plays_on_the_tts_page_not_the_audio_page()
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
        ISoundClipOverlayNotifier adapter = new SoundClipOverlayNotifierAdapter(
            new WidgetNotifier(hub, registry)
        );

        await adapter.PlayTtsAsync(
            Broadcaster,
            new(Guid.NewGuid(), "/tts/a.mp3", 70, 1500, "tts-h1", true)
        );

        await tts.Received(1)
            .PlaySound(
                Arg.Is<PlaySoundPayload>(p =>
                    p.PlaybackUrl == "/tts/a.mp3"
                    && p.Volume == 70
                    && p.Handle == "tts-h1"
                    && p.Synced
                )
            );
        await audio.DidNotReceiveWithAnyArgs().PlaySound(default!);
    }
}
