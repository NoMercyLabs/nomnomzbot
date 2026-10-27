// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Obs.Recovery;

/// <summary>A widget page lost its connection: schedule a pass that reloads it if it does not come back by itself.</summary>
public sealed class WidgetDisconnectedRecoveryTrigger : IEventHandler<WidgetDisconnectedEvent>
{
    private readonly IObsBrowserSourceRecoveryScheduler _scheduler;

    public WidgetDisconnectedRecoveryTrigger(IObsBrowserSourceRecoveryScheduler scheduler) =>
        _scheduler = scheduler;

    public Task HandleAsync(WidgetDisconnectedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _scheduler.Schedule(@event.BroadcasterId);

        return Task.CompletedTask;
    }
}

/// <summary>The direct OBS connection came up: browser sources that loaded before the bot was reachable may be dead.</summary>
public sealed class ObsConnectedRecoveryTrigger : IEventHandler<ObsConnectionEstablishedEvent>
{
    private readonly IObsBrowserSourceRecoveryScheduler _scheduler;

    public ObsConnectedRecoveryTrigger(IObsBrowserSourceRecoveryScheduler scheduler) =>
        _scheduler = scheduler;

    public Task HandleAsync(ObsConnectionEstablishedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _scheduler.Schedule(@event.BroadcasterId);

        return Task.CompletedTask;
    }
}

/// <summary>An OBS bridge page joined the channel: the bridge is the control connection in bridge mode.</summary>
public sealed class ObsBridgeJoinedRecoveryTrigger : IEventHandler<ObsBridgeStateChangedEvent>
{
    private readonly IObsBrowserSourceRecoveryScheduler _scheduler;

    public ObsBridgeJoinedRecoveryTrigger(IObsBrowserSourceRecoveryScheduler scheduler) =>
        _scheduler = scheduler;

    public Task HandleAsync(ObsBridgeStateChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty && @event.InstanceCount > 0)
            _scheduler.Schedule(@event.BroadcasterId);

        return Task.CompletedTask;
    }
}
