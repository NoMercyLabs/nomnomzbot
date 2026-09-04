// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

/// <summary>
/// Self-heals a stale <c>needs_reauth</c> the moment a broadcaster-owned WebSocket session actually welcomes
/// again (twitch-eventsub §7 / identity-auth's "never force a re-login" rule): a successful session welcome for
/// a broadcaster's OWN session can only happen with a token Twitch just accepted, so a connection still marked
/// needs_reauth from an earlier revocation/failure is provably stale and is cleared without asking the operator
/// to do anything — the same "the live signal beats the stored one" pattern S003 used for Spotify. A bot-owned
/// (chat-read) welcome carries no single tenant (<c>BroadcasterId == Guid.Empty</c>) and is ignored here.
/// </summary>
public sealed class EventSubConnectedEventHandler(
    IApplicationDbContext db,
    TimeProvider clock,
    ILogger<EventSubConnectedEventHandler> logger
) : IEventHandler<EventSubConnectedEvent>
{
    public async Task HandleAsync(
        EventSubConnectedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            IntegrationConnection? connection = await db.IntegrationConnections.FirstOrDefaultAsync(
                c => c.Provider == "twitch" && c.BroadcasterId == @event.BroadcasterId,
                cancellationToken
            );
            if (connection is null || connection.Status != AuthEnums.IntegrationStatus.NeedsReauth)
                return;

            connection.Status = AuthEnums.IntegrationStatus.Connected;
            connection.ConsecutiveFailureCount = 0;
            connection.LastErrorAt = null;
            connection.LastRefreshedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "EventSub session for {BroadcasterId} welcomed on a token Twitch just accepted — cleared a stale needs_reauth",
                @event.BroadcasterId
            );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Failed to self-heal needs_reauth on EventSub connect for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
