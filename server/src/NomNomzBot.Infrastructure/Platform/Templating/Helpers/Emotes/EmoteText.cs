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

using System.Globalization;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

internal static class EmoteText
{
    /// <summary>The most times one emote repeats, and the largest wall (rows and columns).</summary>
    public const int MaxRepeat = 50;
    public const int MaxWallRows = 10;
    public const int MaxWallColumns = 20;

    /// <summary>The codes of the emotes, one space apart, the way a viewer types them.</summary>
    public static string Codes(IEnumerable<EmoteItem> emotes) =>
        string.Join(' ', emotes.Select(e => e.Code));

    /// <summary>A whole number in the culture of the channel language: <c>1,204</c>.</summary>
    public static string Count(int number, CultureInfo culture) => number.ToString("N0", culture);

    /// <summary>The emote with exactly this code, else the one that differs only in case; null when there is none.</summary>
    public static EmoteItem? Find(IReadOnlyList<EmoteItem> emotes, string code) =>
        emotes.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.Ordinal))
        ?? emotes.FirstOrDefault(e =>
            string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>
    /// How often each known emote code appears in the texts. A word counts when it is exactly a code of
    /// <paramref name="known"/>; emote codes are case-sensitive on every network.
    /// </summary>
    public static Dictionary<string, int> Tally(
        IEnumerable<string> texts,
        IReadOnlyList<EmoteItem> known
    )
    {
        HashSet<string> codes = [.. known.Select(e => e.Code)];
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (string word in texts.SelectMany(Words))
            if (codes.Contains(word))
                counts[word] = counts.GetValueOrDefault(word) + 1;
        return counts;
    }

    /// <summary>The most used code and its count; a tie goes to the code that sorts first. Null when nothing was used.</summary>
    public static KeyValuePair<string, int>? Top(Dictionary<string, int> counts) =>
        counts.Count == 0
            ? null
            : counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .First();

    /// <summary>The words of a message, in order.</summary>
    public static string[] Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
