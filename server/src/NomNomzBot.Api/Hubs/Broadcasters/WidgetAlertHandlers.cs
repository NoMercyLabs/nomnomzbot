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
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Fans a channel domain event out to the overlay widgets (OBS browser-sources) that subscribe to it — the link
/// from a domain event to an on-stream alert over <c>OverlayHub</c>. The routing decision lives in
/// <see cref="WidgetAlertRouting"/>; this is the shared db-read + push. The transient user-facing alerts
/// (follow/sub/cheer/raid/gift/resub/reward/role/shoutout/ban/hype-train) route through this via
/// <see cref="OverlayAlertBroadcast"/>, which pushes the SAME decorated dto the dashboard gets — so a widget never
/// sees a thinner payload than the dashboard. Standing displays with no dashboard-enriched equivalent (now-playing)
/// keep their own handler below.
/// </summary>
internal static class WidgetAlertDispatch
{
    public static async Task RouteAsync(
        IApplicationDbContext db,
        IWidgetNotifier notifier,
        Guid broadcasterId,
        string eventType,
        object data,
        CancellationToken cancellationToken
    )
    {
        if (broadcasterId == Guid.Empty)
            return;

        List<Widget> widgets = await db
            .Widgets.Where(w => w.BroadcasterId == broadcasterId)
            .ToListAsync(cancellationToken);

        foreach (Widget widget in WidgetAlertRouting.Subscribers(widgets, eventType))
            await notifier.SendWidgetEventAsync(
                broadcasterId.ToString(),
                widget.Id.ToString(),
                new WidgetEventDto(widget.Id.ToString(), eventType, data),
                cancellationToken
            );
    }
}

/// <summary>
/// Playback change → the persistent <c>now_playing</c> overlay widget (music-sr.md). Unlike the transient alerts,
/// this drives a standing now-playing display the browser-source keeps on screen until the next change, and it has
/// no richer dashboard-enriched equivalent (the dashboard music-state push carries the same track name), so it keeps
/// its own flattened handler rather than routing through <see cref="OverlayAlertBroadcast"/>.
/// </summary>
public sealed class WidgetNowPlayingHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<PlaybackStateChangedEvent>
{
    public Task HandleAsync(
        PlaybackStateChangedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "now_playing",
            new { isPlaying = @event.IsPlaying, track = @event.TrackName },
            cancellationToken
        );
}
