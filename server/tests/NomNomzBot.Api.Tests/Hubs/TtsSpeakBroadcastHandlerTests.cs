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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Tts.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a dispatched TTS utterance also reaches the <c>tts_caption</c> overlay surface: the handler routes a
/// <c>tts_speak</c> widget event carrying the spoken text/voice/user/duration to widgets subscribed to that type —
/// and only to those — closing the gap where <c>TtsDispatchService</c> host-played the audio but no caption
/// widget ever heard about the utterance.
/// </summary>
public sealed class TtsSpeakBroadcastHandlerTests
{
    [Fact]
    public async Task Dispatched_utterance_reaches_a_subscribed_widget_with_the_caption_payload()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget caption = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "TTS caption",
            IsEnabled = true,
            EventSubscriptions = ["tts_speak"],
        };
        db.Widgets.Add(caption);
        await db.SaveChangesAsync();
        TtsSpeakBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new TtsUtteranceDispatchedEvent
            {
                BroadcasterId = channel,
                Text = "hello chat",
                VoiceId = "en-US-AvaNeural",
                Provider = "azure",
                CharacterCount = 10,
                DurationMs = 2500,
                RequestedByTwitchUserId = "u1",
            }
        );

        // The anonymous payload carries exactly the fields the tts_caption SFC reads (text/voice/user/durationMs).
        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                caption.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "tts_speak" && PayloadMatches(evt.Data)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Dispatched_utterance_stays_quiet_for_unsubscribed_widgets()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget bystander = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Follow alert",
            IsEnabled = true,
            EventSubscriptions = ["follow"],
        };
        db.Widgets.Add(bystander);
        await db.SaveChangesAsync();
        TtsSpeakBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new TtsUtteranceDispatchedEvent
            {
                BroadcasterId = channel,
                Text = "hello chat",
                VoiceId = "en-US-AvaNeural",
                Provider = "azure",
                CharacterCount = 10,
                DurationMs = 2500,
                RequestedByTwitchUserId = "u1",
            }
        );

        await widgets
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default!, default!, default!, default);
    }

    /// <summary>Asserts the anonymous-typed payload's shape via its JSON form — the same fields the wire carries.</summary>
    private static bool PayloadMatches(object? data)
    {
        if (data is null)
            return false;
        JsonElement json = JsonSerializer.SerializeToElement(data);
        return json.GetProperty("text").GetString() == "hello chat"
            && json.GetProperty("voice").GetString() == "en-US-AvaNeural"
            && json.GetProperty("user").GetString() == "u1"
            && json.GetProperty("durationMs").GetInt32() == 2500;
    }
}
