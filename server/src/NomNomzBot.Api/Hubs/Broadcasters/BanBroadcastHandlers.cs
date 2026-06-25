// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts user ban events to dashboard clients.</summary>
public sealed class UserBannedBroadcastHandler : IEventHandler<UserBannedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public UserBannedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(UserBannedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendModActionAsync(
            @event.BroadcasterId.ToString(),
            new("ban", @event.ModeratorUserId, @event.TargetUserId, @event.Reason, null),
            ct
        );
    }
}

/// <summary>Broadcasts user timeout events to dashboard clients.</summary>
public sealed class UserTimedOutBroadcastHandler : IEventHandler<UserTimedOutEvent>
{
    private readonly IDashboardNotifier _notifier;

    public UserTimedOutBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(UserTimedOutEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendModActionAsync(
            @event.BroadcasterId.ToString(),
            new(
                "timeout",
                @event.ModeratorUserId,
                @event.TargetUserId,
                @event.Reason,
                @event.DurationSeconds
            ),
            ct
        );
    }
}

/// <summary>Broadcasts user unban events to dashboard clients.</summary>
public sealed class UserUnbannedBroadcastHandler : IEventHandler<UserUnbannedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public UserUnbannedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(UserUnbannedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendModActionAsync(
            @event.BroadcasterId.ToString(),
            new("unban", @event.ModeratorUserId, @event.TargetUserId, null, null),
            ct
        );
    }
}
