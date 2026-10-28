// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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

        return new(acquired, remaining, retryAfter);
    }
}
