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
