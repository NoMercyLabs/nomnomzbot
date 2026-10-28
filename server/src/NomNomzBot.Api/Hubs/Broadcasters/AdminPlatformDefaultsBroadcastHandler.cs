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
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A platform default was saved, so every open operator platform defaults page reloads. The payload names the
/// default set and the saved row.
/// </summary>
public sealed class AdminPlatformDefaultsBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<PlatformDefaultsChangedEvent>
{
    public Task HandleAsync(PlatformDefaultsChangedEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceivePlatformDefaultsChanged(new { @event.Set, @event.Key });
}
