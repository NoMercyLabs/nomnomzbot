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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

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
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        StreamStatusDto dto = new(
            IsLive: true,
            StreamId: null,
            Title: @event.StreamTitle,
            GameName: @event.GameName,
            StartedAt: @event.StartedAt.ToString("O")
        );

        return _notifier.SendStreamStatusAsync(@event.BroadcasterId.ToString(), dto, ct);
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
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        StreamStatusDto dto = new(
            IsLive: false,
            StreamId: null,
            Title: null,
            GameName: null,
            StartedAt: null
        );

        return _notifier.SendStreamStatusAsync(@event.BroadcasterId.ToString(), dto, ct);
    }
}
