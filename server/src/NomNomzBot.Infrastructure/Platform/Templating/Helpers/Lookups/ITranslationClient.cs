// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
