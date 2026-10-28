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
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Billing changes reach every open operator desk live, so tier, invite, grant and invoice lists reload.</summary>
public sealed class AdminBillingBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<BillingChangedEvent>
{
    public Task HandleAsync(BillingChangedEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceiveBillingChanged(
            new
            {
                @event.Area,
                @event.Action,
                @event.SubjectId,
                @event.TargetBroadcasterId,
            }
        );
}
