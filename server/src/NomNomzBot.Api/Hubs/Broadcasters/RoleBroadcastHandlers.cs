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
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts moderator role grants (<c>channel.moderator.add</c>) to dashboard clients.</summary>
public sealed class ModeratorAddedBroadcastHandler : IEventHandler<ModeratorAddedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ModeratorAddedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ModeratorAddedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "moderator_added",
            new RoleChangedAlertDto(@event.UserId, @event.UserDisplayName, @event.UserLogin),
            ct
        );
    }
}

/// <summary>Broadcasts moderator role revocations (<c>channel.moderator.remove</c>) to dashboard clients.</summary>
public sealed class ModeratorRemovedBroadcastHandler : IEventHandler<ModeratorRemovedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ModeratorRemovedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ModeratorRemovedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "moderator_removed",
            new RoleChangedAlertDto(@event.UserId, @event.UserDisplayName, @event.UserLogin),
            ct
        );
    }
}

/// <summary>Broadcasts VIP role grants (<c>channel.vip.add</c>) to dashboard clients.</summary>
public sealed class VipAddedBroadcastHandler : IEventHandler<VipAddedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public VipAddedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(VipAddedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "vip_added",
            new RoleChangedAlertDto(@event.UserId, @event.UserDisplayName, @event.UserLogin),
            ct
        );
    }
}

/// <summary>Broadcasts VIP role revocations (<c>channel.vip.remove</c>) to dashboard clients.</summary>
public sealed class VipRemovedBroadcastHandler : IEventHandler<VipRemovedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public VipRemovedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(VipRemovedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "vip_removed",
            new RoleChangedAlertDto(@event.UserId, @event.UserDisplayName, @event.UserLogin),
            ct
        );
    }
}
