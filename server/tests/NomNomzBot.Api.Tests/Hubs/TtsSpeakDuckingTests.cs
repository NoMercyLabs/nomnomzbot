// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>A spoken TTS line ducks the channel's music by the stored percent, for as long as the line lasts (S-PD-8b).</summary>
public sealed class TtsSpeakDuckingTests
{
    private static readonly Guid Channel = Guid.Parse("019f3000-1111-7000-8000-000000000001");

    private static async Task<IMusicDuckScheduler> SpeakAsync(
        int duckPercent,
        int durationMs,
        string? audioUrl
    )
    {
        IChannelAudioMixService mix = Substitute.For<IChannelAudioMixService>();
        mix.GetAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ChannelAudioMixDto(100, 100, duckPercent)));
        IMusicDuckScheduler ducks = Substitute.For<IMusicDuckScheduler>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        TtsSpeakBroadcastHandler handler = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            Substitute.For<IOverlayPresenceRegistry>(),
            mix,
            ducks,
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
                DurationMs = durationMs,
                RequestedByTwitchUserId = "42",
                DispatchMode = "self_host",
                AudioUrl = audioUrl,
            }
        );
        return ducks;
    }

    [Fact]
    public async Task A_server_audio_line_ducks_the_music_by_the_stored_percent_for_its_duration()
    {
        IMusicDuckScheduler ducks = await SpeakAsync(35, 4200, "data:audio/mpeg;base64,AAAA");

        await ducks.Received(1).DuckAsync(Channel, 35, 4200, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_client_edge_line_ducks_the_music_too()
    {
        IMusicDuckScheduler ducks = await SpeakAsync(50, 1500, audioUrl: null);

        await ducks.Received(1).DuckAsync(Channel, 50, 1500, Arg.Any<CancellationToken>());
    }
}
