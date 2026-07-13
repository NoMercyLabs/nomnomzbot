// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Quotes.Dtos;

namespace NomNomzBot.Infrastructure.Quotes;

/// <summary>
/// Renders a quote to its one-line chat form — the single source for both the <c>!quote</c> built-in and the
/// <c>post_quote</c> pipeline action, so the two never drift.
/// </summary>
public static class QuoteFormatter
{
    /// <summary>Renders <c>#{number}: "{text}" — {displayName} ({game})</c>, omitting empty attribution parts.</summary>
    public static string Format(QuoteDto quote)
    {
        string line = $"#{quote.Number}: \"{quote.Text}\"";

        if (!string.IsNullOrWhiteSpace(quote.QuotedDisplayName))
            line += $" — {quote.QuotedDisplayName}";

        if (!string.IsNullOrWhiteSpace(quote.ContextGame))
            line += $" ({quote.ContextGame})";

        return line;
    }
}
