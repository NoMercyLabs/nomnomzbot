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

using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts command execution results to dashboard clients.</summary>
public sealed class CommandExecutedBroadcastHandler : IEventHandler<CommandExecutedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IChannelRegistry _registry;

    public CommandExecutedBroadcastHandler(IDashboardNotifier notifier, IChannelRegistry registry)
    {
        _notifier = notifier;
        _registry = registry;
    }

    public Task HandleAsync(CommandExecutedEvent @event, CancellationToken ct = default)
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
                @event.UserId,
                @event.Succeeded,
                @event.OccurredAt.ToString("O")
            ),
            ct
        );
    }
}
