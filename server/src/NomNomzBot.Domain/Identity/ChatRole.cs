// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Domain.Identity;

/// <summary>
/// Resolves a chatter's authorization level from a chat message — the single mapping that the chat command gate,
/// the pipeline <c>user_role</c> condition, and the <c>{{user.role}}</c> variable all share, onto the canonical
/// <see cref="PermissionLevel"/> ladder (roles-permissions §0). A <b>Lead Moderator</b> (Twitch's <c>lead_moderator</c>
/// badge, which replaces the regular moderator badge) resolves to <see cref="PermissionLevel.SuperMod"/> — checked
/// before plain moderator, since a lead mod outranks one. <b>Editor</b> is deliberately absent: it is not a chat badge
/// (it comes from the Helix editors list), so it is a management/HTTP-plane role, never resolved from a chat message.
/// </summary>
public static class ChatRole
{
    /// <summary>The Twitch chat badge marking a lead moderator (super-mod). It replaces the regular moderator badge.</summary>
    public const string LeadModeratorBadge = "lead_moderator";

    /// <summary>
    /// True when the badge list carries the lead-moderator badge. Twitch may surface the marker as either the badge
    /// <c>set_id</c> or its version <c>id</c>, so both are checked — a lead mod is never missed regardless of placement.
    /// </summary>
    public static bool IsLeadModerator(IReadOnlyList<ChatBadge> badges)
    {
        for (int i = 0; i < badges.Count; i++)
            if (badges[i].SetId == LeadModeratorBadge || badges[i].Id == LeadModeratorBadge)
                return true;

        return false;
    }

    /// <summary>The highest chat-visible level the message proves, from the role flags + badges.</summary>
    public static PermissionLevel Resolve(
        bool isBroadcaster,
        bool isModerator,
        bool isVip,
        bool isSubscriber,
        IReadOnlyList<ChatBadge> badges
    )
    {
        if (isBroadcaster)
            return PermissionLevel.Broadcaster;
        if (IsLeadModerator(badges))
            return PermissionLevel.SuperMod;
        if (isModerator)
            return PermissionLevel.Moderator;
        if (isVip)
            return PermissionLevel.Vip;
        if (isSubscriber)
            return PermissionLevel.Subscriber;

        return PermissionLevel.Everyone;
    }

    /// <summary>Parses a permission/role token (command gate, condition param, or resolved user role) to its ladder rung.</summary>
    public static PermissionLevel Parse(string? token) =>
        token?.Trim().ToLowerInvariant() switch
        {
            "broadcaster" => PermissionLevel.Broadcaster,
            "editor" => PermissionLevel.Editor,
            "supermod" or "super_mod" or "lead_moderator" or "leadmoderator" =>
                PermissionLevel.SuperMod,
            "moderator" or "mod" => PermissionLevel.Moderator,
            "artist" => PermissionLevel.Artist,
            "vip" => PermissionLevel.Vip,
            "subscriber" or "sub" => PermissionLevel.Subscriber,
            _ => PermissionLevel.Everyone, // viewer / everyone / empty / unknown
        };

    /// <summary>The lowercase token for a resolved chat level — the value exposed as <c>{{user.role}}</c>.</summary>
    public static string ToToken(PermissionLevel level) =>
        level switch
        {
            PermissionLevel.Broadcaster => "broadcaster",
            PermissionLevel.Editor => "editor",
            PermissionLevel.SuperMod => "supermod",
            PermissionLevel.Moderator => "moderator",
            PermissionLevel.Artist => "artist",
            PermissionLevel.Vip => "vip",
            PermissionLevel.Subscriber => "subscriber",
            _ => "viewer",
        };
}
