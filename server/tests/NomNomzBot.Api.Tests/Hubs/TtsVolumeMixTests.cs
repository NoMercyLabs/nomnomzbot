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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// The TTS volume and the master volume the bot stores reach the overlay on both TTS routes: the client-edge
/// route (browser speaks the text) and the server-audio route (self_host / byok send an audio data URI).
/// </summary>
public sealed class TtsVolumeMixTests
{
    private static readonly Guid Channel = Guid.Parse("019f2c00-1111-7000-8000-000000000001");

    private static IChannelAudioMixService MixOf(int master, int tts)
    {
        IChannelAudioMixService mix = Substitute.For<IChannelAudioMixService>();
        mix.GetAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ChannelAudioMixDto(master, tts)));
        return mix;
    }

    [Theory]
    [InlineData(100, 60, 0.6)]
    [InlineData(50, 60, 0.3)]
    public async Task The_client_edge_route_carries_the_mixed_volume(
        int master,
        int tts,
        double expected
    )
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        TtsOverlayNotifierAdapter adapter = new(widgets, MixOf(master, tts));

        await adapter.SpeakAsync(Channel, new("hello", "voice-1", "edge", CueId: null));

        TtsSpeakPayload spoken = (TtsSpeakPayload)
            widgets.ReceivedCalls().Single().GetArguments()[1]!;
        spoken.Options.Should().NotBeNull();
        spoken.Options!.Volume.Should().BeApproximately(expected, 1e-9);
        spoken.Text.Should().Be("hello");
    }

    [Theory]
    [InlineData(100, 60, 0.6)]
    [InlineData(50, 60, 0.3)]
    public async Task The_server_audio_route_carries_the_mixed_volume(
        int master,
        int tts,
        double expected
    )
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        TtsSpeakBroadcastHandler handler = new(
            db,
            widgets,
            Substitute.For<IOverlayPresenceRegistry>(),
            Substitute.For<IDashboardNotifier>(),
            MixOf(master, tts),
            NullLogger<TtsSpeakBroadcastHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                Text = "hello",
                VoiceId = "voice-1",
                Provider = "edge",
                CharacterCount = 5,
                DurationMs = 900,
                RequestedByTwitchUserId = "42",
                DispatchMode = "self_host",
                AudioUrl = "data:audio/mpeg;base64,AAAA",
            }
        );

        TtsSpeakPayload spoken = widgets
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IWidgetNotifier.TtsSpeakAsync))
            .Select(c => (TtsSpeakPayload)c.GetArguments()[1]!)
            .Single();
        spoken.AudioUrl.Should().Be("data:audio/mpeg;base64,AAAA");
        spoken.Options!.Volume.Should().BeApproximately(expected, 1e-9);
    }
}
