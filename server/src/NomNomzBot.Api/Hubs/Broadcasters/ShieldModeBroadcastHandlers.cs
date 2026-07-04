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

/// <summary>Broadcasts Shield Mode activation (<c>channel.shield_mode.begin</c>) to dashboard clients.</summary>
public sealed class ShieldModeBeganBroadcastHandler : IEventHandler<ShieldModeBeganEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ShieldModeBeganBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ShieldModeBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "shield_mode_begin",
            new ShieldModeBeganAlertDto(
                @event.ModeratorId,
                @event.ModeratorDisplayName,
                @event.StartedAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts Shield Mode deactivation (<c>channel.shield_mode.end</c>) to dashboard clients.</summary>
public sealed class ShieldModeEndedBroadcastHandler : IEventHandler<ShieldModeEndedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ShieldModeEndedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ShieldModeEndedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "shield_mode_end",
            new ShieldModeEndedAlertDto(
                @event.ModeratorId,
                @event.ModeratorDisplayName,
                @event.EndedAt
            ),
            ct
        );
    }
}
