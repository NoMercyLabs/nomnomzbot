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

using System.Threading.RateLimiting;

namespace NomNomzBot.Api.RateLimiting;

/// <summary>
/// The public Read Aloud voice. A clip the cache does not hold costs one real synthesis, so it gets a budget of
/// its own, tighter than the anonymous tier, partitioned per IP. A reader at the fastest speed needs about 25
/// new sentences a minute; 40 leaves room for a person who skips around, and stops a script. A request that
/// Cloudflare answers from its cache never reaches this limiter, so a repeated sentence costs no budget.
/// </summary>
public static class ReadAloudRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.ReadAloud;
    public const int PermitLimit = 40;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"{PolicyName}:{RateLimitPartitionKeys.Ip(context)}",
            _ =>
                new()
                {
                    PermitLimit = PermitLimit,
                    Window = Window,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                }
        );
}
