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
