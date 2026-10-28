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

using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Forwards <see cref="ObsConnectionEstablishedEvent"/> — raised the instant the direct OBS WebSocket
/// connection (re)establishes, carrying the real current stream/record status — to the channel's
/// dashboards, so the OBS control page reflects a pre-existing live/recording session immediately
/// instead of waiting for a future start/stop event. Mirrors the bridge-state forwarder; a
/// platform-level event (no channel) never reaches the hub.
/// </summary>
public sealed class ObsConnectionEstablishedBroadcastHandler
    : IEventHandler<ObsConnectionEstablishedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ObsConnectionEstablishedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(ObsConnectionEstablishedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.SendObsLiveStateAsync(
            @event.BroadcasterId.ToString(),
            new(
                @event.BroadcasterId.ToString(),
                @event.Streaming,
                @event.Recording,
                @event.OccurredAt.ToString("O")
            ),
            ct
        );
    }
}
