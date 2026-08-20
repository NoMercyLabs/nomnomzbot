// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Persistence;
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
public sealed class TtsSpeakBroadcastHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<TtsUtteranceDispatchedEvent>
{
    public Task HandleAsync(
        TtsUtteranceDispatchedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "tts_speak",
            new
            {
                text = @event.Text,
                voice = @event.VoiceId,
                user = @event.RequestedByTwitchUserId,
                durationMs = @event.DurationMs,
                audioUrl = @event.AudioUrl,
            },
            cancellationToken
        );
}
