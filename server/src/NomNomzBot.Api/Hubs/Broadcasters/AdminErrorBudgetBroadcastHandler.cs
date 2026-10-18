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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A resolved outbound webhook attempt moves one tenant's error budget, so every open operator desk reloads
/// it live. A pending attempt has not resolved yet and never counts, so it pushes nothing.
/// </summary>
public sealed class AdminErrorBudgetBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<OutboundWebhookAttemptedEvent>
{
    public Task HandleAsync(OutboundWebhookAttemptedEvent @event, CancellationToken ct = default) =>
        @event.Status == WebhookDeliveryStatus.Pending
            ? Task.CompletedTask
            : hub.Clients.All.ReceiveErrorBudgetChanged(
                new { @event.BroadcasterId, Status = @event.Status.ToString() }
            );
}
