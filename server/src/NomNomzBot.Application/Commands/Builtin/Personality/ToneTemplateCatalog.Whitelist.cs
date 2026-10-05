// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>Reply content of <c>!whitelist</c> and <c>!unwhitelist</c> (<c>BuiltinResponseSlots.Whitelist.cs</c>).</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddWhitelistSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Whitelist.Key;

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Whitelist.Usage,
            variables: [],
            informative: ["Usage: !whitelist @user subscriber/vip/moderator"],
            friendly: ["To whitelist someone, type !whitelist @user subscriber/vip/moderator."],
            sassy: ["Whitelist who, as what? !whitelist @user subscriber/vip/moderator"],
            hype: ["!WHITELIST @USER SUBSCRIBER/VIP/MODERATOR — LET'S GO!"],
            chill: ["!whitelist @user subscriber/vip/moderator"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Whitelist.NotAllowed,
            variables: [],
            informative: ["Only the broadcaster can use !whitelist."],
            friendly: ["Sorry, only the broadcaster can use !whitelist."],
            sassy: ["!whitelist is for the broadcaster. Not you."],
            hype: ["ONLY THE BROADCASTER CAN WHITELIST!"],
            chill: ["only the broadcaster can whitelist."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Whitelist.InvalidLevel,
            variables: ["whitelist.level"],
            informative:
            [
                "Invalid level \"{whitelist.level}\". Valid levels: subscriber, vip, moderator",
            ],
            friendly:
            [
                "I don't know the level \"{whitelist.level}\". Pick subscriber, vip or moderator!",
            ],
            sassy: ["\"{whitelist.level}\" is not a level. Try subscriber, vip or moderator."],
            hype: ["\"{whitelist.level}\" ISN'T A LEVEL! PICK SUBSCRIBER, VIP OR MODERATOR!"],
            chill: ["\"{whitelist.level}\" isn't a level. subscriber, vip or moderator."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Whitelist.TargetNotFound,
            variables: ["user"],
            informative: ["User \"{user}\" not found on Twitch."],
            friendly: ["I couldn't find \"{user}\" on Twitch. Check the name!"],
            sassy: ["\"{user}\" doesn't exist on Twitch. Nice try."],
            hype: ["\"{user}\" NOT FOUND ON TWITCH!"],
            chill: ["couldn't find \"{user}\" on Twitch."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Whitelist.Granted,
            variables: ["user", "whitelist.level"],
            informative: ["@{user} has been granted {whitelist.level} level access."],
            friendly: ["@{user} now has {whitelist.level} level access!"],
            sassy: ["@{user} got {whitelist.level} access. Handle it well."],
            hype: ["@{user} IS NOW {whitelist.level}! LET'S GO!"],
            chill: ["gave @{user} {whitelist.level} level access."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Whitelist.Failed,
            variables: [],
            informative: ["The whitelist could not be saved."],
            friendly: ["Sorry, I couldn't save that whitelist. Please try again."],
            sassy: ["The whitelist didn't stick. Try again."],
            hype: ["THE WHITELIST FAILED! TRY AGAIN!"],
            chill: ["couldn't save that whitelist."]
        );
    }

    private static void AddUnwhitelistSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Unwhitelist.Key;

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unwhitelist.Usage,
            variables: [],
            informative: ["Usage: !unwhitelist @user"],
            friendly: ["To remove a whitelist, type !unwhitelist @user."],
            sassy: ["Unwhitelist who? !unwhitelist @user"],
            hype: ["!UNWHITELIST @USER — TAKE IT BACK!"],
            chill: ["!unwhitelist @user"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unwhitelist.NotAllowed,
            variables: [],
            informative: ["Only the broadcaster can use !unwhitelist."],
            friendly: ["Sorry, only the broadcaster can use !unwhitelist."],
            sassy: ["!unwhitelist is for the broadcaster. Not you."],
            hype: ["ONLY THE BROADCASTER CAN UNWHITELIST!"],
            chill: ["only the broadcaster can unwhitelist."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unwhitelist.TargetNotFound,
            variables: ["user"],
            informative: ["User \"{user}\" not found on Twitch."],
            friendly: ["I couldn't find \"{user}\" on Twitch. Check the name!"],
            sassy: ["\"{user}\" doesn't exist on Twitch. Nice try."],
            hype: ["\"{user}\" NOT FOUND ON TWITCH!"],
            chill: ["couldn't find \"{user}\" on Twitch."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unwhitelist.Removed,
            variables: ["user"],
            informative: ["@{user}'s permission override has been removed."],
            friendly: ["@{user}'s permission override is removed."],
            sassy: ["@{user} lost the override. Back to normal."],
            hype: ["@{user}'S OVERRIDE IS GONE!"],
            chill: ["removed @{user}'s permission override."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unwhitelist.NoOverride,
            variables: ["user"],
            informative: ["@{user} had no permission override."],
            friendly: ["@{user} didn't have a permission override."],
            sassy: ["@{user} had nothing to remove."],
            hype: ["@{user} HAD NO OVERRIDE!"],
            chill: ["@{user} had no override."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unwhitelist.Failed,
            variables: [],
            informative: ["The whitelist could not be removed."],
            friendly: ["Sorry, I couldn't remove that whitelist. Please try again."],
            sassy: ["The whitelist refuses to leave. Try again."],
            hype: ["THE REMOVE FAILED! TRY AGAIN!"],
            chill: ["couldn't remove that whitelist."]
        );
    }
}
