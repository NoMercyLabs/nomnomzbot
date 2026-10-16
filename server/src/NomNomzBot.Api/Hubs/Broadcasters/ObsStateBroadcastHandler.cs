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
/// Forwards each <see cref="ObsDashboardStateChangedEvent"/> to the channel's dashboards as one
/// <c>ObsStateChanged</c> hub message carrying the new value, so the OBS page flips live without a refetch.
/// A platform-level event (no channel) never reaches the hub.
/// </summary>
public sealed class ObsStateBroadcastHandler : IEventHandler<ObsDashboardStateChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ObsStateBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ObsDashboardStateChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        string broadcasterId = @event.BroadcasterId.ToString();
        ObsStateChangedDto dto = new(
            broadcasterId,
            @event.Kind,
            @event.SceneName,
            @event.Active,
            @event.Paused,
            @event.InputName,
            @event.Muted,
            @event.VolumeDb,
            @event.SceneItemId,
            @event.Enabled,
            @event.Locked,
            @event.SourceName,
            @event.FilterName,
            @event.SceneNames,
            @event.Name,
            @event.Names,
            @event.OccurredAt.ToString("O")
        );

        return _notifier.SendObsStateAsync(broadcasterId, dto, ct);
    }
}
