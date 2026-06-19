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
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts integration connection events (Spotify, Discord, OBS) as channel events.</summary>
public sealed class IntegrationConnectedBroadcastHandler : IEventHandler<IntegrationConnectedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public IntegrationConnectedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(IntegrationConnectedEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.BroadcasterId))
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId,
            "integration_connected",
            new IntegrationEventDto(@event.IntegrationName),
            ct
        );
    }
}

/// <summary>Broadcasts integration disconnection events as dashboard alerts.</summary>
public sealed class IntegrationDisconnectedBroadcastHandler
    : IEventHandler<IntegrationDisconnectedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public IntegrationDisconnectedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(IntegrationDisconnectedEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.BroadcasterId))
            return Task.CompletedTask;

        return _notifier.SendAlertAsync(
            @event.BroadcasterId,
            new(
                "integration_disconnected",
                $"{@event.IntegrationName} disconnected",
                new IntegrationEventDto(@event.IntegrationName)
            ),
            ct
        );
    }
}
