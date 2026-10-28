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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Dispatched TTS utterance → the <c>tts_speak</c> overlay widget event (tts.md), carrying
/// <c>{ text, voice, user, durationMs, audioUrl }</c> after the hub's camelCase serialization —
/// <c>audioUrl</c> is the server-rendered audio on the <c>self_host</c>/<c>byok</c> planes. The dedicated TTS
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
    IMusicDuckScheduler ducks,
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
        Result<ChannelAudioMixDto> mixResult = await mix.GetAsync(
            @event.BroadcasterId,
            cancellationToken
        );
        if (mixResult.IsSuccess)
            await ducks.DuckAsync(
                @event.BroadcasterId,
                mixResult.Value.MusicDuckPercent,
                @event.DurationMs,
                cancellationToken
            );
        if (@event.AudioUrl is not null)
        {
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
    /// TTS reported success while no TTS Caption page was open and the stream heard nothing. The inbox item
    /// <c>tts-source-missing</c> tells the streamer; the log line is for the operator.
    /// </summary>
    private void WarnIfNoAudioPage(Guid broadcasterId)
    {
        if (presence.GetTtsTarget(broadcasterId) is not null)
            return;

        logger.LogWarning(
            "TTS spoke for channel {BroadcasterId} but no TTS Caption page is open to play it. Add the TTS "
                + "Caption source in OBS.",
            broadcasterId
        );
    }
}
