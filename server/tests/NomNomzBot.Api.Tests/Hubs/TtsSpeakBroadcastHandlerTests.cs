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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Sound;
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
        TtsSpeakBroadcastHandler handler = new(
            db,
            widgets,
            Substitute.For<IOverlayPresenceRegistry>(),
            new ChannelAudioMixService(db),
            NullLogger<TtsSpeakBroadcastHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
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

        // The anonymous payload carries exactly the fields the TTS overlay widget reads
        // (text/voice/user/durationMs/audioUrl) — audioUrl is null: the audio rides the raw TtsSpeak to the one
        // audio page, never the widget event, so a caption page cannot play it a second time.
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
        TtsSpeakBroadcastHandler handler = new(
            db,
            widgets,
            Substitute.For<IOverlayPresenceRegistry>(),
            new ChannelAudioMixService(db),
            NullLogger<TtsSpeakBroadcastHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Text = "hello chat",
                VoiceId = "en-US-AvaNeural",
                Provider = "azure",
                CharacterCount = 10,
                DurationMs = 2500,
                RequestedByTwitchUserId = "u1",
                DispatchMode = "self_host",
            }
        );

        await widgets.DidNotReceiveWithAnyArgs().SendWidgetEventAsync(default!, default!, default!);
    }

    /// <summary>Asserts the anonymous-typed payload's shape via its JSON form — the same fields the wire carries.</summary>
    private static bool PayloadMatches(object? data)
    {
        if (data is null)
            return false;
        JsonElement json = JsonSerializer.SerializeToElement(data, OverlayWireJson.Options);
        return json.GetProperty("text").GetString() == "hello chat"
            && json.GetProperty("voice").GetString() == "en-US-AvaNeural"
            && json.GetProperty("user").GetString() == "u1"
            && json.GetProperty("durationMs").GetInt32() == 2500
            && json.GetProperty("audioUrl").ValueKind == JsonValueKind.Null;
    }

    private static async Task<ILogger<TtsSpeakBroadcastHandler>> SpeakAsync(bool audioPageConnected)
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsAudioSourceConnected(channel).Returns(audioPageConnected);
        ILogger<TtsSpeakBroadcastHandler> logger = Substitute.For<
            ILogger<TtsSpeakBroadcastHandler>
        >();

        TtsSpeakBroadcastHandler handler = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            presence,
            new ChannelAudioMixService(db),
            logger
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Text = "is anyone listening",
                VoiceId = "en-US-AvaNeural",
                Provider = "azure",
                CharacterCount = 19,
                DurationMs = 1000,
                RequestedByTwitchUserId = "u1",
                DispatchMode = "self_host",
                AudioUrl = "data:audio/mpeg;base64,AAAA",
            }
        );
        return logger;
    }

    /// <summary>
    /// With no Audio Source page open the line plays nowhere. The dashboard learns it from the inbox item, so
    /// the handler logs the warning for the operator and sends no transient alert (it has no dashboard notifier).
    /// </summary>
    [Fact]
    public async Task An_utterance_with_no_audio_page_logs_one_warning()
    {
        ILogger<TtsSpeakBroadcastHandler> logger = await SpeakAsync(audioPageConnected: false);

        List<string> warnings = logger
            .ReceivedCalls()
            .Where(c =>
                c.GetMethodInfo().Name == "Log"
                && (LogLevel)c.GetArguments()[0]! == LogLevel.Warning
            )
            .Select(c => c.GetArguments()[2]!.ToString()!)
            .ToList();
        warnings.Should().ContainSingle().Which.Should().Contain("no Audio Source page is open");
    }

    /// <summary>No warning when an Audio Source page IS connected, so the log does not cry wolf every utterance.</summary>
    [Fact]
    public async Task An_utterance_with_a_connected_audio_page_logs_no_warning()
    {
        ILogger<TtsSpeakBroadcastHandler> logger = await SpeakAsync(audioPageConnected: true);

        logger.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// S-REPLAY-TTS-PIPELINE-CORRELATION's done-when proof: a TTS utterance fired by a reward redemption's
    /// pipeline action chain (play_tts) carries the redemption's own ChannelEvent id all the way through
    /// TtsSpeakRequest → TtsDispatchService → TtsUtteranceDispatchedEvent, so the capture this handler writes
    /// for the <c>tts_speak</c> push is queryable by that SAME id — the real join a Replay lookup needs, not
    /// a fuzzy time-window match.
    /// </summary>
    [Fact]
    public async Task Pipeline_triggered_utterance_captures_the_triggering_redemptions_ChannelEventId()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid redemptionEventId = Guid.CreateVersion7();
        string channelEventId = redemptionEventId.ToString();

        db.Widgets.Add(
            new Widget
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "TTS caption",
                IsEnabled = true,
                EventSubscriptions = ["tts_speak"],
            }
        );
        await db.SaveChangesAsync();

        TtsSpeakBroadcastHandler handler = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            Substitute.For<IOverlayPresenceRegistry>(),
            new ChannelAudioMixService(db),
            NullLogger<TtsSpeakBroadcastHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Text = "thanks for the redemption",
                VoiceId = "en-US-AvaNeural",
                Provider = "azure",
                CharacterCount = 26,
                DurationMs = 3000,
                RequestedByTwitchUserId = "u1",
                DispatchMode = "self_host",
                AudioUrl = "data:audio/mpeg;base64,AQIDBA==",
                ChannelEventId = channelEventId,
            }
        );

        RenderedAlertCapture capture = await db.RenderedAlertCaptures.SingleAsync(c =>
            c.BroadcasterId == channel && c.EventType == "tts_speak"
        );
        capture.ChannelEventId.Should().Be(channelEventId);
    }

    /// <summary>
    /// The other half of the done-when proof: a standalone chat-command utterance (no triggering
    /// ChannelEvent — a free <c>!tts</c> never logs one) still correctly captures with a null
    /// ChannelEventId, not an invented/approximated correlation.
    /// </summary>
    [Fact]
    public async Task Chat_command_triggered_utterance_captures_a_null_ChannelEventId()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();

        db.Widgets.Add(
            new Widget
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "TTS caption",
                IsEnabled = true,
                EventSubscriptions = ["tts_speak"],
            }
        );
        await db.SaveChangesAsync();

        TtsSpeakBroadcastHandler handler = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            Substitute.For<IOverlayPresenceRegistry>(),
            new ChannelAudioMixService(db),
            NullLogger<TtsSpeakBroadcastHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
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

        RenderedAlertCapture capture = await db.RenderedAlertCaptures.SingleAsync(c =>
            c.BroadcasterId == channel && c.EventType == "tts_speak"
        );
        capture.ChannelEventId.Should().BeNull();
    }
}
