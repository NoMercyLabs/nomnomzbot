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

namespace NomNomzBot.Application.Tts.Services;

/// <summary>
/// Turns a viewer or channel name into something a voice reads well ("xX_D4rk_Xx" becomes "Dark",
/// "Stoney_Eagle" becomes "Stoney Eagle"). Pure and stateless. The channel's own pronunciation override
/// is applied before this and always wins.
/// </summary>
public interface ISpokenNameFormatter
{
    /// <summary>The spoken form of one name. Never empty: a name that cleans to nothing comes back as given.</summary>
    string Format(string name);

    /// <summary>
    /// Rewrite the known <paramref name="names"/> and every <c>@mention</c> in <paramref name="text"/> to their
    /// spoken form, in one pass. Bare words that merely look like names are left alone.
    /// </summary>
    string ApplyToText(string text, IReadOnlyList<string>? names);
}
