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

namespace NomNomzBot.Application.Commands.Builtin.Personality;

public static partial class BuiltinResponseSlots
{
    /// <summary><c>!whitelist</c> — give a viewer a manual community-standing override.</summary>
    public static class Whitelist
    {
        public const string Key = "whitelist";

        /// <summary>The command was used without a user and a level.</summary>
        public const string Usage = "usage";

        /// <summary>The caller is not the broadcaster.</summary>
        public const string NotAllowed = "notallowed";

        /// <summary>The named level is not subscriber, vip or moderator.</summary>
        public const string InvalidLevel = "invalidlevel";

        /// <summary>The mentioned user was not found on Twitch.</summary>
        public const string TargetNotFound = "targetnotfound";

        /// <summary>The override was saved.</summary>
        public const string Granted = "granted";

        /// <summary>The override could not be saved.</summary>
        public const string Failed = "failed";
    }

    /// <summary><c>!unwhitelist</c> — remove a viewer's manual community-standing override.</summary>
    public static class Unwhitelist
    {
        public const string Key = "unwhitelist";

        /// <summary>The command was used without a user.</summary>
        public const string Usage = "usage";

        /// <summary>The caller is not the broadcaster.</summary>
        public const string NotAllowed = "notallowed";

        /// <summary>The mentioned user was not found on Twitch.</summary>
        public const string TargetNotFound = "targetnotfound";

        /// <summary>The override was removed.</summary>
        public const string Removed = "removed";

        /// <summary>The user had no override to remove.</summary>
        public const string NoOverride = "nooverride";

        /// <summary>The override could not be removed.</summary>
        public const string Failed = "failed";
    }
}
