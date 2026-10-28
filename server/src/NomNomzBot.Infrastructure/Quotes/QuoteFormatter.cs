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

    /// <summary>
    /// Renders the quote's spoken form for TTS (S-OBS-12): <c>"{author} said: {text}"</c> when attributed,
    /// else just the text — no numbering or game context, since neither reads naturally aloud.
    /// </summary>
    public static string FormatSpoken(QuoteDto quote) =>
        string.IsNullOrWhiteSpace(quote.QuotedDisplayName)
            ? quote.Text
            : $"{quote.QuotedDisplayName} said: {quote.Text}";
}
