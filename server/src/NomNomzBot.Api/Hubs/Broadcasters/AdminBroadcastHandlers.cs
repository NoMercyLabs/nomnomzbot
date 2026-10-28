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
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Events;
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
                @event.BroadcasterId,
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
                @event.BroadcasterId,
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

/// <summary>Trust and safety desk changes reach every open operator desk live, so a queue or block list reloads.</summary>
public sealed class AdminTrustSafetyBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<TrustSafetyChangedEvent>
{
    public Task HandleAsync(TrustSafetyChangedEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceiveTrustSafetyChanged(
            new
            {
                @event.Area,
                @event.Action,
                @event.SubjectId,
            }
        );
}

/// <summary>Platform content catalogue changes reach every open operator content page live, so the list reloads.</summary>
public sealed class AdminContentBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<PlatformContentChangedEvent>
{
    public Task HandleAsync(PlatformContentChangedEvent @event, CancellationToken ct = default) =>
        hub.Clients.All.ReceiveContentChanged(new { @event.Action, @event.DefinitionId });
}
