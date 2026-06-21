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
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Default <see cref="IChannelAccessService"/> — authorizes tenant resolution against the
/// database: the caller's own channel (Channel.OwnerUserId == User.Id), an active moderator
/// grant, or platform admin. Fails closed for everything else.
/// </summary>
public sealed class ChannelAccessService : IChannelAccessService
{
    private readonly IApplicationDbContext _db;

    public ChannelAccessService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanResolveTenantAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default
    )
    {
        // userId / channelId are the internal user / tenant Guids in string form (JWT sub + tenant key).
        if (
            !Guid.TryParse(userId, out Guid userGuid)
            || !Guid.TryParse(channelId, out Guid channelGuid)
        )
            return false;

        // Own channel — the caller owns the channel they are resolving.
        if (
            await _db.Channels.AnyAsync(
                c => c.Id == channelGuid && c.OwnerUserId == userGuid,
                cancellationToken
            )
        )
            return true;

        // Active moderator grant (soft-deleted grants are excluded by the global query filter).
        if (
            await _db.ChannelModerators.AnyAsync(
                m => m.ChannelId == channelGuid && m.UserId == userGuid,
                cancellationToken
            )
        )
            return true;

        // Active management membership — roles-permissions Gate 1 (§3.1): a Moderator/SuperMod/Editor/
        // Broadcaster membership grants tenant access. TenantResolutionMiddleware sets the tenant to channelGuid
        // before this call, so the global tenant + soft-delete filters already scope ChannelMemberships here.
        if (
            await _db.ChannelMemberships.AnyAsync(
                m => m.BroadcasterId == channelGuid && m.UserId == userGuid,
                cancellationToken
            )
        )
            return true;

        // Platform principal may act on any channel.
        return await _db.Users.AnyAsync(
            u => u.Id == userGuid && u.IsPlatformPrincipal,
            cancellationToken
        );
    }

    public async Task<Guid> ResolveOwnChannelAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            return Guid.Empty;

        return await _db
            .Channels.Where(c => c.OwnerUserId == userGuid)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
