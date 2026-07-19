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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Tts.Entities;

/// <summary>
/// One per-channel pronunciation-lexicon rule: whenever <see cref="Phrase"/> appears in an utterance
/// (usernames and message content alike), TTS speaks <see cref="Replacement"/> instead. The one generic
/// mechanism behind username pronunciation ("xX_JD_Xx" → "Jaydee") and slang expansion ("brb" → "be right
/// back"). <see cref="MatchKind"/> picks the matcher: <c>word</c> (whole-word, case-insensitive — the
/// default) or <c>exact</c> (case-sensitive literal substring).
/// </summary>
public class TtsLexiconEntry : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>The text to match in the utterance.</summary>
    [MaxLength(100)]
    public string Phrase { get; set; } = null!;

    /// <summary>What TTS speaks in its place.</summary>
    [MaxLength(200)]
    public string Replacement { get; set; } = null!;

    /// <summary>Matcher: <c>word</c> (whole-word, case-insensitive) | <c>exact</c> (case-sensitive literal).</summary>
    [MaxLength(10)]
    public string MatchKind { get; set; } = TtsLexiconMatchKinds.Word;
}

/// <summary>The closed set of lexicon matchers.</summary>
public static class TtsLexiconMatchKinds
{
    public const string Word = "word";
    public const string Exact = "exact";

    public static bool IsValid(string kind) => kind is Word or Exact;
}
