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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>The translation source behind the <c>lookup.translate</c> forms (MyMemory, catalog section 9).</summary>
internal interface ITranslationClient
{
    /// <summary>The text in the target language, or null on failure. Language codes are ISO 639 (<c>en</c>, <c>nl</c>).</summary>
    Task<string?> TranslateAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken
    );
}
