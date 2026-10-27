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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Writes the template helper hint both SDK contexts share, from the registry (the static rows plus every
/// provider): <c>TemplateHelperName</c>, the union of every key, and <c>NnzTemplateHelperMap</c>, key to a
/// one-line description. A new registry row reaches both without a hand edit and without a new SDK method.
/// </summary>
internal static class TemplateHelperTypeScript
{
    public static string Write(ITemplateHelperRegistry registry)
    {
        List<TemplateHelperEntry> entries =
        [
            .. registry.All.OrderBy(entry => entry.Key, StringComparer.Ordinal),
        ];

        StringBuilder sb = new();
        sb.AppendLine(
            "/** Every template helper name, as written in a template: {name} or {name:argument}. */"
        );
        sb.AppendLine("type TemplateHelperName =");
        foreach (TemplateHelperEntry entry in entries)
            sb.AppendLine($"  | '{Escape(entry.Key)}'");
        sb.AppendLine("  ;");
        sb.AppendLine();
        sb.AppendLine("/** Each template helper name and what it gives, in one line. */");
        sb.AppendLine("interface NnzTemplateHelperMap {");
        foreach (TemplateHelperEntry entry in entries)
            sb.AppendLine($"  '{Escape(entry.Key)}': '{Escape(DescriptionOf(entry))}';");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // A row with no English help text still gets a line: its example value, so the hint is never empty.
    private static string DescriptionOf(TemplateHelperEntry entry)
    {
        string text = TemplateHelperDescriptions.Of(entry);
        if (text.Length > 0)
            return text;
        return entry.Sample.Length > 0 ? $"For example {entry.Sample}" : entry.Key;
    }

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
