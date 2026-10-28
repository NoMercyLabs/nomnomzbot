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

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The cache-key scheme for the chat-decoration Helix badge sets (chat-decoration spec §7). Shared by the reader
/// (<c>ChatBadgeResolver</c>) and the refresh worker. Keyed by the tenant broadcaster id, matching how the Helix
/// channel-badge call is addressed.
/// </summary>
internal static class ChatBadgeCacheKeys
{
    /// <summary>The global badge set (every channel).</summary>
    public const string Global = "chat:badges:global";

    /// <summary>A channel's own badge set (subscriber tiers, bits tiers, etc.).</summary>
    public static string Channel(Guid broadcasterId) => $"chat:badges:channel:{broadcasterId}";

    /// <summary>The badge set of a channel addressed by its raw Twitch id (a channel the bot is not on).</summary>
    public static string TwitchChannel(string twitchBroadcasterId) =>
        $"chat:badges:twitch:{twitchBroadcasterId}";
}
