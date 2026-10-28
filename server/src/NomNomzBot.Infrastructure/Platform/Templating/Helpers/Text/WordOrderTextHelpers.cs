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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>Ordering the words of a text: alphabetical sort and removing repeats.</summary>
internal sealed class WordOrderTextHelpers()
    : TextHelperGroup(
        TextHelperRow.OfText(
            "text.words.sort",
            "a b c",
            text =>
                TextArguments.Join(
                    TextArguments.Words(text).Order(StringComparer.OrdinalIgnoreCase)
                )
        ),
        TextHelperRow.OfText(
            "text.words.unique",
            "a b",
            text => TextArguments.Join(TextArguments.Words(text).Distinct(StringComparer.Ordinal))
        )
    );
