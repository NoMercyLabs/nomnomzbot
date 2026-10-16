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
