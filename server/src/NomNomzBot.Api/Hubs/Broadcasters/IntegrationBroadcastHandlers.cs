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
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "integration_connected",
            new IntegrationEventDto(@event.Provider),
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
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendAlertAsync(
            @event.BroadcasterId.ToString(),
            new(
                "integration_disconnected",
                $"{@event.Provider} disconnected",
                new IntegrationEventDto(@event.Provider)
            ),
            ct
        );
    }
}
