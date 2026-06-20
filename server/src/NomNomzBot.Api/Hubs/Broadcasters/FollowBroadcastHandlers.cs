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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts follow alerts to dashboard/overlay clients.</summary>
public sealed class FollowBroadcastHandler : IEventHandler<FollowEvent>
{
    private readonly IDashboardNotifier _notifier;

    public FollowBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(FollowEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "follow",
            new FollowAlertDto(
                @event.UserId,
                @event.UserDisplayName,
                @event.UserLogin,
                @event.FollowedAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts new follower alerts (IRC fallback path) to dashboard/overlay clients.</summary>
public sealed class NewFollowerBroadcastHandler : IEventHandler<NewFollowerEvent>
{
    private readonly IDashboardNotifier _notifier;

    public NewFollowerBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(NewFollowerEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "follow",
            new FollowAlertDto(@event.UserId, @event.UserDisplayName, @event.UserLogin, null),
            ct
        );
    }
}
