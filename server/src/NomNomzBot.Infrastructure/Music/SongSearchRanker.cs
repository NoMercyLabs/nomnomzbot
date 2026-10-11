// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Picks the song a viewer meant from a provider's search hits. The provider's order is the base; a hit
/// that shares no real word with the query is dropped, a hit by the artist the query names comes first,
/// and a version of the song (karaoke, cover, remix, ...) the query did not ask for goes last.
/// </summary>
internal static class SongSearchRanker
{
    private static readonly HashSet<string> StopWords =
    [
        "a",
        "an",
        "the",
        "in",
        "on",
        "of",
        "to",
        "by",
        "and",
        "feat",
        "ft",
        "with",
        "for",
    ];

    private static readonly string[] VersionPhrases =
    [
        "karaoke",
        "cover",
        "instrumental",
        "remix",
        "live",
        "sped up",
        "slowed",
        "nightcore",
        "tribute",
        "8 bit",
        "lullaby",
    ];

    public static TrackInfo? Pick(string query, IReadOnlyList<TrackInfo> hits)
    {
        if (hits.Count == 0)
            return null;

        string normalizedQuery = Normalize(query);
        HashSet<string> queryWords = RealWords(normalizedQuery);
        string compactQuery = normalizedQuery.Replace(" ", string.Empty, StringComparison.Ordinal);

        List<(TrackInfo Hit, int Index)> survivors =
        [
            .. hits.Select((hit, index) => (hit, index))
                .Where(entry => SharesARealWord(entry.hit, queryWords, compactQuery)),
        ];
        if (survivors.Count == 0)
            return hits[0];

        return survivors
            .OrderByDescending(entry => NamesArtist(entry.Hit, queryWords, compactQuery))
            .ThenBy(entry => IsUnaskedVersion(entry.Hit, normalizedQuery))
            .ThenBy(entry => entry.Index)
            .First()
            .Hit;
    }

    private static bool SharesARealWord(
        TrackInfo hit,
        HashSet<string> queryWords,
        string compactQuery
    ) =>
        RealWords(Normalize($"{hit.TrackName} {hit.Artist}")).Overlaps(queryWords)
        || NamesArtist(hit, queryWords, compactQuery);

    private static bool NamesArtist(TrackInfo hit, HashSet<string> queryWords, string compactQuery)
    {
        string normalizedArtist = Normalize(hit.Artist);
        string compactArtist = normalizedArtist.Replace(
            " ",
            string.Empty,
            StringComparison.Ordinal
        );
        if (
            compactArtist.Length >= 3
            && compactQuery.Contains(compactArtist, StringComparison.Ordinal)
        )
            return true;

        HashSet<string> artistWords = RealWords(normalizedArtist);
        return artistWords.Count > 0 && artistWords.IsSubsetOf(queryWords);
    }

    private static bool IsUnaskedVersion(TrackInfo hit, string normalizedQuery)
    {
        string paddedQuery = $" {normalizedQuery} ";
        string paddedHit = $" {Normalize($"{hit.TrackName} {hit.Artist} {hit.Album}")} ";
        return VersionPhrases.Any(phrase =>
        {
            string padded = $" {phrase} ";
            return paddedHit.Contains(padded, StringComparison.Ordinal)
                && !paddedQuery.Contains(padded, StringComparison.Ordinal);
        });
    }

    private static HashSet<string> RealWords(string normalized) =>
        [
            .. normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => !StopWords.Contains(word)),
        ];

    /// <summary>Lower-case letters and digits only, accents removed, every other character a space.</summary>
    private static string Normalize(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (c == '\'' || c == '’')
                continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(
            ' ',
            builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );
    }
}
