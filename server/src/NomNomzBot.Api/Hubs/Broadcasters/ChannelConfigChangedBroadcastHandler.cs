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

using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// The single broadcast handler for EVERY config-CRUD domain (E5): forwards <see cref="ChannelConfigChangedEvent"/>
/// to the dashboard as one generic hub push instead of a bespoke event/handler per domain. The receiving client
/// just refetches <c>Domain</c>'s query.
/// </summary>
public sealed class ChannelConfigChangedBroadcastHandler : IEventHandler<ChannelConfigChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ChannelConfigChangedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(ChannelConfigChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendConfigChangedAsync(
            @event.BroadcasterId.ToString(),
            new(@event.BroadcasterId.ToString(), @event.Domain, @event.EntityId, @event.Action),
            ct
        );
    }
}
