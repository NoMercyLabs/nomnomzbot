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

/// <summary>Reply content of the music built-ins (<c>BuiltinResponseSlots.Music.cs</c>).</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddMusicSlots(Dictionary<(string, string), SlotEntry> catalog) { }

    private static void AddMusicSamples(Dictionary<string, string> samples)
    {
        samples["song.name"] = "Never Gonna Give You Up";
        samples["song.artist"] = "Rick Astley";
        samples["song.status"] = "Now playing:";
        samples["track.name"] = "Never Gonna Give You Up";
        samples["track.artist"] = "Rick Astley";
        samples["track.link"] = "https://open.spotify.com/track/4PTG3Z6ehGkBFwjybzWkR8";
        samples["requested.by"] = "StreamFan42";
        samples["query"] = "never gonna give you up";
        samples["queue.list"] = "1. Take On Me — a-ha, 2. Africa — Toto";
        samples["queue.count"] = "2";
        samples["queue.next"] = "Take On Me — a-ha";
        samples["queue.more"] = "";
    }
}
