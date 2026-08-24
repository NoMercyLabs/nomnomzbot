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
/// Cheap authenticated writes — toggles, config, small CRUD (S114). Generous, partitioned per user, in
/// its own bucket separate from reads and from expensive writes: the owner's reported bug ("rate-limited
/// toggling cheap options") was this exact traffic sharing one generic budget with everything else.
/// </summary>
public static class WriteCheapRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.WriteCheap;
    public const int PermitLimit = 120;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"{PolicyName}:{RateLimitPartitionKeys.PrincipalOrIp(context)}",
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
