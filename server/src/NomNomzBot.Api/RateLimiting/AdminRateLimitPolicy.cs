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
/// Platform-admin reads/non-destructive writes (S114) — partitioned per principal. Its own bucket, well
/// above the 3/min <see cref="SecuritySensitiveRateLimitPolicy"/> tier carved out for the same
/// controllers' actually destructive actions (impersonate, suspend, principal create/deactivate, flag
/// writes, billing writes/refunds, GDPR erasure).
/// </summary>
public static class AdminRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.Admin;
    public const int PermitLimit = 60;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"{PolicyName}:{RateLimitPartitionKeys.PrincipalOrIp(context)}",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }
        );
}
