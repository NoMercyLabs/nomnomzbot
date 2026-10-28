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
