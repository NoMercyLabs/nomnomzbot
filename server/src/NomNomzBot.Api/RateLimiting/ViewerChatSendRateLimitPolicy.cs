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
/// A viewer sending in their own voice (chat-client.md §9a). Sized to Twitch's own limit for a normal chatter:
/// 20 messages per 30 seconds, counted per user per channel, so a send we accept is one Twitch accepts too.
/// </summary>
public static class ViewerChatSendRateLimitPolicy
{
    public const string PolicyName = RateLimitPolicyNames.ViewerChatSend;
    public const int PermitLimit = 20;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"{PolicyName}:{RateLimitPartitionKeys.PrincipalOrIp(context)}:{RateLimitPartitionKeys.ChannelOrCaller(context)}",
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
