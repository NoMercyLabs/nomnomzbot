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
