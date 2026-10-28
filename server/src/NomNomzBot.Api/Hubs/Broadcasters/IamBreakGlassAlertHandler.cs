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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// The break-glass watch (S086f — <c>IamAccessEvaluatedEvent</c> had zero consumers, so nothing ever
/// surfaced a break-glass access or a denied platform-permission attempt). The audit row itself is already
/// written synchronously by <c>PlatformIamService.AuthorizePlatformAsync</c> before this event is
/// published; this handler is the missing operator-facing attention signal on top of that row — a denied
/// evaluation or an allowed break-glass evaluation lands on the operator console log feed.
/// </summary>
public sealed class IamBreakGlassAlertHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<IamAccessEvaluatedEvent>
{
    public Task HandleAsync(IamAccessEvaluatedEvent @event, CancellationToken ct = default)
    {
        bool breakGlassWorthy =
            @event.Outcome == IamOutcome.Denied
            || @event is { Outcome: IamOutcome.Allowed, BreakGlass: true };
        if (!breakGlassWorthy)
            return Task.CompletedTask;

        return hub.Clients.All.ReceiveLog(
            new
            {
                Message = @event.Outcome == IamOutcome.Denied
                    ? $"Denied: principal {@event.PrincipalId} attempted '{@event.Permission}'"
                        + (
                            @event.TargetBroadcasterId is null
                                ? ""
                                : $" on tenant {@event.TargetBroadcasterId}"
                        )
                    : $"Break-glass: principal {@event.PrincipalId} used '{@event.Permission}'"
                        + (
                            @event.TargetBroadcasterId is null
                                ? ""
                                : $" on tenant {@event.TargetBroadcasterId}"
                        ),
                Type = @event.Outcome == IamOutcome.Denied ? "warning" : "error",
            }
        );
    }
}
