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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Every poll of the ad schedule → the <c>ad_schedule</c> widget event, so a countdown widget always knows when the
/// next ad is due. Standing state, like <c>now_playing</c>: it reaches only widgets that subscribe to the name and
/// never the alert queue.
/// </summary>
public sealed class AdScheduleBroadcastHandler(IApplicationDbContext db, IWidgetNotifier widgets)
    : IEventHandler<AdScheduleUpdatedEvent>
{
    public Task HandleAsync(
        AdScheduleUpdatedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            @event.BroadcasterId,
            "ad_schedule",
            new AdScheduleWidgetPayload(
                @event.NextAdAt,
                @event.LastAdAt,
                @event.DurationSeconds,
                @event.PrerollFreeTimeSeconds,
                @event.SnoozeCount,
                @event.SnoozeRefreshAt,
                @event.TimeUntilNextAdSeconds
            ),
            excludeWidgetId: null,
            channelEventId: null,
            cancellationToken
        );
}

/// <summary>
/// An ad is near (one of the warn thresholds was crossed) → the <c>ad_upcoming</c> widget event, once per threshold
/// for each next-ad slot. It reaches only widgets that subscribe to the name and never the alert queue.
/// </summary>
public sealed class AdUpcomingBroadcastHandler(IApplicationDbContext db, IWidgetNotifier widgets)
    : IEventHandler<AdBreakUpcomingEvent>
{
    public Task HandleAsync(
        AdBreakUpcomingEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            @event.BroadcasterId,
            "ad_upcoming",
            new AdUpcomingWidgetPayload(
                @event.SecondsUntilAd,
                @event.ThresholdSeconds,
                @event.DurationSeconds,
                @event.NextAdAt
            ),
            excludeWidgetId: null,
            channelEventId: null,
            cancellationToken
        );
}
