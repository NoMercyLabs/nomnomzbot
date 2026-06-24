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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Community;

/// <summary>
/// Twitch reconciliation for the Community moderator roster (<c>ChannelModerators</c>). It get-or-creates a
/// <c>User</c> for every moderator and VIP returned by Helix, then upserts the moderator rows by
/// (channel, user). The upsert is idempotent: a member already present is left as-is, so re-running on every
/// onboarding + backfill never duplicates a row. Twitch ids (mod/VIP <c>UserId</c>) live in Twitch-user-id
/// space and are joined to <see cref="User.TwitchUserId"/>; the moderator FK is the internal <see cref="User.Id"/>.
/// </summary>
public sealed class CommunityRosterService(
    IApplicationDbContext db,
    ITwitchModeratorsApi moderators,
    TimeProvider clock,
    ILogger<CommunityRosterService> logger
) : ICommunityRosterService
{
    public async Task<Result<int>> SyncModeratorsFromTwitchAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        bool channelExists = await db.Channels.AnyAsync(
            c => c.Id == broadcasterId,
            cancellationToken
        );
        if (!channelExists)
            return Errors.ChannelNotFound<int>(broadcasterId.ToString());

        // Pull the current rosters from Helix. A failure (no token / missing scope) degrades to an empty
        // roster — the channel simply seeds nothing rather than throwing.
        Result<TwitchPage<TwitchModerator>> modsResult = await moderators.GetModeratorsAsync(
            broadcasterId,
            new TwitchPageRequest(),
            cancellationToken
        );
        IReadOnlyList<TwitchModerator> mods = modsResult.IsSuccess ? modsResult.Value.Items : [];

        Result<TwitchPage<TwitchVip>> vipsResult = await moderators.GetVipsAsync(
            broadcasterId,
            new TwitchPageRequest(),
            cancellationToken
        );
        IReadOnlyList<TwitchVip> vips = vipsResult.IsSuccess ? vipsResult.Value.Items : [];

        if (mods.Count == 0 && vips.Count == 0)
            return Result.Success(0);

        // mod/VIP .UserId are Twitch user string ids; the moderator FK target is the internal User.Id Guid.
        List<string> allTwitchUserIds = mods.Select(m => m.UserId)
            .Concat(vips.Select(v => v.UserId))
            .Distinct()
            .ToList();

        List<string> existingTwitchUserIds = await db
            .Users.Where(u => allTwitchUserIds.Contains(u.TwitchUserId))
            .Select(u => u.TwitchUserId)
            .ToListAsync(cancellationToken);

        // Get-or-create a User for every moderator (then every VIP not already covered by a moderator).
        foreach (TwitchModerator mod in mods.Where(m => !existingTwitchUserIds.Contains(m.UserId)))
            db.Users.Add(
                new User
                {
                    TwitchUserId = mod.UserId,
                    Username = mod.UserLogin,
                    UsernameNormalized = mod.UserLogin.ToLowerInvariant(),
                    DisplayName = mod.UserName ?? mod.UserLogin,
                }
            );

        foreach (
            TwitchVip vip in vips.Where(v =>
                !existingTwitchUserIds.Contains(v.UserId) && mods.All(m => m.UserId != v.UserId)
            )
        )
            db.Users.Add(
                new User
                {
                    TwitchUserId = vip.UserId,
                    Username = vip.UserLogin,
                    UsernameNormalized = vip.UserLogin.ToLowerInvariant(),
                    DisplayName = vip.UserName ?? vip.UserLogin,
                }
            );

        await db.SaveChangesAsync(cancellationToken);

        // Resolve Twitch user ids → internal User.Id Guids for the moderator FK.
        Dictionary<string, Guid> userIdByTwitchId = await db
            .Users.Where(u => allTwitchUserIds.Contains(u.TwitchUserId))
            .ToDictionaryAsync(u => u.TwitchUserId, u => u.Id, cancellationToken);

        HashSet<Guid> existingModUserIds = await db
            .ChannelModerators.Where(cm => cm.ChannelId == broadcasterId)
            .Select(cm => cm.UserId)
            .ToHashSetAsync(cancellationToken);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        int created = 0;
        foreach (TwitchModerator mod in mods)
        {
            if (!userIdByTwitchId.TryGetValue(mod.UserId, out Guid modUserId))
                continue;
            if (existingModUserIds.Contains(modUserId))
                continue;

            db.ChannelModerators.Add(
                new ChannelModerator
                {
                    ChannelId = broadcasterId,
                    UserId = modUserId,
                    GrantedAt = now,
                }
            );
            existingModUserIds.Add(modUserId);
            created++;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Community roster sync upserted {Created} new moderator(s) ({ModCount} mods, {VipCount} VIPs from Twitch) for {BroadcasterId}",
            created,
            mods.Count,
            vips.Count,
            broadcasterId
        );
        return Result.Success(created);
    }
}
