// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Xml.Linq;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// The one-line English description of each template helper, read from the embedded help text the dashboard
/// shares (<c>template_helper_text_upper</c> for the key <c>template.helper.text.upper</c>). The generated SDK
/// types are English-only developer text, so this is the one place the backend reads English.
/// </summary>
internal static class TemplateHelperDescriptions
{
    private const string ResourcePrefix = "NomNomzBot.Infrastructure.I18n.En.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Strings = new(Load);

    /// <summary>The first line of the row's English help text; empty when the row has none.</summary>
    public static string Of(TemplateHelperEntry entry) =>
        Strings.Value.TryGetValue(entry.Description.Key.Replace('.', '_'), out string? text)
            ? FirstLine(text)
            : string.Empty;

    private static string FirstLine(string text) =>
        text.Replace("\\'", "'", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
        ?? string.Empty;

    private static Dictionary<string, string> Load()
    {
        Assembly assembly = typeof(TemplateHelperDescriptions).Assembly;
        Dictionary<string, string> strings = new(StringComparer.Ordinal);
        foreach (
            string name in assembly
                .GetManifestResourceNames()
                .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        )
        {
            using System.IO.Stream stream = assembly.GetManifestResourceStream(name)!;
            foreach (XElement element in XDocument.Load(stream).Root!.Elements("string"))
                strings[element.Attribute("name")!.Value] = element.Value;
        }
        return strings;
    }
}
