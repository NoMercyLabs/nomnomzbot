// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Forwards <see cref="ObsBridgeStateChangedEvent"/> — raised by the bridge registry on every browser-source
/// join/leave/leader move — to the channel's dashboards, so the OBS page's bridge indicator reflects
/// connect/disconnect live instead of only on a manual refresh. Mirrors the config-changed forwarder; a
/// platform-level event (no channel) never reaches the hub.
/// </summary>
public sealed class ObsBridgeStateBroadcastHandler : IEventHandler<ObsBridgeStateChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ObsBridgeStateBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ObsBridgeStateChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendObsBridgeStateAsync(
            @event.BroadcasterId.ToString(),
            new ObsBridgeStateDto(
                @event.BroadcasterId.ToString(),
                @event.InstanceCount,
                @event.HasLeader,
                @event.OccurredAt.ToString("O")
            ),
            ct
        );
    }
}
