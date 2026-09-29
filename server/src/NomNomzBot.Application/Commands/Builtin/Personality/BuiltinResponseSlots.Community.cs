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

/// <summary>
/// Reply slots of the community built-ins (<c>!stats</c>, <c>!quote</c>, <c>!permit</c>, <c>!voice</c>,
/// <c>!media</c>) and the data-rights built-ins.
/// </summary>
public static partial class BuiltinResponseSlots
{
    /// <summary>
    /// <c>!forgetme</c> — self-service GDPR erasure (gdpr-crypto.md §9). Only the friendly completion copy is
    /// customizable; the informed-re-entry clause is appended by the built-in itself and is NOT part of any
    /// template.
    /// </summary>
    public static class Forgetme
    {
        public const string Key = "forgetme";

        /// <summary>Erasure completed — the streamer-stylable "clean slate" sentence (part 1 of the reply).</summary>
        public const string Done = "done";
    }

    /// <summary><c>!stats</c> / <c>!profile</c> — a viewer's headline stats line.</summary>
    public static class Stats
    {
        public const string Key = "stats";

        /// <summary>The composed profile line; <c>{stats.*}</c> variables are set.</summary>
        public const string Profile = "profile";
    }
}
