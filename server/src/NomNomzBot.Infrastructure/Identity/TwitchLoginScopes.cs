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

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The one table of Twitch scopes a sign-in asks for, per role. Scopes beyond a role are requested later, in
/// place, by the additive re-grant (never by a logout).
/// </summary>
public static class TwitchLoginScopes
{
    // Chatting is the viewer's minimum: the dashboard chat composer reads chat, sends as the viewer
    // (OperatorChatSender) and lists the viewer's own emotes. No `openid`: no sign-in reads an OIDC token, and
    // Twitch's consent screen lists the user's email for it.
    private static readonly string[] Viewer =
    [
        TwitchScopes.UserReadChat,
        TwitchScopes.UserWriteChat,
        TwitchScopes.UserReadEmotes,
    ];

    // Every scope a dashboard moderation call signs with the operator's OWN token (TwitchHelixAuth.Operator):
    // ban, timeout, unban, unban requests, blocked terms, message delete, shield mode, warnings, suspicious
    // users, the operator's emotes, the operator's chat send, and the moderated-channels switcher list.
    private static readonly string[] Moderator =
    [
        TwitchScopes.UserReadChat,
        TwitchScopes.UserReadModeratedChannels,
        TwitchScopes.UserWriteChat,
        TwitchScopes.UserReadEmotes,
        TwitchScopes.ModeratorManageBannedUsers,
        TwitchScopes.ModeratorReadUnbanRequests,
        TwitchScopes.ModeratorManageUnbanRequests,
        TwitchScopes.ModeratorReadBlockedTerms,
        TwitchScopes.ModeratorManageBlockedTerms,
        TwitchScopes.ModeratorManageChatMessages,
        TwitchScopes.ModeratorReadShieldMode,
        TwitchScopes.ModeratorManageShieldMode,
        TwitchScopes.ModeratorManageWarnings,
        TwitchScopes.ModeratorManageSuspiciousUsers,
    ];

    // The base grant of a channel owner. Everything else is requested on demand (progressive scopes): the
    // full catalogue made Twitch's authorize endpoint answer 502 (oversized request).
    private static readonly string[] Broadcaster =
    [
        TwitchScopes.UserReadChat, // EventSub channel.chat.message: the bot cannot read chat without it
        TwitchScopes.UserWriteChat, // Helix chat send: the bot cannot type in chat without it
        TwitchScopes.UserReadModeratedChannels, // the channel switcher's "channels you moderate" list
    ];

    public static string[] For(TwitchLoginRole role) =>
        role switch
        {
            TwitchLoginRole.Viewer => Viewer,
            TwitchLoginRole.Moderator => Moderator,
            _ => Broadcaster,
        };
}
