// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
