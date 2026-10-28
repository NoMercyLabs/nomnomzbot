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

/// <summary>
/// Every evaluated platform permission writes one audit row, so every open operator desk reloads the audit
/// log live. The audit page's own read (audit:read) writes a row too and pushes nothing, otherwise the page
/// would reload itself forever.
/// </summary>
public sealed class AdminAuditBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<IamAccessEvaluatedEvent>
{
    private const string AuditReadPermission = "audit:read";

    public Task HandleAsync(IamAccessEvaluatedEvent @event, CancellationToken ct = default) =>
        @event.Permission == AuditReadPermission
            ? Task.CompletedTask
            : hub.Clients.All.ReceiveAuditChanged(
                new
                {
                    @event.PrincipalId,
                    @event.Permission,
                    Outcome = @event.Outcome.ToString(),
                    @event.TargetBroadcasterId,
                }
            );
}
