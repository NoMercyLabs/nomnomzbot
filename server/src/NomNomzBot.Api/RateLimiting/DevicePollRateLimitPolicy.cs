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
/// Device Code Flow polling (S114): the flow legitimately polls every ~5s (≈12 req/min) until the
/// operator approves, for up to the code's lifetime — far above the brute-force "auth" budget, so it
/// gets its own generous per-IP allowance. 60 req/min (1/s) still bounds a flood (real polling never
/// exceeds it, even with a concurrent streamer + bot login) while never throttling a legitimate login.
/// The backend's own DeviceCodePollThrottle separately caps how often each code reaches Twitch.
/// </summary>
public static class DevicePollRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.DevicePoll;
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
