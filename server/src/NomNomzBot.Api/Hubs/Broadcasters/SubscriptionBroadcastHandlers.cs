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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts new subscription alerts to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class NewSubscriptionBroadcastHandler : IEventHandler<NewSubscriptionEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public NewSubscriptionBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(NewSubscriptionEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        SubscriptionAlertDto dto = new(@event.UserId, @event.UserDisplayName, @event.Tier);

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "subscription",
            dto,
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "subscription",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts resubscription alerts to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class ResubscriptionBroadcastHandler : IEventHandler<ResubscriptionEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public ResubscriptionBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(ResubscriptionEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        ResubAlertDto dto = new(
            @event.UserId,
            @event.UserDisplayName,
            @event.Tier,
            @event.CumulativeMonths,
            @event.StreakMonths,
            @event.Message
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "resub",
            dto,
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "resub",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts gift subscription alerts to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class GiftSubscriptionBroadcastHandler : IEventHandler<GiftSubscriptionEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public GiftSubscriptionBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(GiftSubscriptionEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        GiftSubAlertDto dto = new(
            @event.IsAnonymous ? null : @event.GifterUserId,
            @event.IsAnonymous ? "Anonymous" : @event.GifterDisplayName,
            @event.Tier,
            @event.GiftCount,
            @event.IsAnonymous
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "gift",
            dto,
            ct,
            userId: @event.IsAnonymous ? null : @event.GifterUserId,
            userDisplayName: @event.IsAnonymous ? "Anonymous" : @event.GifterDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "gift",
            dto,
            ct
        );
    }
}
