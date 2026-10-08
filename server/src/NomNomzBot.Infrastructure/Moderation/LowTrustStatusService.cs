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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Stores Twitch's suspicious-user flag per chatter and channel. A row exists only while the flag is
/// <c>active_monitoring</c> or <c>restricted</c>; <c>none</c> deletes it. A real change pushes the
/// <c>suspicious-users</c> config domain so an open dashboard refetches.
/// </summary>
public sealed class LowTrustStatusService(
    IApplicationDbContext db,
    IEventBus eventBus,
    TimeProvider timeProvider
) : ILowTrustStatusService
{
    public async Task<bool> ApplyAsync(
        Guid broadcasterId,
        string twitchUserId,
        string status,
        string? banEvasionEvaluation,
        CancellationToken cancellationToken = default
    )
    {
        string normalized = LowTrustStatuses.Normalize(status);
        ChannelLowTrustStatus? row = await db.ChannelLowTrustStatuses.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.TwitchUserId == twitchUserId,
            cancellationToken
        );
        string previous = row?.Status ?? LowTrustStatuses.None;

        if (normalized == LowTrustStatuses.None)
        {
            if (row is null)
                return false;
            db.ChannelLowTrustStatuses.Remove(row);
        }
        else if (row is null)
        {
            db.ChannelLowTrustStatuses.Add(
                new ChannelLowTrustStatus
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = broadcasterId,
                    TwitchUserId = twitchUserId,
                    Status = normalized,
                    BanEvasionEvaluation = banEvasionEvaluation,
                    UpdatedAt = timeProvider.GetUtcNow().UtcDateTime,
                }
            );
        }
        else
        {
            row.Status = normalized;
            row.BanEvasionEvaluation = banEvasionEvaluation ?? row.BanEvasionEvaluation;
            row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (previous == normalized)
            return false;

        await eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "suspicious-users",
                EntityId = twitchUserId,
                Action = "updated",
            },
            cancellationToken
        );
        return true;
    }

    public async Task<string> GetAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken cancellationToken = default
    )
    {
        string? status = await db
            .ChannelLowTrustStatuses.AsNoTracking()
            .Where(s => s.BroadcasterId == broadcasterId && s.TwitchUserId == twitchUserId)
            .Select(s => s.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return status ?? LowTrustStatuses.None;
    }
}
