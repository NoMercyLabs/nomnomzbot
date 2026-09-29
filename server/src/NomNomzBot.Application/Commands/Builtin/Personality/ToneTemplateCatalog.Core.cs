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

/// <summary>Reply content of the core chat built-ins and the bot's own lines (<c>BuiltinResponseSlots.Core.cs</c>).</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddCoreSlots(Dictionary<(string, string), SlotEntry> catalog) { }

    private static void AddCoreSamples(Dictionary<string, string> samples)
    {
        samples["user"] = "StreamFan42";
        samples["uptime"] = "2 hours 14 minutes";
        samples["commands"] = "!uptime, !song, !sr, !lurk";
        samples["command"] = "!socials";
        samples["description"] = "Links to all my socials.";
        samples["age"] = "3 years 2 months";
    }
}
