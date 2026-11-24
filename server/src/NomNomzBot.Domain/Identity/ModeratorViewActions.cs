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

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Domain.Identity;

/// <summary>
/// The moderator view: the action keys that work in a channel whose streamer never onboarded. There the bot
/// holds no streamer token and its features do not run, so a moderator can only do what their own Twitch
/// access allows: moderate, watch for spam, read chat and the people in it, read the stream, and clear the
/// inbox. Every other key is held by nobody but the owner until the channel onboards.
/// An entry ending in <c>*</c> covers every key that starts with the text before it.
/// </summary>
public static class ModeratorViewActions
{
    public static readonly IReadOnlyList<string> Members =
    [
        "moderation:*",
        "spam:*",
        "chat:read",
        "chat:send",
        "chat:announce",
        "community:read",
        "dashboard:read",
        "notifications:dismiss",
        "stream:read",
    ];

    // Twitch grants these five powers to every moderator, so a channel that did not onboard has no lead to wait for.
    public static readonly IReadOnlyList<string> TwitchModeratorPowers =
    [
        "moderation:blocklist:write",
        "moderation:automod:twitch:manage",
        "moderation:shieldmode:write",
        "moderation:unbanrequest:resolve",
        "moderation:suspicioususer:write",
    ];

    // The bot's own chat color: a bot setting, not a moderator power.
    private const string BotChatColor = "moderation:chatcolor:write";

    // The channel economy runs on the bot, and the bot does not run in a channel that did not onboard.
    private const string EconomyPrefix = "economy:";

    /// <summary>
    /// True when the action stays held in a channel that did not onboard. Management powers keep only the
    /// moderator view; community actions stay, except the economy, which has no bot to run it there.
    /// </summary>
    public static bool Allows(AuthPlane plane, string actionKey) =>
        plane == AuthPlane.Management
            ? Covers(actionKey)
            : !actionKey.StartsWith(EconomyPrefix, StringComparison.Ordinal);

    /// <summary>True when <paramref name="actionKey"/> works without the streamer's own token.</summary>
    public static bool Covers(string actionKey)
    {
        if (string.Equals(actionKey, BotChatColor, StringComparison.Ordinal))
            return false;

        foreach (string member in Members)
        {
            bool matches = member.EndsWith('*')
                ? actionKey.StartsWith(member[..^1], StringComparison.Ordinal)
                : string.Equals(actionKey, member, StringComparison.Ordinal);
            if (matches)
                return true;
        }
        return false;
    }
}
