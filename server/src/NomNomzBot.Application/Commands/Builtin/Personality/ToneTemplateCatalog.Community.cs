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

/// <summary>Reply content of the community and data-rights built-ins (<c>BuiltinResponseSlots.Community.cs</c>).</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddCommunitySlots(Dictionary<(string, string), SlotEntry> catalog) { }

    private static void AddCommunitySamples(Dictionary<string, string> samples)
    {
        samples["stats.user"] = "StreamFan42";
        samples["stats.messages"] = "1284";
        samples["stats.watchtime"] = "36h 12m";
        samples["stats.points"] = "5400";
        samples["stats.rank"] = "7";
        samples["stats.streak"] = "12";
        samples["stats.firstseen"] = "2025-03-14";
    }
}
