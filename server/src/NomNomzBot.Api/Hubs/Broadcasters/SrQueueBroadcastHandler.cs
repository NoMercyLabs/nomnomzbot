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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Song-request queue change → the standing <c>sr_queue</c> overlay widget (music-sr.md). Pushes the event's
/// fresh top-of-queue snapshot as an <c>sr_queue</c> widget event — <c>{ items: [{ title, requestedBy,
/// durationSec }] }</c> after the hub's camelCase serialization — through the shared subscription-matched
/// dispatch, so only widgets that declare <c>sr_queue</c> receive it. Like now-playing, this drives a standing
/// display rather than a transient alert, so it routes widgets-only (no decorated dashboard equivalent exists;
/// the raw journaled event already rides the generic feed).
/// </summary>
public sealed class SrQueueBroadcastHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<SongRequestQueueChangedEvent>
{
    public Task HandleAsync(
        SongRequestQueueChangedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "sr_queue",
            new { items = @event.Items },
            cancellationToken
        );
}
