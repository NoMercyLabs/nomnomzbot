// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Reflection;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// Reads the multi-file first-party widget bundles embedded from <c>Content/Widgets/Bundles/&lt;key&gt;/</c>
/// (for example <c>chat_overlay</c>: index.vue, components/*.vue, settings.json). A gallery row carries one
/// source file, so a bundle is not a <see cref="FirstPartyWidgetCatalogue"/> entry; it is a repo-owned project
/// that a build or a test reads file by file.
/// </summary>
public static class FirstPartyWidgetBundles
{
    private const string Prefix = "NomNomzBot.Infrastructure.Content.Widgets.Bundles/";

    /// <summary>The bundle's files keyed by path relative to the bundle folder, with '/' separators.</summary>
    public static Dictionary<string, string> Read(string key)
    {
        Assembly assembly = typeof(FirstPartyWidgetBundles).Assembly;
        string bundlePrefix = $"{Prefix}{key}/";
        Dictionary<string, string> files = new(StringComparer.Ordinal);

        foreach (
            string resourceName in assembly
                .GetManifestResourceNames()
                .Where(name =>
                    name.Replace('\\', '/').StartsWith(bundlePrefix, StringComparison.Ordinal)
                )
        )
        {
            using System.IO.Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using System.IO.StreamReader reader = new(stream);
            files[resourceName.Replace('\\', '/')[bundlePrefix.Length..]] = reader.ReadToEnd();
        }

        return files;
    }
}
