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
using NomNomzBot.Infrastructure.Platform.Text;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Picks the song a viewer meant from a provider's search hits. The provider's order is the base; a hit
/// that shares no real word with the query is dropped, a hit by the artist the query names comes first,
/// a version of the song (karaoke, cover, remix, ...) the query did not ask for goes below the song, an
/// upload that is not the song at all (a reaction, a loop, an hours-long edit) goes below that, and an
/// official upload (a "- Topic" channel, a VEVO channel, an "Official" title) is preferred.
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

    /// <summary>Uploads around the song rather than of it — common on YouTube, never what "!sr song" meant.</summary>
    private static readonly string[] FillerPhrases =
    [
        "reaction",
        "reactions",
        "react",
        "reacts",
        "reacting",
        "loop",
        "looped",
        "1 hour",
        "10 hour",
        "10 hours",
        "hours",
    ];

    /// <summary>A hit is the requested song when this share of the query's title words is in its title.</summary>
    private const double TitleCoverageNeeded = 2.0 / 3.0;

    /// <summary>
    /// The same, when every word of the hit's title is also in the query (the query only adds the artist and
    /// filler around a short title).
    /// </summary>
    private const double TitleCoverageNeededWhenTitleIsInQuery = 0.5;

    /// <summary>
    /// The best hit whose TITLE is the requested song, or null when none is: a hit that only shares the
    /// artist (the artist's top track) is not the song and is never picked.
    /// </summary>
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
                .Where(entry => TitleMatches(entry.hit, queryWords)),
        ];
        if (survivors.Count == 0)
            return null;

        return survivors
            .OrderByDescending(entry => NamesArtist(entry.Hit, queryWords, compactQuery))
            .ThenBy(entry => HasUnaskedPhrase(entry.Hit, normalizedQuery, VersionPhrases))
            .ThenBy(entry => HasUnaskedPhrase(entry.Hit, normalizedQuery, FillerPhrases))
            .ThenByDescending(entry => IsOfficialUpload(entry.Hit))
            .ThenBy(entry => entry.Index)
            .First()
            .Hit;
    }

    /// <summary>
    /// The hit that comes nearest to the request without being it, to name in the "no match" reply; null
    /// when no hit shares a title word with the query.
    /// </summary>
    public static TrackInfo? Closest(string query, IReadOnlyList<TrackInfo> hits)
    {
        HashSet<string> queryWords = RealWords(Normalize(query));
        return hits.Select(
                (hit, index) => (Hit: hit, Index: index, Coverage: TitleCoverage(hit, queryWords))
            )
            .Where(entry => entry.Coverage > 0)
            .OrderByDescending(entry => entry.Coverage)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Hit)
            .FirstOrDefault();
    }

    private static bool TitleMatches(TrackInfo hit, HashSet<string> queryWords)
    {
        double coverage = TitleCoverage(hit, queryWords);
        if (coverage >= TitleCoverageNeeded)
            return true;
        if (coverage < TitleCoverageNeededWhenTitleIsInQuery)
            return false;

        HashSet<string> titleWords = RealWords(Normalize(hit.TrackName));
        return titleWords.All(titleWord => queryWords.Any(word => SameWord(word, titleWord)));
    }

    /// <summary>
    /// The share of the query's words, with the hit's own artist taken out, that its title carries.
    /// A query that is only the artist's name asks for any song by them, so it covers fully.
    /// </summary>
    private static double TitleCoverage(TrackInfo hit, HashSet<string> queryWords)
    {
        string normalizedArtist = Normalize(hit.Artist);
        string compactArtist = normalizedArtist.Replace(
            " ",
            string.Empty,
            StringComparison.Ordinal
        );
        HashSet<string> artistWords = RealWords(normalizedArtist);
        List<string> titleQueryWords =
        [
            .. queryWords.Where(word =>
                !artistWords.Any(artistWord => SameWord(artistWord, word))
                && !(compactArtist.Length >= 3 && word == compactArtist)
            ),
        ];
        if (titleQueryWords.Count == 0)
            return 1;

        HashSet<string> titleWords = RealWords(Normalize(hit.TrackName));
        int carried = titleQueryWords.Count(word =>
            titleWords.Any(titleWord => SameWord(word, titleWord))
        );
        return (double)carried / titleQueryWords.Count;
    }

    /// <summary>The same word, allowing one typo in a long word and two in a very long one.</summary>
    private static bool SameWord(string a, string b)
    {
        if (a == b)
            return true;
        int shortest = Math.Min(a.Length, b.Length);
        if (shortest < 5)
            return false;
        return EditDistance.Between(a, b) <= (shortest >= 9 ? 2 : 1);
    }

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

    /// <summary>A YouTube "Artist - Topic" channel, a VEVO channel, or an "Official" title.</summary>
    private static bool IsOfficialUpload(TrackInfo hit)
    {
        string artist = Normalize(hit.Artist);
        return artist.EndsWith(" topic", StringComparison.Ordinal)
            || artist
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .EndsWith("vevo", StringComparison.Ordinal)
            || $" {Normalize(hit.TrackName)} ".Contains(" official ", StringComparison.Ordinal);
    }

    private static bool HasUnaskedPhrase(TrackInfo hit, string normalizedQuery, string[] phrases)
    {
        string paddedQuery = $" {normalizedQuery} ";
        string paddedHit = $" {Normalize($"{hit.TrackName} {hit.Artist} {hit.Album}")} ";
        return phrases.Any(phrase =>
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
