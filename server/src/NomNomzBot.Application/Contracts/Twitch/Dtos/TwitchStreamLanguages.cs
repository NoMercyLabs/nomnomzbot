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

using System.Collections.Frozen;
using System.Globalization;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The broadcaster languages Twitch offers (Modify Channel Information <c>broadcaster_language</c>), the same list
/// as the dashboard's Stream page. Twitch takes ISO 639-1 codes plus three of its own: "other", American Sign
/// Language ("asl") and Hong Kong Chinese ("zh-hk").
/// </summary>
public static class TwitchStreamLanguages
{
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
    [
        "asl",
        "zh-hk",
        "ar",
        "bg",
        "ca",
        "cs",
        "da",
        "de",
        "el",
        "en",
        "es",
        "fi",
        "fr",
        "hi",
        "hu",
        "id",
        "it",
        "ja",
        "ko",
        "ms",
        "nl",
        "no",
        "pl",
        "pt",
        "ro",
        "ru",
        "sk",
        "sv",
        "th",
        "tl",
        "tr",
        "uk",
        "vi",
        "zh",
        Other,
    ];

    // Twitch takes any ISO 639-1 code, not only the listed ones. .NET names Norwegian "nb"/"nn" and Filipino
    // "fil", so it has no culture for "no" and "tl"; the listed codes cover those.
    private static readonly FrozenSet<string> Known = CultureInfo
        .GetCultures(CultureTypes.NeutralCultures)
        .Select(culture => culture.TwoLetterISOLanguageName)
        .Where(code => code.Length == 2)
        .Concat(All)
        .ToFrozenSet(StringComparer.Ordinal);

    // .NET knows Twitch's own codes under other names: "ase" for American Sign Language, "zh-HK", and "fil" for "tl".
    private static readonly FrozenDictionary<string, string> CultureNames = new Dictionary<
        string,
        string
    >
    {
        ["asl"] = "ase",
        ["zh-hk"] = "zh-HK",
        ["tl"] = "fil",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>True when Twitch takes <paramref name="code"/> (already trimmed and lower-cased).</summary>
    public static bool IsKnown(string code) => Known.Contains(code);

    /// <summary>The English name of a language code, from the .NET culture data; "Other" for "other".</summary>
    public static string EnglishName(string code) =>
        code == Other ? "Other" : Culture(code)?.EnglishName ?? code;

    /// <summary>The language's own name for itself, or null when .NET has no culture for it.</summary>
    public static string? NativeName(string code) =>
        code == Other ? null : Culture(code)?.NativeName;

    private static CultureInfo? Culture(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(CultureNames.GetValueOrDefault(code, code));
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
