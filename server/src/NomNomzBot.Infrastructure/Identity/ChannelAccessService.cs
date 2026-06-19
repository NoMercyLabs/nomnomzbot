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
/// database: the caller's own channel (Channel.Id == User.Id), an active moderator grant,
/// or platform admin. Fails closed for everything else.
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
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(channelId))
            return false;

        // Own channel — Channel.Id is the owner's User.Id by design. No query needed.
        if (string.Equals(userId, channelId, StringComparison.Ordinal))
            return true;

        // Active moderator grant (soft-deleted grants are excluded by the global query filter).
        if (
            await _db.ChannelModerators.AnyAsync(
                m => m.ChannelId == channelId && m.UserId == userId,
                cancellationToken
            )
        )
            return true;

        // Platform admin may act on any channel.
        return await _db.Users.AnyAsync(u => u.Id == userId && u.IsAdmin, cancellationToken);
    }
}
