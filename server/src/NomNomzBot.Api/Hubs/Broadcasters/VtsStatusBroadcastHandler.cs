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
