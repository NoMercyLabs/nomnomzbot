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
/// Dispatched TTS utterance → the <c>tts_caption</c> overlay widget (tts.md). The audio itself rides the
/// host page's audio bus (<c>PlaySound</c>); this pushes the caption leg — a <c>tts_speak</c> widget event
/// carrying <c>{ text, voice, user, durationMs }</c> after the hub's camelCase serialization — through the
/// shared subscription-matched dispatch, so a speaking indicator can render (and auto-hide on duration)
/// only on widgets that declare <c>tts_speak</c>.
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
            },
            cancellationToken
        );
}
