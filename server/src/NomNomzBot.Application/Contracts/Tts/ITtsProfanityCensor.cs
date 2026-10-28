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

namespace NomNomzBot.Application.Contracts.Tts;

/// <summary>
/// The opt-out light swear filter for TTS (tts.md §3.5) — deliberately thin: AutoMod upstream is the real filter,
/// this only masks mild profanity so a cautious channel's spoken text stays tame. Pure, no I/O.
/// </summary>
public interface ITtsProfanityCensor
{
    /// <summary>
    /// Masks mild profanity in <paramref name="text"/> using a built-in light word list. Pure function; no I/O, no
    /// persistence, no events. Returns the (possibly unchanged) text and whether anything was masked.
    /// </summary>
    TtsCensorResult Censor(string text);
}

/// <summary>The result of a censor pass: the (possibly rewritten) text and whether any word was masked.</summary>
public sealed record TtsCensorResult(string Text, bool WasCensored);
