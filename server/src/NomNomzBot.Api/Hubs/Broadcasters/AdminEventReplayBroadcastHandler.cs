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

using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.EventStore.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A replay run changed state, so every open operator event replay page reloads its list of executed
/// replays. The payload names the channel, the projection, the status and how many events were applied.
/// </summary>
public sealed class AdminEventReplayBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<ReplayStatusChangedEvent>
{
    public Task HandleAsync(ReplayStatusChangedEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceiveEventReplayChanged(
            new
            {
                @event.BroadcasterId,
                @event.ProjectionName,
                @event.Status,
                @event.ProcessedCount,
            }
        );
}
