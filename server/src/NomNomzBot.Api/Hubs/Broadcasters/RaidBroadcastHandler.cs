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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts incoming raid alerts to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class RaidBroadcastHandler : IEventHandler<RaidEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;
    private readonly IAlertQueueService _alertQueue;
    private readonly IWidgetService _widgetService;

    public RaidBroadcastHandler(
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

    public async Task HandleAsync(RaidEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        RaidAlertDto dto = new(
            @event.FromUserId,
            @event.FromDisplayName,
            @event.FromLogin,
            @event.ViewerCount
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "raid",
            dto,
            ct,
            userId: @event.FromUserId,
            userDisplayName: @event.FromDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            _alertQueue,
            _widgetService,
            @event.BroadcasterId,
            AuthEnums.Platform.Twitch,
            "raid",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}
