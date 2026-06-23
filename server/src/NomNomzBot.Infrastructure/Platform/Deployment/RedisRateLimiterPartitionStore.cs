// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Interfaces;
using StackExchange.Redis;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// The full/SaaS rate-limiter counter store (platform-conventions §3.7): an atomic fixed-window counter in Redis
/// (<c>INCR</c> + first-hit <c>EXPIRE</c>) so the per-user / per-IP budgets hold cluster-wide across the stateless
/// replica pool. The window TTL is set only on the first increment, so the bucket expires exactly one window after
/// it opens.
/// </summary>
public sealed class RedisRateLimiterPartitionStore : IRateLimiterPartitionStore
{
    private const string KeyPrefix = "nomnomzbot:ratelimit:";

    private readonly IConnectionMultiplexer _redis;

    public RedisRateLimiterPartitionStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<RateLimitLease> AcquireAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default
    )
    {
        IDatabase db = _redis.GetDatabase();
        RedisKey key = KeyPrefix + partitionKey;

        long count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, window);

        bool acquired = count <= permitLimit;
        int remaining = (int)Math.Max(0, permitLimit - count);

        TimeSpan retryAfter = TimeSpan.Zero;
        if (!acquired)
        {
            TimeSpan? ttl = await db.KeyTimeToLiveAsync(key);
            retryAfter = ttl ?? window;
        }

        return new RateLimitLease(acquired, remaining, retryAfter);
    }
}
