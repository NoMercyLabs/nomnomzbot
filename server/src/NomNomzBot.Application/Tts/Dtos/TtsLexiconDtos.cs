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

namespace NomNomzBot.Application.Tts.Dtos;

/// <summary>One pronunciation-lexicon rule: when <paramref name="Phrase"/> appears in an utterance, TTS speaks <paramref name="Replacement"/>.</summary>
public sealed record TtsLexiconEntryDto(
    Guid Id,
    string Phrase,
    string Replacement,
    string MatchKind
);

/// <summary>Request to create or update a lexicon rule. <c>word</c> matches whole words case-insensitively; <c>exact</c> is a case-sensitive literal.</summary>
public sealed record UpsertTtsLexiconEntryDto
{
    [Required]
    [MaxLength(100)]
    public string Phrase { get; init; } = null!;

    [Required]
    [MaxLength(200)]
    public string Replacement { get; init; } = null!;

    [RegularExpression("^(word|exact)$")]
    public string MatchKind { get; init; } = "word";
}
