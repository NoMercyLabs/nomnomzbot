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
