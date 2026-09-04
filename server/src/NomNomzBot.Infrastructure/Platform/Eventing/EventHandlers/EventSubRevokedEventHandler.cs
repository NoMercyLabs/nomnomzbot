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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

/// <summary>
/// The reactive half of S034: before this handler existed, a Twitch-side revocation of a subscription left the
/// tenant's Twitch <see cref="IntegrationConnection"/> reading <c>connected</c> forever — the exact "needs_reauth
/// classified from a dead auth signal" gap S003 closed for Spotify, now closed here for EventSub. Only the two
/// statuses that mean the AUTHORIZATION itself is gone (<c>authorization_revoked</c>, <c>user_removed</c>) flip
/// the connection; <c>version_removed</c> is an API-version housekeeping revocation, not an auth loss, and is
/// left alone (a future reconcile simply re-subscribes on the current version).
/// </summary>
public sealed class EventSubRevokedEventHandler(
    IApplicationDbContext db,
    IChatProvider chatProvider,
    TimeProvider clock,
    ILogger<EventSubRevokedEventHandler> logger
) : IEventHandler<EventSubRevokedEvent>
{
    private static readonly HashSet<string> AuthDeadStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization_revoked",
        "user_removed",
    };

    public async Task HandleAsync(
        EventSubRevokedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || !AuthDeadStatuses.Contains(@event.Status))
            return;

        try
        {
            IntegrationConnection? connection = await db.IntegrationConnections.FirstOrDefaultAsync(
                c => c.Provider == "twitch" && c.BroadcasterId == @event.BroadcasterId,
                cancellationToken
            );
            if (connection is null || connection.Status == AuthEnums.IntegrationStatus.NeedsReauth)
                return;

            connection.Status = AuthEnums.IntegrationStatus.NeedsReauth;
            connection.ConsecutiveFailureCount = Math.Max(connection.ConsecutiveFailureCount, 1);
            connection.LastErrorAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "EventSub revocation ({EventType}/{Status}) flipped {BroadcasterId}'s Twitch connection to needs_reauth",
                @event.EventType,
                @event.Status,
                @event.BroadcasterId
            );

            // Best-effort: the bot's own token is independent of the just-revoked broadcaster grant, so the
            // notice can still land even though the very feature it announces the loss of just went dark.
            await chatProvider.SendMessageAsync(
                @event.BroadcasterId,
                "⚠️ NomNomzBot lost access to your Twitch account (authorization was revoked) — chat and event features will stop updating until you reconnect from the dashboard.",
                cancellationToken
            );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Failed to handle EventSub revocation for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
