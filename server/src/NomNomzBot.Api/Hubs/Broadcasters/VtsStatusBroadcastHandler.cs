// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Vts.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Forwards <see cref="VtsStatusChangedEvent"/> to the channel's dashboards so the VTS page shows a
/// connect or failure live. A platform-level event (no channel) never reaches the hub.
/// </summary>
public sealed class VtsStatusBroadcastHandler : IEventHandler<VtsStatusChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public VtsStatusBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(VtsStatusChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendVtsStatusAsync(
            @event.BroadcasterId.ToString(),
            new(@event.BroadcasterId.ToString(), @event.Status, @event.OccurredAt.ToString("O")),
            ct
        );
    }
}
