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
    private readonly NomNomzBot.Application.Contracts.Security.IOutboundSanctionAccessor _sanctions;
    private readonly ILogger<DiscordLiveRoleOfflineHandler> _logger;

    public DiscordLiveRoleOfflineHandler(
        IDiscordLiveRoleService liveRoleService,
        NomNomzBot.Application.Contracts.Security.IOutboundSanctionAccessor sanctions,
        ILogger<DiscordLiveRoleOfflineHandler> logger
    )
    {
        _liveRoleService = liveRoleService;
        _sanctions = sanctions;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChannelOfflineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        // Assigning a role on somebody's Discord server is the same class of act as granting a Twitch
        // moderator. It is allowed here because the broadcaster configured a live role, and the sanction
        // names that setting so the change is traceable to it.
        using IDisposable sanction = _sanctions.Begin(
            NomNomzBot.Application.Contracts.Security.OutboundSanction.ChannelConfiguration(
                "discord_live_role"
            )
        );

        try
        {
            await _liveRoleService.RemoveForOfflineAsync(@event.BroadcasterId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Discord live-role remove threw for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
