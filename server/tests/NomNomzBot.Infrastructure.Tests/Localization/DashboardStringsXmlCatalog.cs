// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Xml.Linq;

namespace NomNomzBot.Infrastructure.Tests.Localization;

/// <summary>
/// Reads the dashboard's committed translation files directly — <c>strings.xml</c> (en) and
/// <c>values-nl/strings.xml</c> (nl) under <c>app/composeApp/src/commonMain/composeResources</c> — the single
/// home for every user-facing string in the product (S-SCHEMA-I18N-redesign). Walks up from the test binary's
/// output folder to the repo root (mirrors <see cref="Widgets.WidgetAssetPaths"/>'s pattern) so the guard tests
/// read the SAME files the dashboard build packages, not a copy. A backend <see cref="LocalizedText"/> key like
/// <c>widget.alerts.events.label</c> maps to the Compose Resources string name
/// <c>widget_alerts_events_label</c> (dots → underscores — Compose string names must be valid identifiers).
/// </summary>
internal sealed class DashboardStringsXmlCatalog
{
    private const string EnglishRelativePath =
        "app/composeApp/src/commonMain/composeResources/values/strings.xml";
    private const string DutchRelativePath =
        "app/composeApp/src/commonMain/composeResources/values-nl/strings.xml";

    private readonly IReadOnlyDictionary<string, string> _english;
    private readonly IReadOnlyDictionary<string, string> _dutch;

    public DashboardStringsXmlCatalog()
    {
        string repoRoot = ResolveRepoRoot();
        _english = LoadStrings(Path.Combine(repoRoot, EnglishRelativePath));
        _dutch = LoadStrings(Path.Combine(repoRoot, DutchRelativePath));
    }

    /// <summary>Converts a dot-separated backend translation key to its Compose Resources string name.</summary>
    public static string ResourceNameFor(string translationKey) => translationKey.Replace('.', '_');

    public bool TryGetEnglish(string translationKey, out string value) =>
        _english.TryGetValue(ResourceNameFor(translationKey), out value!);

    public bool TryGetDutch(string translationKey, out string value) =>
        _dutch.TryGetValue(ResourceNameFor(translationKey), out value!);

    private static IReadOnlyDictionary<string, string> LoadStrings(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Dashboard translation file not found: '{path}'.",
                path
            );

        XDocument document = XDocument.Load(path);
        return document
            .Root!.Elements("string")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Value,
                StringComparer.Ordinal
            );
    }

    private static string ResolveRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, EnglishRelativePath)))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate '{EnglishRelativePath}' above '{AppContext.BaseDirectory}'."
        );
    }
}
