// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
