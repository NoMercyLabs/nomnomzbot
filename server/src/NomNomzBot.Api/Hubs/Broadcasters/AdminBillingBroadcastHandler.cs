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
