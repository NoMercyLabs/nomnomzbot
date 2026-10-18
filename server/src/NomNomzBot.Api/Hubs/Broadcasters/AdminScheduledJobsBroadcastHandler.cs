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
