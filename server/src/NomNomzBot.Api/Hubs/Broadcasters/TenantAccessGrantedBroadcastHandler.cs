// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Tells the tenant owner an operator gained support access to THEIR channel (S086f — the grant used to
/// happen silently). Pushed as a dashboard alert to the affected tenant only, never platform-wide.
/// </summary>
public sealed class TenantAccessGrantedBroadcastHandler(IDashboardNotifier notifier)
    : IEventHandler<TenantAccessGrantedEvent>
{
    public Task HandleAsync(TenantAccessGrantedEvent @event, CancellationToken ct = default) =>
        notifier.SendAlertAsync(
            @event.TargetBroadcasterId.ToString(),
            new(
                "tenant_access_granted",
                @event.BreakGlass
                    ? "A NomNomzBot operator used break-glass access on your channel."
                    : "A NomNomzBot operator was granted temporary support access to your channel.",
                new
                {
                    @event.PrincipalId,
                    @event.AccessGrantId,
                    @event.BreakGlass,
                    @event.ExpiresAt,
                }
            ),
            ct
        );
}
