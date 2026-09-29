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

/// <summary>Reply slots of the music built-ins (<c>!song</c>, <c>!queue</c>, <c>!sr</c>, <c>!skip</c>, …).</summary>
public static partial class BuiltinResponseSlots
{
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

        /// <summary>The track is ALREADY pending in the queue; <c>{user}</c>/<c>{requested.by}</c> are set.</summary>
        public const string Duplicate = "duplicate";

        /// <summary>The track is playing RIGHT NOW; <c>{track.name}</c>/<c>{track.artist}</c>/<c>{user}</c> are set.</summary>
        public const string AlreadyPlaying = "alreadyplaying";
    }

    /// <summary><c>!skip</c> — skip the current track (mods+).</summary>
    public static class Skip
    {
        public const string Key = "skip";

        /// <summary>A track was skipped.</summary>
        public const string Skipped = "skipped";
    }

    /// <summary><c>!bansong</c> — usage/error tone slots (S069h).</summary>
    public static class BanSong
    {
        public const string Key = "bansong";

        /// <summary>Nothing is currently playing, so there is no track to ban — no variables.</summary>
        public const string Nothing = "nothing";

        /// <summary>The block write itself failed with no service-supplied reason — no variables.</summary>
        public const string CouldNotBan = "couldnotban";
    }

    /// <summary><c>!volume</c> — usage/error tone slots (S069h).</summary>
    public static class Volume
    {
        public const string Key = "volume";

        /// <summary>An unparsable argument was given — no variables.</summary>
        public const string Usage = "usage";

        /// <summary>The current volume genuinely cannot be read (nothing playing) — no variables.</summary>
        public const string CannotRead = "cannotread";
    }

    /// <summary><c>!sr</c> — additional usage/error tone slot (S069i), beyond the personality slots above.</summary>
    public static class SongRequestErrors
    {
        /// <summary>Song requests are disabled and the caller is a plain viewer — no variables.</summary>
        public const string Disabled = "disabled";
    }

    /// <summary><c>!playlist</c> — the channel's playlist summary.</summary>
    public static class Playlist
    {
        public const string Key = "playlist";

        /// <summary>The playlist has no tracks.</summary>
        public const string Empty = "empty";

        /// <summary>The playlist summary line.</summary>
        public const string Summary = "summary";
    }
}
