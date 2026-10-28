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
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A scheduled job was retried, so every open operator scheduled jobs page reloads its queue. The payload
/// names the change and both task ids.
/// </summary>
public sealed class AdminScheduledJobsBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<ScheduledJobsChangedEvent>
{
    public Task HandleAsync(ScheduledJobsChangedEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceiveScheduledJobsChanged(
            new
            {
                @event.Change,
                @event.OriginalTaskId,
                @event.NewTaskId,
            }
        );
}
