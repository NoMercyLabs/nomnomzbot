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
using Microsoft.AspNetCore.Http;

namespace NomNomzBot.Api.RateLimiting;

/// <summary>
/// Unauthenticated public surfaces (S114) — overlays, webhooks, song-request, OAuth relay. Partitioned
/// per IP since there is no authenticated caller to key on.
/// </summary>
public static class AnonymousRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.Anonymous;
    public const int PermitLimit = 120;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"{PolicyName}:{RateLimitPartitionKeys.Ip(context)}",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }
        );
}
