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
/// Broadcasts a title/category change (<c>channel.update</c>) to dashboard clients, so the stream-info card
/// live-updates. <see cref="NomNomzBot.Infrastructure.Stream.EventHandlers.ChannelUpdatedHandler"/> is the
/// sibling read-model handler that persists the same event to <c>Channel.Title</c>/<c>Channel.GameName</c>.
/// </summary>
public sealed class ChannelUpdatedBroadcastHandler : IEventHandler<ChannelUpdatedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ChannelUpdatedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ChannelUpdatedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        StreamInfoChangedDto dto = new(
            BroadcasterId: @event.BroadcasterId.ToString(),
            BroadcasterDisplayName: @event.BroadcasterDisplayName,
            Title: @event.NewTitle,
            GameName: @event.NewGameName
        );

        return _notifier.SendStreamInfoChangedAsync(@event.BroadcasterId.ToString(), dto, ct);
    }
}
