// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// The events a widget's code listens to, read from its compiled bundle: every <c>NomNomz.on("name", …)</c> call.
/// A save adds them to the widget's subscriptions, so a widget made in the dashboard receives the events it
/// handles. It never removes one: a system widget can subscribe to an event the runtime handles for it (audio).
/// </summary>
public static partial class WidgetEventSubscriptions
{
    public static List<string> Including(IReadOnlyCollection<string> current, string compiledBundle)
    {
        List<string> merged = [.. current];
        foreach (Match call in OnCall().Matches(compiledBundle))
        {
            string eventType = call.Groups["event"].Value;
            if (!merged.Contains(eventType))
                merged.Add(eventType);
        }

        return merged;
    }

    [GeneratedRegex(
        """NomNomz\s*\.\s*on\s*\(\s*(?<quote>["'`])(?<event>[A-Za-z0-9_.:-]+)\k<quote>"""
    )]
    private static partial Regex OnCall();
}
