// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
