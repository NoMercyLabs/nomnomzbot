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
    /// <summary><c>!allow massban</c> — a live channel lets a held mass ban run now.</summary>
    public static class Allow
    {
        public const string Key = "allow";

        /// <summary>The command was used without <c>massban</c>.</summary>
        public const string Usage = "usage";

        /// <summary>The channel has no held mass ban.</summary>
        public const string NothingPending = "nothingpending";

        /// <summary>The held mass ban was approved and the bans start.</summary>
        public const string Approved = "approved";
    }

    /// <summary><c>!disallow massban</c> — a channel stops a moderator's mass ban.</summary>
    public static class Disallow
    {
        public const string Key = "disallow";

        /// <summary>The command was used without <c>massban</c>.</summary>
        public const string Usage = "usage";

        /// <summary>The channel has no open mass ban.</summary>
        public const string NothingPending = "nothingpending";

        /// <summary>The mass ban was stopped; accounts not banned yet stay unbanned.</summary>
        public const string Declined = "declined";
    }

    /// <summary>The bot's own mass-ban lines, posted without a command.</summary>
    public static class MassBan
    {
        public const string Key = "massban";

        /// <summary>A live channel is told a mass ban waits for it.</summary>
        public const string Request = "request";

        /// <summary>An approved mass ban has finished.</summary>
        public const string Completed = "completed";
    }
}
