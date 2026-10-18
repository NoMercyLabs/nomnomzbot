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
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Platform access changes reach every open operator access page live, so the principals and roles reload.</summary>
public sealed class AdminAccessBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<PlatformAccessChangedEvent>
{
    public Task HandleAsync(PlatformAccessChangedEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceiveAccessChanged(
            new
            {
                @event.Action,
                @event.PrincipalId,
                @event.AssignmentId,
            }
        );
}
