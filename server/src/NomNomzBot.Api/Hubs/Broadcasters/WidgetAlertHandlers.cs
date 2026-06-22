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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Fans a channel domain event out to the overlay widgets (OBS browser-sources) that subscribe to it — the link
/// from a domain event to an on-stream alert over <c>OverlayHub</c>. The routing decision lives in
/// <see cref="WidgetAlertRouting"/>; this is the shared db-read + push the per-event handlers below reuse.
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
                widget.Id,
                new WidgetEventDto(widget.Id, eventType, data),
                cancellationToken
            );
    }
}

/// <summary>New subscriber → the <c>subscription</c> overlay alert.</summary>
public sealed class WidgetSubscriptionAlertHandler(
    IApplicationDbContext db,
    IWidgetNotifier notifier
) : IEventHandler<NewSubscriptionEvent>
{
    public Task HandleAsync(
        NewSubscriptionEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "subscription",
            new { user = @event.UserDisplayName, tier = @event.Tier },
            cancellationToken
        );
}

/// <summary>New follower → the <c>follow</c> overlay alert.</summary>
public sealed class WidgetFollowAlertHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<FollowEvent>
{
    public Task HandleAsync(FollowEvent @event, CancellationToken cancellationToken = default) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "follow",
            new { user = @event.UserDisplayName },
            cancellationToken
        );
}

/// <summary>Bits cheer → the <c>cheer</c> overlay alert.</summary>
public sealed class WidgetCheerAlertHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<CheerEvent>
{
    public Task HandleAsync(CheerEvent @event, CancellationToken cancellationToken = default) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "cheer",
            new
            {
                user = @event.IsAnonymous ? "Anonymous" : @event.UserDisplayName,
                amount = @event.Bits,
            },
            cancellationToken
        );
}

/// <summary>Incoming raid → the <c>raid</c> overlay alert.</summary>
public sealed class WidgetRaidAlertHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<RaidEvent>
{
    public Task HandleAsync(RaidEvent @event, CancellationToken cancellationToken = default) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "raid",
            new { user = @event.FromDisplayName, viewers = @event.ViewerCount },
            cancellationToken
        );
}

/// <summary>Gifted subs → the <c>gift</c> overlay alert.</summary>
public sealed class WidgetGiftAlertHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<GiftSubscriptionEvent>
{
    public Task HandleAsync(
        GiftSubscriptionEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "gift",
            new
            {
                user = @event.IsAnonymous ? "Anonymous" : @event.GifterDisplayName,
                tier = @event.Tier,
                amount = @event.GiftCount,
            },
            cancellationToken
        );
}

/// <summary>Resubscription → the <c>resub</c> overlay alert (with the cumulative-month milestone).</summary>
public sealed class WidgetResubAlertHandler(IApplicationDbContext db, IWidgetNotifier notifier)
    : IEventHandler<ResubscriptionEvent>
{
    public Task HandleAsync(
        ResubscriptionEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            "resub",
            new
            {
                user = @event.UserDisplayName,
                tier = @event.Tier,
                months = @event.CumulativeMonths,
            },
            cancellationToken
        );
}

/// <summary>
/// Playback change → the persistent <c>now_playing</c> overlay widget (music-sr.md). Unlike the transient alerts,
/// this drives a standing now-playing display the browser-source keeps on screen until the next change.
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
