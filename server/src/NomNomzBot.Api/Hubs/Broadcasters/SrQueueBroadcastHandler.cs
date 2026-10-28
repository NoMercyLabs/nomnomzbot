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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Song-request queue change → the standing <c>sr_queue</c> overlay widget (music-sr.md) AND the dashboard.
/// Pushes the event's fresh top-of-queue snapshot as an <c>sr_queue</c> widget event — <c>{ items: [{ title,
/// requestedBy, durationSec }] }</c> after the hub's camelCase serialization — through the shared
/// subscription-matched dispatch, so only widgets that declare <c>sr_queue</c> receive it. The same
/// already-built <c>{ items }</c> payload is also pushed to the dashboard's channel group under the
/// <c>sr_queue_changed</c> method, so a mod promoting/banning/removing a queued track from the dashboard
/// sees the live update too — not just the OBS-facing widget.
/// </summary>
public sealed class SrQueueBroadcastHandler(
    IApplicationDbContext db,
    IWidgetNotifier widgets,
    IDashboardNotifier dashboard
) : IEventHandler<SongRequestQueueChangedEvent>
{
    public async Task HandleAsync(
        SongRequestQueueChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        SrQueueWidgetPayload payload = new(@event.Items);

        await WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            @event.BroadcasterId,
            "sr_queue",
            payload,
            // Standing queue snapshot — not a ChannelEvent-backed feed item.
            excludeWidgetId: null,
            channelEventId: null,
            cancellationToken
        );

        await dashboard.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "sr_queue_changed",
            payload,
            cancellationToken
        );
    }
}
