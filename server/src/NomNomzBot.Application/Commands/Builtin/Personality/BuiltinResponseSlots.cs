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
/// The response "slots" a built-in can render — a slot is the response CASE (e.g. "live" vs "offline" for
/// uptime), each with its own tone variation-set in <see cref="ToneTemplateCatalog"/>. Grouped by built-in
/// key so the catalog authoring and the built-in code reference one shared set of tokens (no drift). Only
/// slots that carry PERSONALITY appear here — pure usage/error strings stay neutral in the built-in itself.
/// </summary>
public static class BuiltinResponseSlots
{
    /// <summary><c>!uptime</c> — how long the stream has been live.</summary>
    public static class Uptime
    {
        public const string Key = "uptime";

        /// <summary>Stream is live; <c>{uptime}</c> carries the real elapsed time.</summary>
        public const string Live = "live";

        /// <summary>Stream is offline.</summary>
        public const string Offline = "offline";
    }

    /// <summary><c>!song</c> — the currently playing track.</summary>
    public static class Song
    {
        public const string Key = "song";

        /// <summary>A track is playing; <c>{song.name}</c>/<c>{song.artist}</c>/<c>{song.status}</c> are set.</summary>
        public const string Playing = "playing";

        /// <summary>Nothing is playing.</summary>
        public const string Nothing = "nothing";
    }

    /// <summary><c>!queue</c> — the upcoming song queue.</summary>
    public static class Queue
    {
        public const string Key = "queue";

        /// <summary>Queue has tracks; <c>{queue.list}</c>/<c>{queue.count}</c>/<c>{queue.next}</c>/<c>{queue.more}</c> are set.</summary>
        public const string List = "list";

        /// <summary>Queue is empty.</summary>
        public const string Empty = "empty";
    }

    /// <summary><c>!sr</c> — request a song.</summary>
    public static class SongRequest
    {
        public const string Key = "sr";

        /// <summary>Track added; <c>{track.name}</c>/<c>{track.artist}</c>/<c>{user}</c> are set.</summary>
        public const string Added = "added";

        /// <summary>No track matched the query; <c>{query}</c>/<c>{user}</c> are set.</summary>
        public const string NotFound = "notfound";
    }

    /// <summary><c>!skip</c> — skip the current track (mods+).</summary>
    public static class Skip
    {
        public const string Key = "skip";

        /// <summary>A track was skipped.</summary>
        public const string Skipped = "skipped";
    }

    /// <summary><c>!stats</c> / <c>!profile</c> — a viewer's headline stats line.</summary>
    public static class Stats
    {
        public const string Key = "stats";

        /// <summary>The composed profile line; <c>{stats.*}</c> variables are set.</summary>
        public const string Profile = "profile";
    }
}
