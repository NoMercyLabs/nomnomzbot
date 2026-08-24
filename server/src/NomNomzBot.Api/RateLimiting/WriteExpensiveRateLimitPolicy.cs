// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
