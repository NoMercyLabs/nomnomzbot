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
/// Session renewal (<c>POST auth/refresh</c>) — generous, partitioned per IP. The refresh token is a long
/// random httpOnly cookie, so there is nothing to brute-force; the strict <c>auth</c> login budget (10 per
/// minute per IP) only made every dashboard page load spend from a budget shared by everyone behind one NAT
/// and signed users out with a 429.
/// </summary>
public static class SessionRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.Session;
    public const int PermitLimit = 60;
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
