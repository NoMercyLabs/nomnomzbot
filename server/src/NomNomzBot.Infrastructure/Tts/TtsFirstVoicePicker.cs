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

using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Picks the first voice a viewer with no saved voice gets: from the English voices plus the multilingual voices,
/// preferring a voice whose gender fits the viewer's pronouns (he is male, she is female; they, any or unknown
/// prefer a neutral voice when the catalogue has one, else any). When no voice fits the preference, the whole
/// English plus multilingual pool is used.
/// </summary>
public static class TtsFirstVoicePicker
{
    private const string Male = "male";
    private const string Female = "female";
    private const string Neutral = "neutral";

    /// <summary>True for an English voice or a multilingual voice (see <see cref="TtsVoiceSampleComposer.IsMultilingual"/>).</summary>
    public static bool IsEligible(TtsVoice voice) =>
        voice.Locale.StartsWith("en-", StringComparison.OrdinalIgnoreCase)
        || TtsVoiceSampleComposer.IsMultilingual(voice.Name, voice.Provider);

    /// <summary>The voice id picked from <paramref name="voices"/>, or null when none is eligible.</summary>
    public static string? Pick(
        IReadOnlyList<TtsVoice> voices,
        Pronoun? primary,
        Pronoun? alt,
        Random random
    )
    {
        List<TtsVoice> pool = voices
            .Where(IsEligible)
            .OrderBy(v => v.Id, StringComparer.Ordinal)
            .ToList();
        if (pool.Count == 0)
            return null;

        string wanted = WantedGender(primary, alt);
        List<TtsVoice> preferred = pool.Where(v =>
                string.Equals(v.Gender, wanted, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        List<TtsVoice> source = preferred.Count > 0 ? preferred : pool;
        return source[random.Next(source.Count)].Id;
    }

    // he/him is male, she/her is female; they/them, any, unknown or a he plus she pair prefers neutral.
    private static string WantedGender(Pronoun? primary, Pronoun? alt)
    {
        HashSet<string> genders = [];
        foreach (Pronoun? pronoun in new[] { primary, alt })
        {
            string? gender = GenderOf(pronoun);
            if (gender is not null)
                genders.Add(gender);
        }

        return genders.Count == 1 ? genders.Single() : Neutral;
    }

    private static string? GenderOf(Pronoun? pronoun) =>
        pronoun?.Subject.ToLowerInvariant() switch
        {
            "he" => Male,
            "she" => Female,
            _ => null,
        };
}
