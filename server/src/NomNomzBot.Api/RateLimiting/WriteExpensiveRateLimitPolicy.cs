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
/// Expensive authenticated writes — synthesis, uploads, fan-out sends (S114). Partitioned per channel
/// (mirrors <c>TenantResolutionMiddleware</c>'s route → header → query resolution) rather than per
/// caller, so a moderator's heavy action on one channel cannot starve a different channel they also
/// moderate.
/// </summary>
public static class WriteExpensiveRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.WriteExpensive;
    public const int PermitLimit = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"{PolicyName}:{RateLimitPartitionKeys.ChannelOrCaller(context)}",
            _ =>
                new()
                {
                    PermitLimit = PermitLimit,
                    Window = Window,
                    SegmentsPerWindow = 4,
                    QueueLimit = 0,
                }
        );
}
