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

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Application.Tts.Dtos;

/// <summary>
/// One pronunciation-lexicon rule: when <paramref name="Phrase"/> appears in an utterance, TTS speaks
/// <paramref name="Replacement"/>. <paramref name="Ipa"/> is the exact sound of the replacement, for a speech
/// provider that reads the International Phonetic Alphabet; <c>null</c> when the rule has none.
/// </summary>
public sealed record TtsLexiconEntryDto(
    Guid Id,
    string Phrase,
    string Replacement,
    string MatchKind,
    string? Ipa = null
);

/// <summary>A text after the lexicon: the plain words every voice can say, and the exact sound of the words that have one.</summary>
public sealed record TtsPronouncedText(string Text, IReadOnlyList<TtsPhoneme> Phonemes);

/// <summary>Request to create or update a lexicon rule. <c>word</c> matches whole words case-insensitively; <c>exact</c> is a case-sensitive literal.</summary>
public sealed record UpsertTtsLexiconEntryDto
{
    [Required]
    [MaxLength(100)]
    public string Phrase { get; init; } = null!;

    [Required]
    [MaxLength(200)]
    public string Replacement { get; init; } = null!;

    /// <summary>The exact sound of the replacement in the International Phonetic Alphabet. Leave empty for none.</summary>
    [MaxLength(200)]
    public string? Ipa { get; init; }

    [RegularExpression("^(word|exact)$")]
    public string MatchKind { get; init; } = "word";
}
