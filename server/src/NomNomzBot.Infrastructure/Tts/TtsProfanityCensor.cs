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
using NomNomzBot.Application.Contracts.Tts;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// A light, built-in profanity mask for spoken TTS text (tts.md §3.5). Matches a small curated set of common mild
/// swears on WORD boundaries (case-insensitive) and replaces each hit with its first character followed by
/// asterisks — so "shit happens" becomes "s*** happens". Whole-word matching is deliberate: this is the light
/// filter (AutoMod is the real one), so it never touches substrings ("class", "assess" are untouched). Stateless,
/// pure, thread-safe — registered as a singleton.
/// </summary>
public sealed class TtsProfanityCensor : ITtsProfanityCensor
{
    // Curated LIGHT list — mild profanity a cautious streamer wants kept out of spoken text. Intentionally not
    // exhaustive: AutoMod upstream handles the severe surface; ambiguous everyday words (e.g. "git") are excluded
    // to avoid false positives.
    private static readonly string[] Words =
    [
        "arse",
        "arsehole",
        "ass",
        "asshole",
        "bastard",
        "bitch",
        "bollocks",
        "bugger",
        "bullshit",
        "cock",
        "crap",
        "damn",
        "dick",
        "dickhead",
        "douche",
        "jackass",
        "piss",
        "prick",
        "shit",
        "shite",
        "slut",
        "twat",
        "wanker",
        "whore",
    ];

    private static readonly Regex Matcher = new(
        $@"\b(?:{string.Join('|', Words.Select(Regex.Escape))})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public TtsCensorResult Censor(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new TtsCensorResult(text ?? string.Empty, false);

        bool masked = false;
        string result = Matcher.Replace(
            text,
            match =>
            {
                masked = true;
                return Mask(match.Value);
            }
        );

        return new TtsCensorResult(result, masked);
    }

    /// <summary>Keeps the first character (preserving its case) and replaces the rest with asterisks.</summary>
    private static string Mask(string word) =>
        string.Concat(word[..1], new string('*', word.Length - 1));
}
