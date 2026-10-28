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

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a spoken utterance reaches the channel's dashboard clients live as one <c>TtsUtteranceSpoken</c> push
/// that carries the history row (id, text, voice, requester) and never the audio.
/// </summary>
public sealed class TtsSpokenBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_AnUtterance_PushesOneTtsUtteranceSpokenWithoutAudio()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        TtsSpokenBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Text = "hello world",
                VoiceId = "en-US-AnaNeural",
                Provider = "edge",
                CharacterCount = 11,
                DurationMs = 1200,
                RequestedByTwitchUserId = "viewer-1",
                DispatchMode = "self_host",
                AudioUrl = "data:audio/mpeg;base64,AQIDBA==",
                UsageRecordId = 42,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "TtsUtteranceSpoken",
                Arg.Is<TtsUtteranceSpokenAlertDto>(d =>
                    d.Id == 42
                    && d.Text == "hello world"
                    && d.VoiceId == "en-US-AnaNeural"
                    && d.Provider == "edge"
                    && d.RequestedByUserId == "viewer-1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_AnUtteranceWithoutAHistoryRow_PushesNothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        TtsSpokenBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.CreateVersion7(),
                Text = "x",
                VoiceId = "v",
                Provider = "edge",
                CharacterCount = 1,
                DurationMs = 0,
                RequestedByTwitchUserId = "",
                DispatchMode = "self_host",
            }
        );

        await notifier
            .DidNotReceiveWithAnyArgs()
            .NotifyChannelAsync(default!, default!, default!, default);
    }
}
