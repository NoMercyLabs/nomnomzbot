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
/// The Discord "currently live" role trigger (discord.md live-role extension). Mirrors
/// <see cref="DiscordGoLiveNotificationHandler"/>'s shape: best-effort, never disturbs the live flow. Uses the
/// same per-session dedupe key (the stream start instant) so a duplicate <see cref="ChannelOnlineEvent"/> for
/// the same session does not re-apply the role.
/// </summary>
public sealed class DiscordLiveRoleOnlineHandler : IEventHandler<ChannelOnlineEvent>
{
    private const string Trigger = "live_role";

    private readonly IDiscordLiveRoleService _liveRoleService;
    private readonly ILogger<DiscordLiveRoleOnlineHandler> _logger;

    public DiscordLiveRoleOnlineHandler(
        IDiscordLiveRoleService liveRoleService,
        ILogger<DiscordLiveRoleOnlineHandler> logger
    )
    {
        _liveRoleService = liveRoleService;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChannelOnlineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        string dedupeKey = $"{Trigger}:{@event.StartedAt.UtcDateTime:O}";

        try
        {
            await _liveRoleService.ApplyForOnlineAsync(
                @event.BroadcasterId,
                dedupeKey,
                cancellationToken
            );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort like the go-live notification handler — a Discord-side failure must never disturb
            // the live flow; it is recorded here, never silently swallowed.
            _logger.LogWarning(
                ex,
                "Discord live-role apply threw for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
