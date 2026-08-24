// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>
/// The offline half of the Discord "currently live" role trigger (discord.md live-role extension) — removes
/// the role from every currently-applied live-role rule for the channel that just went offline. Best-effort,
/// mirrors <see cref="DiscordGoLiveNotificationHandler"/>: a Discord-side failure never disturbs the live flow
/// and is logged, never silently swallowed. If this event is ever missed (bot restart/crash), the startup
/// reconciler (<see cref="IDiscordLiveRoleService.ReconcileStaleAsync"/>) clears the stranded role.
/// </summary>
public sealed class DiscordLiveRoleOfflineHandler : IEventHandler<ChannelOfflineEvent>
{
    private readonly IDiscordLiveRoleService _liveRoleService;
    private readonly ILogger<DiscordLiveRoleOfflineHandler> _logger;

    public DiscordLiveRoleOfflineHandler(
        IDiscordLiveRoleService liveRoleService,
        ILogger<DiscordLiveRoleOfflineHandler> logger
    )
    {
        _liveRoleService = liveRoleService;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChannelOfflineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            await _liveRoleService.RemoveForOfflineAsync(@event.BroadcasterId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Discord live-role remove threw for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
