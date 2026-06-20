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
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts command execution results to dashboard clients.</summary>
public sealed class CommandExecutedBroadcastHandler : IEventHandler<AfterCommandExecutedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IChannelRegistry _registry;

    public CommandExecutedBroadcastHandler(IDashboardNotifier notifier, IChannelRegistry registry)
    {
        _notifier = notifier;
        _registry = registry;
    }

    public Task HandleAsync(AfterCommandExecutedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        if (@event.Succeeded)
        {
            ChannelContext? ctx = _registry.Get(@event.BroadcasterId);
            if (ctx is not null)
            {
                lock (ctx.Lock)
                    ctx.CommandsUsed++;
            }
        }

        return _notifier.SendCommandExecutedAsync(
            @event.BroadcasterId.ToString(),
            new(
                @event.BroadcasterId.ToString(),
                @event.CommandName,
                @event.TriggeredByUserId,
                @event.Succeeded,
                @event.Timestamp.ToString("O")
            ),
            ct
        );
    }
}
