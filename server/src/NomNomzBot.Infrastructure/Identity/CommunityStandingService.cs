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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Plane-A community-standing writer (roles-permissions §3.5). A single upsert per observed viewer recomputes
/// the ladder <c>LevelValue</c> and stamps <c>LastSeenAt</c>; the change event fires only when the standing
/// actually moves, so the chat hot path stays quiet for repeat viewers at the same standing.
/// </summary>
public sealed class CommunityStandingService(
    IApplicationDbContext db,
    IEventBus eventBus,
    TimeProvider clock
) : ICommunityStandingService
{
    public async Task<Result> UpsertStandingAsync(
        Guid broadcasterId,
        Guid userId,
        CommunityStanding standing,
        StandingSource source,
        string? subTier,
        CancellationToken cancellationToken = default
    )
    {
        ChannelCommunityStanding? existing = await db.ChannelCommunityStandings.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.UserId == userId,
            cancellationToken
        );
        CommunityStanding old = existing?.Standing ?? CommunityStanding.Everyone;
        DateTime now = clock.GetUtcNow().UtcDateTime;

        if (existing is null)
        {
            db.ChannelCommunityStandings.Add(
                new ChannelCommunityStanding
                {
                    BroadcasterId = broadcasterId,
                    UserId = userId,
                    Standing = standing,
                    LevelValue = standing.ToLevel(),
                    Source = source,
                    SubTier = subTier,
                    LastSeenAt = now,
                }
            );
        }
        else
        {
            existing.Standing = standing;
            existing.LevelValue = standing.ToLevel();
            existing.Source = source;
            existing.SubTier = subTier;
            existing.LastSeenAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);

        if (old != standing)
            await eventBus.PublishAsync(
                new CommunityStandingChangedEvent
                {
                    BroadcasterId = broadcasterId,
                    TargetUserId = userId,
                    OldStanding = old,
                    NewStanding = standing,
                    Source = source,
                },
                cancellationToken
            );

        return Result.Success();
    }

    public async Task<Result<CommunityStanding>> GetStandingAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        CommunityStanding standing = await db
            .ChannelCommunityStandings.Where(s =>
                s.BroadcasterId == broadcasterId && s.UserId == userId
            )
            .Select(s => s.Standing)
            .FirstOrDefaultAsync(cancellationToken);
        return Result.Success(standing);
    }
}
