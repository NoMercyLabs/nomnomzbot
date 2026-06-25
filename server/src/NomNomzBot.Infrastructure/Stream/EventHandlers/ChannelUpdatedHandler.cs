// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Stream.EventHandlers;

/// <summary>
/// Updates Channel.Title and Channel.GameName when the channel info changes.
/// </summary>
public sealed class ChannelUpdatedHandler : IEventHandler<ChannelUpdatedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChannelUpdatedHandler> _logger;

    public ChannelUpdatedHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<ChannelUpdatedHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChannelUpdatedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Channel? channel = await db.Channels.FindAsync([broadcasterId], cancellationToken);
        if (channel is null)
            return;

        channel.Title = @event.NewTitle;
        channel.GameName = @event.NewGameName;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Channel {BroadcasterId} updated: title={Title}, game={Game}",
            broadcasterId,
            @event.NewTitle,
            @event.NewGameName
        );
    }
}
