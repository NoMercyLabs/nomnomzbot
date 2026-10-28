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

using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>The two Twitch shoutout cooldowns, read off the channel's in-memory stamps.</summary>
internal static class ShoutoutCooldowns
{
    public static bool GlobalActive(
        ChannelContext? channel,
        TimeSpan cooldown,
        DateTimeOffset now
    ) => channel?.LastGlobalShoutout is DateTimeOffset last && now - last < cooldown;

    public static bool PerUserActive(
        ChannelContext? channel,
        string targetTwitchUserId,
        TimeSpan cooldown,
        DateTimeOffset now
    ) =>
        channel is not null
        && channel.LastShoutoutPerUser.TryGetValue(targetTwitchUserId, out DateTimeOffset last)
        && now - last < cooldown;
}
