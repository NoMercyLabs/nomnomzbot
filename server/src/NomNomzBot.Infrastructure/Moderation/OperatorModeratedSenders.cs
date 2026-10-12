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
using Microsoft.Extensions.Caching.Memory;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Tells whether the sender is a streamer the channel owner moderates for. Moderating someone's channel
/// is a trust the owner has already extended, so the sender needs no chat history to earn standing here.
/// </summary>
public sealed class OperatorModeratedSenders
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    private readonly IApplicationDbContext _db;
    private readonly IOperatorModeratedChannelResolver _resolver;
    private readonly IMemoryCache _cache;

    public OperatorModeratedSenders(
        IApplicationDbContext db,
        IOperatorModeratedChannelResolver resolver,
        IMemoryCache cache
    )
    {
        _db = db;
        _resolver = resolver;
        _cache = cache;
    }

    public async Task<bool> IsModeratedByOwnerAsync(
        Guid broadcasterId,
        string platformUserId,
        CancellationToken ct
    )
    {
        Guid ownerUserId = await _db
            .Channels.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == broadcasterId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId == Guid.Empty)
            return false;

        HashSet<string>? moderated = await ModeratedTwitchIdsAsync(ownerUserId, ct);
        return moderated is not null && moderated.Contains(platformUserId);
    }

    // A failed lookup is not cached and returns null, so the caller falls back to the history ladder and the
    // next message retries.
    private async Task<HashSet<string>?> ModeratedTwitchIdsAsync(
        Guid ownerUserId,
        CancellationToken ct
    )
    {
        string key = $"spam-moderated-channels:{ownerUserId}";
        if (_cache.TryGetValue(key, out HashSet<string>? cached))
            return cached;

        Result<IReadOnlyList<TwitchModeratedChannel>> resolved = await _resolver.ResolveAsync(
            ownerUserId,
            ct
        );
        if (resolved.IsFailure)
            return null;

        HashSet<string> ids = [.. resolved.Value.Select(c => c.BroadcasterId)];
        _cache.Set(key, ids, CacheFor);
        return ids;
    }
}
