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
using NomNomzBot.Application.Abstractions.Transport;

namespace NomNomzBot.Infrastructure.Platform.Transport;

/// <summary>
/// EF-backed <see cref="ITwitchIdentityResolver"/>. Reads the <c>Channels.TwitchChannelId</c> /
/// <c>Users.TwitchUserId</c> indexed attribute columns to translate between internal tenant/user
/// <see cref="Guid"/> keys and the external Twitch string ids. This is the single seam that enforces
/// the invariant "Twitch never receives a Guid": every transport call resolves through here first.
/// Queries ignore the tenant query filter (<see cref="QueryTrackingBehavior.NoTracking"/> +
/// <c>IgnoreQueryFilters</c>) because resolution runs in ambient-tenant-free contexts (EventSub / IRC
/// hosted services) and must find the channel regardless of which tenant is currently set.
/// </summary>
public sealed class TwitchIdentityResolver : ITwitchIdentityResolver
{
    private readonly IApplicationDbContext _db;

    public TwitchIdentityResolver(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetTwitchChannelIdAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        if (broadcasterId == Guid.Empty)
            return null;

        try
        {
            return await _db
                .Channels.IgnoreQueryFilters()
                .Where(c => c.Id == broadcasterId)
                .Select(c => c.TwitchChannelId)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<Guid?> GetBroadcasterIdAsync(
        string twitchChannelId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(twitchChannelId))
            return null;

        try
        {
            Guid id = await _db
                .Channels.IgnoreQueryFilters()
                .Where(c => c.TwitchChannelId == twitchChannelId)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(ct);

            return id == Guid.Empty ? null : id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<Guid?> GetBroadcasterIdByNameAsync(
        string channelName,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(channelName))
            return null;

        string normalized = channelName.TrimStart('#').ToLowerInvariant();

        try
        {
            Guid id = await _db
                .Channels.IgnoreQueryFilters()
                .Where(c => c.Name.ToLower() == normalized)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(ct);

            return id == Guid.Empty ? null : id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<string?> GetTwitchUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return null;

        try
        {
            return await _db
                .Users.IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .Select(u => u.TwitchUserId)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
