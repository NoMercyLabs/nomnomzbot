// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Events;
using NomNomzBot.Domain.Widgets.Entities;

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
    IDashboardNotifier dashboard,
    ILogger<TtsSpeakBroadcastHandler> logger
) : IEventHandler<TtsUtteranceDispatchedEvent>
{
    public async Task HandleAsync(
        TtsUtteranceDispatchedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        await ReportIfNothingIsListeningAsync(@event.BroadcasterId, cancellationToken);
        await WidgetAlertDispatch.RouteAsync(
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
            // Non-null only when this utterance was fired by a pipeline action chain triggered by a PAID
            // channel event (e.g. a reward redemption whose actions include play_tts) — PlayTtsAction threads
            // the triggering ChannelEvent id down through TtsSpeakRequest.ChannelEventId. A standalone chat
            // command (!tts) never logs a ChannelEvent at all, so it stays genuinely null here, not a fuzzy
            // time-window join.
            channelEventId: @event.ChannelEventId,
            cancellationToken
        );
    }

    /// <summary>
    /// A dispatched utterance is delivered to every widget subscribing <c>tts_speak</c> whether or not a
    /// browser source is actually open on one, so TTS reported success while the stream heard nothing at all —
    /// which is exactly how it went unnoticed for days. If no subscribing widget is attached, say so: a
    /// warning in the log and an alert on the dashboard, rather than silence that looks like success.
    /// </summary>
    private async Task ReportIfNothingIsListeningAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<Widget> widgets = await db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == broadcasterId)
            .ToListAsync(cancellationToken);

        List<Widget> subscribers = WidgetAlertRouting.Subscribers(widgets, "tts_speak").ToList();
        if (subscribers.Any(w => presence.IsWidgetAttached(broadcasterId, w.Id)))
            return;

        string reason =
            subscribers.Count == 0
                ? "no widget on this channel subscribes tts_speak"
                : "no browser source is open on the TTS widget";
        logger.LogWarning(
            "TTS spoke for channel {BroadcasterId} but nothing could play it: {Reason}. Add the TTS Audio "
                + "overlay as a browser source in OBS.",
            broadcasterId,
            reason
        );
        await dashboard.SendAlertAsync(
            broadcasterId.ToString(),
            new(
                "tts_no_output",
                "TTS spoke but nothing played it — add the TTS Audio overlay as a browser source in OBS.",
                new { reason, subscribingWidgets = subscribers.Count }
            ),
            cancellationToken
        );
    }
}
