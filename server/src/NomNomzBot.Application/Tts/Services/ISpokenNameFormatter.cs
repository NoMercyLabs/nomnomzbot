// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
