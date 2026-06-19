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
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Broadcasts StreamStatusChanged to all dashboard clients when a stream goes online.
/// </summary>
public sealed class ChannelOnlineBroadcastHandler : IEventHandler<ChannelOnlineEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ChannelOnlineBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ChannelOnlineEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.BroadcasterId))
            return Task.CompletedTask;

        StreamStatusDto dto = new(
            IsLive: true,
            StreamId: null,
            Title: @event.StreamTitle,
            GameName: @event.GameName,
            StartedAt: @event.StartedAt.ToString("O")
        );

        return _notifier.SendStreamStatusAsync(@event.BroadcasterId, dto, ct);
    }
}

/// <summary>
/// Broadcasts StreamStatusChanged to all dashboard clients when a stream goes offline.
/// </summary>
public sealed class ChannelOfflineBroadcastHandler : IEventHandler<ChannelOfflineEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ChannelOfflineBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ChannelOfflineEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.BroadcasterId))
            return Task.CompletedTask;

        StreamStatusDto dto = new(
            IsLive: false,
            StreamId: null,
            Title: null,
            GameName: null,
            StartedAt: null
        );

        return _notifier.SendStreamStatusAsync(@event.BroadcasterId, dto, ct);
    }
}
