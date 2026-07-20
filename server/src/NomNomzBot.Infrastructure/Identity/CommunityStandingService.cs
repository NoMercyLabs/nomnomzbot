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

    public async Task<Result> ReconcileTwitchStandingsAsync(
        Guid broadcasterId,
        CommunityStandingSnapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        // A lapse downgrade is safe ONLY when both signals were read completely — otherwise a user absent from this
        // run might still be a sub/VIP via the signal we could not read.
        bool fullyAuthoritative = snapshot.SubscribersAuthoritative && snapshot.VipsAuthoritative;

        HashSet<Guid> snapshotUsers = [.. snapshot.Members.Select(m => m.UserId)];

        // The rows this reconcile could touch: any existing standing for a reported user (to decide up/down without
        // clobbering a higher non-owned standing), plus every Helix-seeded Subscriber/Vip row (a lapse-downgrade
        // candidate). One query.
        List<ChannelCommunityStanding> rows = await db
            .ChannelCommunityStandings.Where(s =>
                s.BroadcasterId == broadcasterId
                && (
                    snapshotUsers.Contains(s.UserId)
                    || (
                        s.Source == StandingSource.HelixSeed
                        && (
                            s.Standing == CommunityStanding.Subscriber
                            || s.Standing == CommunityStanding.Vip
                        )
                    )
                )
            )
            .ToListAsync(cancellationToken);
        Dictionary<Guid, ChannelCommunityStanding> rowByUser = rows.ToDictionary(s => s.UserId);

        List<(Guid UserId, CommunityStanding Old, CommunityStanding New)> deltas = [];

        // 1. Raise/refresh every user Twitch reports as a sub/VIP to their Twitch standing.
        foreach (TwitchStandingMember member in snapshot.Members)
        {
            rowByUser.TryGetValue(member.UserId, out ChannelCommunityStanding? row);
            CommunityStanding? apply = StandingToApply(
                row?.Standing,
                row?.Source,
                member.Standing,
                fullyAuthoritative
            );
            if (apply is not CommunityStanding target)
                continue; // leave the existing row untouched (higher non-owned standing, or a partial-read lower)

            if (row is null)
            {
                db.ChannelCommunityStandings.Add(
                    new ChannelCommunityStanding
                    {
                        BroadcasterId = broadcasterId,
                        UserId = member.UserId,
                        Standing = target,
                        LevelValue = target.ToLevel(),
                        Source = StandingSource.HelixSeed,
                        SubTier = target == CommunityStanding.Subscriber ? member.SubTier : null,
                        LastSeenAt = now,
                    }
                );
                deltas.Add((member.UserId, CommunityStanding.Everyone, target));
            }
            else
            {
                CommunityStanding old = row.Standing;
                row.Standing = target;
                row.LevelValue = target.ToLevel();
                row.Source = StandingSource.HelixSeed;
                row.SubTier = target == CommunityStanding.Subscriber ? member.SubTier : null;
                row.LastSeenAt = now;
                if (old != target)
                    deltas.Add((member.UserId, old, target));
            }
        }

        // 2. Lapse downgrade: an owned Subscriber/Vip row for a user Twitch no longer reports falls back to Everyone
        //    (soft — the row is kept, never DELETEd). Only on a fully-authoritative run.
        if (fullyAuthoritative)
        {
            foreach (ChannelCommunityStanding row in rows)
            {
                if (snapshotUsers.Contains(row.UserId) || !IsReconcilable(row.Source, row.Standing))
                    continue;
                CommunityStanding old = row.Standing;
                row.Standing = CommunityStanding.Everyone;
                row.LevelValue = CommunityStanding.Everyone.ToLevel();
                row.SubTier = null;
                row.LastSeenAt = now;
                deltas.Add((row.UserId, old, CommunityStanding.Everyone));
            }
        }

        if (deltas.Count == 0)
            return Result.Success();

        await db.SaveChangesAsync(cancellationToken);

        foreach ((Guid userId, CommunityStanding old, CommunityStanding @new) in deltas)
            await eventBus.PublishAsync(
                new CommunityStandingChangedEvent
                {
                    BroadcasterId = broadcasterId,
                    TargetUserId = userId,
                    OldStanding = old,
                    NewStanding = @new,
                    Source = StandingSource.HelixSeed,
                },
                cancellationToken
            );

        return Result.Success();
    }

    /// <summary>
    /// Whether a standing row is one the Twitch reconcile OWNS and may downgrade: a Helix-roster-seeded Subscriber or
    /// Vip. Artist, Moderator, and any non-<see cref="StandingSource.HelixSeed"/> standing are outside its authority
    /// and never touched.
    /// </summary>
    internal static bool IsReconcilable(StandingSource source, CommunityStanding standing) =>
        source == StandingSource.HelixSeed
        && (standing == CommunityStanding.Subscriber || standing == CommunityStanding.Vip);

    /// <summary>
    /// The standing to persist for a user Twitch reports at <paramref name="desired"/>, or <c>null</c> to LEAVE the
    /// existing row untouched. Never clobbers a higher, non-owned standing (Artist / Moderator / manual): such a row
    /// is taken over only to RAISE a strictly-lower one (e.g. a dormant Everyone). For an owned Subscriber/Vip row
    /// Twitch is authoritative on a full read (up or down); on a partial read it only RAISES — a signal that could
    /// not be read might still hold a higher standing, so a lower reading is not trusted to downgrade.
    /// </summary>
    internal static CommunityStanding? StandingToApply(
        CommunityStanding? current,
        StandingSource? currentSource,
        CommunityStanding desired,
        bool fullyAuthoritative
    )
    {
        if (current is not CommunityStanding cur)
            return desired; // no row yet — create at the Twitch standing

        int curLevel = cur.ToLevel();
        int desLevel = desired.ToLevel();

        if (currentSource is not StandingSource src || !IsReconcilable(src, cur))
            // A standing we don't own: only take it over to RAISE a strictly-lower row, never lower an equal/higher one.
            return desLevel > curLevel ? desired : null;

        // An owned Subscriber/Vip row: authoritative full read applies either way; partial read raises only.
        return fullyAuthoritative || desLevel >= curLevel ? desired : null;
    }
}
