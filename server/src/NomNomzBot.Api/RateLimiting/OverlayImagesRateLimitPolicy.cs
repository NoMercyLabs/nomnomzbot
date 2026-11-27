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
/// The overlay image optimizer. One OBS machine loads every widget's images from one IP, and a busy chat brings a
/// new avatar or emote with most lines, so images get a budget of their own, wider than the anonymous tier they
/// would otherwise drain from the overlay calls. The browser keeps each answer for a year (immutable), so only an
/// image it has never seen costs a permit. Partitioned per IP.
/// </summary>
public static class OverlayImagesRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.OverlayImages;
    public const int PermitLimit = 600;
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
