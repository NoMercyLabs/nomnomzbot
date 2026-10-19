// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
