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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Widgets.Services;
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
        TtsSpeakBroadcastHandler handler = new(
            db,
            widgets,
            Substitute.For<IOverlayPresenceRegistry>(),
            Substitute.For<IDashboardNotifier>(),
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
        // (text/voice/user/durationMs/audioUrl) — audioUrl is what lets the widget's own queue play the
        // utterance in order, instead of the generic unqueued overlay sound bus.
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
            Substitute.For<IDashboardNotifier>(),
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
        JsonElement json = JsonSerializer.SerializeToElement(data);
        return json.GetProperty("text").GetString() == "hello chat"
            && json.GetProperty("voice").GetString() == "en-US-AvaNeural"
            && json.GetProperty("user").GetString() == "u1"
            && json.GetProperty("durationMs").GetInt32() == 2500
            && json.GetProperty("audioUrl").GetString() == "data:audio/mpeg;base64,AQIDBA==";
    }

    /// <summary>
    /// TTS reported every utterance as spoken whether or not a browser source was open on a subscribing
    /// widget, so when the streamer had not added the TTS overlay the stream heard NOTHING while the bot,
    /// the logs and the dashboard all looked healthy. That silence is now stated.
    /// </summary>
    [Fact]
    public async Task An_utterance_with_no_attached_browser_source_is_reported_not_silently_dropped()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        db.Widgets.Add(
            new Widget
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "TTS Audio",
                IsEnabled = true,
                EventSubscriptions = ["tts_speak"],
            }
        );
        await db.SaveChangesAsync();

        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(false);
        IDashboardNotifier dashboard = Substitute.For<IDashboardNotifier>();

        TtsSpeakBroadcastHandler handler = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            presence,
            dashboard,
            NullLogger<TtsSpeakBroadcastHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Text = "nobody can hear this",
                VoiceId = "en-US-AvaNeural",
                Provider = "azure",
                CharacterCount = 20,
                DurationMs = 1000,
                RequestedByTwitchUserId = "u1",
                DispatchMode = "self_host",
                AudioUrl = "data:audio/mpeg;base64,AAAA",
            }
        );

        await dashboard
            .Received(1)
            .SendAlertAsync(
                channel.ToString(),
                Arg.Is<AlertDto>(a => a.Type == "tts_no_output"),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>No alert when a browser source IS attached — the warning must not cry wolf every utterance.</summary>
    [Fact]
    public async Task An_utterance_with_an_attached_browser_source_raises_no_alert()
    {
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        db.Widgets.Add(
            new Widget
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "TTS Audio",
                IsEnabled = true,
                EventSubscriptions = ["tts_speak"],
            }
        );
        await db.SaveChangesAsync();

        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(true);
        IDashboardNotifier dashboard = Substitute.For<IDashboardNotifier>();

        TtsSpeakBroadcastHandler handler = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            presence,
            dashboard,
            NullLogger<TtsSpeakBroadcastHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Text = "this one is audible",
                VoiceId = "en-US-AvaNeural",
                Provider = "azure",
                CharacterCount = 19,
                DurationMs = 1000,
                RequestedByTwitchUserId = "u1",
                DispatchMode = "self_host",
                AudioUrl = "data:audio/mpeg;base64,AAAA",
            }
        );

        await dashboard
            .DidNotReceive()
            .SendAlertAsync(Arg.Any<string>(), Arg.Any<AlertDto>(), Arg.Any<CancellationToken>());
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
            Substitute.For<IDashboardNotifier>(),
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
            Substitute.For<IDashboardNotifier>(),
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
