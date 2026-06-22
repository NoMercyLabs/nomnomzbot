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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Fans a subscription out to the channel's overlay widgets (OBS browser-sources). Each enabled widget that
/// declares <c>subscription</c> in its event subscriptions receives a <c>WidgetEvent</c> over <c>OverlayHub</c> —
/// the missing link that turns a domain event into an on-stream alert. Other alert types follow the same shape.
/// </summary>
public sealed class WidgetSubscriptionAlertHandler(
    IApplicationDbContext db,
    IWidgetNotifier notifier
) : IEventHandler<NewSubscriptionEvent>
{
    private const string EventType = "subscription";

    public async Task HandleAsync(
        NewSubscriptionEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        List<Widget> widgets = await db
            .Widgets.Where(w => w.BroadcasterId == @event.BroadcasterId)
            .ToListAsync(cancellationToken);

        object data = new { user = @event.UserDisplayName, tier = @event.Tier };
        foreach (Widget widget in WidgetAlertRouting.Subscribers(widgets, EventType))
            await notifier.SendWidgetEventAsync(
                @event.BroadcasterId.ToString(),
                widget.Id,
                new WidgetEventDto(widget.Id, EventType, data),
                cancellationToken
            );
    }
}
