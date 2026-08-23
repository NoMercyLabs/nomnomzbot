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
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

/// <summary>
/// The consumer half of publishing <see cref="EventSubDisconnectedEvent"/> (S034): stamps
/// <c>IntegrationConnection.LastErrorAt</c> for the broadcaster whose OWN session dropped, so the dashboard's
/// connection diagnostics show the last transport hiccup even though — unlike <see cref="EventSubRevokedEvent"/>
/// — a single drop is never enough on its own to prove the token is dead (the transport is already retrying
/// with backoff, twitch-eventsub §7). <c>Status</c> is deliberately left untouched here: only a genuine
/// revocation (<see cref="EventSubRevokedEventHandler"/>) or the refresh-failure threshold
/// (<c>IIntegrationTokenVault.MarkRefreshFailureAsync</c>) earns a needs_reauth flip.
/// </summary>
public sealed class EventSubDisconnectedEventHandler(
    IApplicationDbContext db,
    ILogger<EventSubDisconnectedEventHandler> logger
) : IEventHandler<EventSubDisconnectedEvent>
{
    public async Task HandleAsync(
        EventSubDisconnectedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
        {
            logger.LogInformation(
                "EventSub bot session disconnected ({Reason}); retrying in {NextRetryIn:g}",
                @event.Reason,
                @event.NextRetryIn
            );
            return;
        }

        try
        {
            IntegrationConnection? connection = await db.IntegrationConnections.FirstOrDefaultAsync(
                c => c.Provider == "twitch" && c.BroadcasterId == @event.BroadcasterId,
                cancellationToken
            );
            if (connection is null)
                return;

            connection.LastErrorAt = @event.OccurredAt.UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "EventSub session for {BroadcasterId} disconnected ({Reason}); retrying in {NextRetryIn:g}",
                @event.BroadcasterId,
                @event.Reason,
                @event.NextRetryIn
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to record EventSub disconnect diagnostics for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
