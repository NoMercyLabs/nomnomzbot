// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
