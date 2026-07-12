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
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts incoming raid alerts to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class RaidBroadcastHandler : IEventHandler<RaidEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public RaidBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
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
            @event.BroadcasterId,
            "raid",
            dto,
            ct
        );
    }
}
