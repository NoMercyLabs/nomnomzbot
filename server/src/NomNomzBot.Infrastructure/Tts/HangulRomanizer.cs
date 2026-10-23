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

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Writes Korean syllables in Latin letters (Revised Romanization, one syllable at a time) so an English voice
/// can say a Korean name instead of staying silent. It does not model sound changes between syllables.
/// </summary>
public static class HangulRomanizer
{
    private const int FirstSyllable = 0xAC00;
    private const int LastSyllable = 0xD7A3;
    private const int MedialCount = 21;
    private const int FinalCount = 28;

    private static readonly string[] Initials =
    [
        "g",
        "kk",
        "n",
        "d",
        "tt",
        "r",
        "m",
        "b",
        "pp",
        "s",
        "ss",
        "",
        "j",
        "jj",
        "ch",
        "k",
        "t",
        "p",
        "h",
    ];

    private static readonly string[] Medials =
    [
        "a",
        "ae",
        "ya",
        "yae",
        "eo",
        "e",
        "yeo",
        "ye",
        "o",
        "wa",
        "wae",
        "oe",
        "yo",
        "u",
        "wo",
        "we",
        "wi",
        "yu",
        "eu",
        "ui",
        "i",
    ];

    // As the final consonant of a syllable is pronounced, which is how a name sounds.
    private static readonly string[] Finals =
    [
        "",
        "k",
        "k",
        "k",
        "n",
        "n",
        "n",
        "t",
        "l",
        "k",
        "m",
        "l",
        "l",
        "l",
        "p",
        "l",
        "m",
        "p",
        "p",
        "t",
        "t",
        "ng",
        "t",
        "t",
        "k",
        "t",
        "p",
        "t",
    ];

    public static bool HasHangul(string text) => text.Any(IsSyllable);

    /// <summary>The text with every Hangul syllable written in Latin letters; other characters stay.</summary>
    public static string Romanize(string text)
    {
        StringBuilder result = new(text.Length * 2);
        foreach (char character in text)
        {
            if (!IsSyllable(character))
            {
                result.Append(character);
                continue;
            }

            int index = character - FirstSyllable;
            int initial = index / (MedialCount * FinalCount);
            int medial = index % (MedialCount * FinalCount) / FinalCount;
            int final = index % FinalCount;
            result.Append(Initials[initial]).Append(Medials[medial]).Append(Finals[final]);
        }

        return result.ToString();
    }

    private static bool IsSyllable(char character) =>
        character is >= (char)FirstSyllable and <= (char)LastSyllable;
}
