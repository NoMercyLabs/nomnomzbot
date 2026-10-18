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
