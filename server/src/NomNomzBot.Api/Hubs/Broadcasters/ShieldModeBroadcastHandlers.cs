// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
