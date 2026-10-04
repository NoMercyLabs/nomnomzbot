// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// The events a widget's code listens to, read from its compiled bundle: every <c>NomNomz.on("name", …)</c> call.
/// A save adds them to the widget's subscriptions, so a widget made in the dashboard receives the events it
/// handles. It never removes one: a system widget can subscribe to an event the runtime handles for it (audio).
/// A call inside a comment is help text, not code, so it subscribes to nothing.
/// </summary>
public static partial class WidgetEventSubscriptions
{
    public static List<string> Including(IReadOnlyCollection<string> current, string compiledBundle)
    {
        List<string> merged = [.. current];
        foreach (Match call in OnCall().Matches(WithoutComments(compiledBundle)))
        {
            string eventType = call.Groups["event"].Value;
            if (!merged.Contains(eventType))
                merged.Add(eventType);
        }

        return merged;
    }

    /// <summary>Drops <c>//</c>, <c>/* */</c> and <c>&lt;!-- --&gt;</c> comments; quoted text is kept whole.</summary>
    private static string WithoutComments(string code)
    {
        StringBuilder kept = new(code.Length);
        int index = 0;
        while (index < code.Length)
        {
            char current = code[index];
            if (current is '\'' or '"' or '`')
                index = CopyQuoted(code, index, kept);
            else if (code.AsSpan(index).StartsWith("//"))
                index = EndOf(code, "\n", index + 2, keepEnd: true);
            else if (code.AsSpan(index).StartsWith("/*"))
                index = EndOf(code, "*/", index + 2, keepEnd: false);
            else if (code.AsSpan(index).StartsWith("<!--"))
                index = EndOf(code, "-->", index + 4, keepEnd: false);
            else
            {
                kept.Append(current);
                index++;
            }
        }

        return kept.ToString();
    }

    private static int CopyQuoted(string code, int start, StringBuilder kept)
    {
        char quote = code[start];
        int index = start + 1;
        while (index < code.Length && code[index] != quote && (quote == '`' || code[index] != '\n'))
            index += code[index] == '\\' ? 2 : 1;

        int end = Math.Min(index + 1, code.Length);
        kept.Append(code, start, end - start);
        return end;
    }

    private static int EndOf(string code, string marker, int from, bool keepEnd)
    {
        int found = code.IndexOf(marker, from, StringComparison.Ordinal);
        if (found < 0)
            return code.Length;

        return keepEnd ? found : found + marker.Length;
    }

    [GeneratedRegex(
        """NomNomz\s*\.\s*on\s*\(\s*(?<quote>["'`])(?<event>[A-Za-z0-9_.:-]+)\k<quote>"""
    )]
    private static partial Regex OnCall();
}
