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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Tts.Services;

/// <summary>
/// Speaks a short piece of public page text (the legal pages' Read Aloud) in the bot's own neural voice, so a
/// visitor hears the same voice as the app instead of the browser's. Open to anyone, so it takes short text only
/// and picks the voice itself: the platform default for English, a catalogue voice for the page language.
/// </summary>
public interface IReadAloudService
{
    /// <summary>
    /// The audio for <paramref name="text"/> in the voice of <paramref name="locale"/> (a language such as
    /// <c>nl</c> or a locale such as <c>nl-NL</c>). Fails with <c>VALIDATION_FAILED</c> for blank or over-long
    /// text, and <c>TTS_UNAVAILABLE</c> when the provider cannot speak right now.
    /// </summary>
    Task<Result<TtsResult>> SynthesizeAsync(
        string text,
        string locale,
        CancellationToken ct = default
    );
}
