// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Chat.Enums;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The cache-key scheme for the chat-decoration third-party emote sets (chat-decoration spec §7). Shared by the reader
/// (<c>ThirdPartyEmoteAdapter</c>) and the refresh worker so both agree on the exact key. The provider token is a stable
/// lowercase string that must never change once data has been cached under it.
/// </summary>
internal static class ChatEmoteCacheKeys
{
    /// <summary><c>chat:emotes:{provider}:global</c> — a provider's global emote set (every channel).</summary>
    public static string Global(EmoteProvider provider) => $"chat:emotes:{Token(provider)}:global";

    /// <summary><c>chat:emotes:{provider}:channel:{broadcasterId}</c> — a provider's per-channel emote set.</summary>
    public static string Channel(EmoteProvider provider, string twitchBroadcasterId) =>
        $"chat:emotes:{Token(provider)}:channel:{twitchBroadcasterId}";

    /// <summary>
    /// <c>chat:emotes:twitch:user:{twitchUserId}</c> — the signed-in user's cross-channel Twitch emotes
    /// (Get User Emotes). Keyed to the resolved Twitch user id, never the viewed channel, so one user's
    /// subscription emotes never leak into another user's composer.
    /// </summary>
    public static string TwitchUser(string twitchUserId) =>
        $"chat:emotes:twitch:user:{twitchUserId}";

    /// <summary>The stable lowercase token a provider is cached under (7TV is "7tv", matching the provider's own naming).</summary>
    public static string Token(EmoteProvider provider) =>
        provider switch
        {
            EmoteProvider.Bttv => "bttv",
            EmoteProvider.Ffz => "ffz",
            EmoteProvider.SevenTv => "7tv",
            _ => provider.ToString().ToLowerInvariant(),
        };
}
