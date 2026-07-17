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
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Feeds the operator hub (<see cref="AdminHub"/>): channel go-live/offline flips update the live channel
/// registry view, and tenant suspensions land as both a registry update and an operator log line. Only
/// platform principals can be connected (the hub's <c>iam:manage</c> handshake gate), so pushes go to all.
/// </summary>
public sealed class AdminChannelOnlineBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<ChannelOnlineEvent>
{
    public Task HandleAsync(ChannelOnlineEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceiveChannelRegistryUpdate(
            new
            {
                BroadcasterId = @event.BroadcasterId,
                ChannelName = @event.BroadcasterDisplayName,
                IsLive = true,
                @event.StreamTitle,
                @event.GameName,
            }
        );
}

/// <summary>The offline half of the operator registry feed.</summary>
public sealed class AdminChannelOfflineBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<ChannelOfflineEvent>
{
    public Task HandleAsync(ChannelOfflineEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceiveChannelRegistryUpdate(
            new
            {
                BroadcasterId = @event.BroadcasterId,
                ChannelName = @event.BroadcasterDisplayName,
                IsLive = false,
            }
        );
}

/// <summary>Tenant suspensions reach the operator console live — registry update + a log line.</summary>
public sealed class AdminTenantSuspensionBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<TenantSuspensionChangedEvent>
{
    public async Task HandleAsync(
        TenantSuspensionChangedEvent @event,
        CancellationToken ct = default
    )
    {
        await hub.Clients.All.ReceiveChannelRegistryUpdate(
            new { BroadcasterId = @event.TargetBroadcasterId, Status = @event.NewStatus }
        );
        await hub.Clients.All.ReceiveLog(
            new
            {
                Message = $"Tenant {@event.TargetBroadcasterId} → {@event.NewStatus}"
                    + (@event.Reason is null ? "" : $" ({@event.Reason})"),
                Type = @event.NewStatus == "active" ? "success" : "warning",
            }
        );
    }
}
