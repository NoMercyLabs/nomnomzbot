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
using NomNomzBot.Application.Alerts.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts cheer/bits alerts to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class CheerBroadcastHandler : IEventHandler<CheerEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;
    private readonly IAlertQueueService _alertQueue;
    private readonly IWidgetService _widgetService;

    public CheerBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets,
        IAlertQueueService alertQueue,
        IWidgetService widgetService
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
        _alertQueue = alertQueue;
        _widgetService = widgetService;
    }

    public async Task HandleAsync(CheerEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        CheerAlertDto dto = new(
            @event.IsAnonymous ? null : @event.UserId,
            @event.IsAnonymous ? "Anonymous" : @event.UserDisplayName,
            @event.Bits,
            @event.Message,
            @event.IsAnonymous
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "cheer",
            dto,
            ct,
            userId: @event.IsAnonymous ? null : @event.UserId,
            userDisplayName: @event.IsAnonymous ? "Anonymous" : @event.UserDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            _alertQueue,
            _widgetService,
            @event.BroadcasterId,
            @event.Provider,
            "cheer",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}
