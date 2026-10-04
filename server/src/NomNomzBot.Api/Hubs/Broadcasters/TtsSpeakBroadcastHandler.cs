// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Dispatched TTS utterance → the <c>tts_speak</c> overlay widget event (tts.md), carrying
/// <c>{ text, voice, user, durationMs, audioUrl }</c> after the hub's camelCase serialization —
/// <c>audioUrl</c> is a <c>data:</c> URI on the <c>self_host</c>/<c>byok</c> planes, <c>null</c> on
/// <c>client_edge</c> (the browser's own <c>speechSynthesis</c> has no server audio). The dedicated TTS
/// overlay widget queues entries by this event and plays them strictly in order — deliberately NOT the
/// generic (unqueued) overlay sound bus, so two utterances close together never overlap. Routed through
/// the shared subscription-matched dispatch so any widget declaring <c>tts_speak</c> can also react
/// visually (a speaking indicator, auto-hide on duration) whether or not it plays the audio itself.
/// </summary>
public sealed class TtsSpeakBroadcastHandler(
    IApplicationDbContext db,
    IWidgetNotifier notifier,
    IOverlayPresenceRegistry presence,
    IChannelAudioMixService mix,
    ILogger<TtsSpeakBroadcastHandler> logger
) : IEventHandler<TtsUtteranceDispatchedEvent>
{
    /// <summary>The widget event a TTS utterance is pushed (and captured) as.</summary>
    public const string WidgetEventType = "tts_speak";

    public async Task HandleAsync(
        TtsUtteranceDispatchedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        WarnIfNoAudioPage(@event.BroadcasterId);
        // Captions and indicators only need the words: the audio plays once, on the audio page, from the raw
        // TtsSpeak below. A widget event that carried audio would be played by every page that received it.
        if (@event.AudioUrl is not null)
        {
            Result<ChannelAudioMixDto> mixResult = await mix.GetAsync(
                @event.BroadcasterId,
                cancellationToken
            );
            TtsSpeakOptions? options = mixResult.IsSuccess
                ? new TtsSpeakOptions(null, null, mixResult.Value.TtsPlaybackVolume)
                : null;
            await notifier.TtsSpeakAsync(
                @event.BroadcasterId.ToString(),
                new(
                    @event.BroadcasterId,
                    @event.Text,
                    @event.VoiceId,
                    @event.Provider,
                    CueId: null,
                    Options: options,
                    AudioUrl: @event.AudioUrl
                ),
                cancellationToken
            );
        }
        await WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            WidgetEventType,
            new TtsSpeakWidgetPayload(
                @event.Text,
                @event.VoiceId,
                @event.RequestedByTwitchUserId,
                @event.DurationMs,
                AudioUrl: null
            ),
            // Non-null only when this utterance was fired by a pipeline action chain triggered by a PAID
            // channel event (e.g. a reward redemption whose actions include play_tts) — PlayTtsAction threads
            // the triggering ChannelEvent id down through TtsSpeakRequest.ChannelEventId. A standalone chat
            // command (!tts) never logs a ChannelEvent at all, so it stays genuinely null here, not a fuzzy
            // time-window join.
            excludeWidgetId: null,
            channelEventId: @event.ChannelEventId,
            cancellationToken
        );
    }

    /// <summary>
    /// TTS reported success while no Audio Source page was open and the stream heard nothing. The inbox item
    /// <c>audio-source-missing</c> tells the streamer; the log line is for the operator.
    /// </summary>
    private void WarnIfNoAudioPage(Guid broadcasterId)
    {
        if (presence.IsAudioSourceConnected(broadcasterId))
            return;

        logger.LogWarning(
            "TTS spoke for channel {BroadcasterId} but no Audio Source page is open to play it. Add the TTS "
                + "source in OBS.",
            broadcasterId
        );
    }
}
