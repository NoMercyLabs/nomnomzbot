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

using System.Xml.Linq;

namespace NomNomzBot.Infrastructure.Tests.Localization;

/// <summary>
/// Reads the dashboard's committed translation files directly — every <c>values/*.xml</c> (en) and
/// <c>values-nl/*.xml</c> (nl) file under <c>server/i18n/strings</c> — the single
/// home for every user-facing string in the product (S-SCHEMA-I18N-redesign). Walks up from the test binary's
/// output folder to the repo root (mirrors <see cref="Widgets.WidgetAssetPaths"/>'s pattern) so the guard tests
/// read the SAME files the dashboard build packages, not a copy. A backend <see cref="LocalizedText"/> key like
/// <c>widget.alerts.events.label</c> maps to the Compose Resources string name
/// <c>widget_alerts_events_label</c> (dots → underscores — Compose string names must be valid identifiers).
/// </summary>
internal sealed class DashboardStringsXmlCatalog
{
    private const string EnglishRelativePath = "server/i18n/strings/values/strings.xml";
    private const string EnglishRelativeDir = "server/i18n/strings/values";
    private const string DutchRelativeDir = "server/i18n/strings/values-nl";

    private readonly IReadOnlyDictionary<string, string> _english;
    private readonly IReadOnlyDictionary<string, string> _dutch;

    public DashboardStringsXmlCatalog()
    {
        string repoRoot = ResolveRepoRoot();
        _english = LoadStrings(Path.Combine(repoRoot, EnglishRelativeDir));
        _dutch = LoadStrings(Path.Combine(repoRoot, DutchRelativeDir));
    }

    /// <summary>Converts a dot-separated backend translation key to its Compose Resources string name.</summary>
    public static string ResourceNameFor(string translationKey) => translationKey.Replace('.', '_');

    public bool TryGetEnglish(string translationKey, out string value) =>
        _english.TryGetValue(ResourceNameFor(translationKey), out value!);

    public bool TryGetDutch(string translationKey, out string value) =>
        _dutch.TryGetValue(ResourceNameFor(translationKey), out value!);

    /// <summary>
    /// Every string of one language: Compose Resources merges all <c>*.xml</c> files of a values folder (e.g.
    /// <c>strings.xml</c> plus <c>strings_builtin_replies*.xml</c>), so the guard reads them all the same way.
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadStrings(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(
                $"Dashboard translation folder not found: '{directory}'."
            );

        Dictionary<string, string> strings = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory, "*.xml"))
        foreach (XElement element in XDocument.Load(path).Root!.Elements("string"))
            strings[element.Attribute("name")!.Value] = element.Value;
        return strings;
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
