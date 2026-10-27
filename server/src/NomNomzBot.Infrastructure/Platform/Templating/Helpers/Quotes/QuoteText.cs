// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Quotes.Dtos;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Quotes;

/// <summary>How a quote prints: the numbered line, the credit and the channel-local day.</summary>
internal static class QuoteText
{
    /// <summary><c>#12 "that was close"</c>.</summary>
    public static string Line(QuoteDto quote) => $"#{quote.Number} \"{quote.Text}\"";

    /// <summary><c>#12 "that was close" (ViewerTwo, 2026-03-01)</c>; only the credit parts the quote has.</summary>
    public static string LineWithCredit(QuoteDto quote, string? timezone)
    {
        List<string> credit = [];
        if (!string.IsNullOrWhiteSpace(quote.QuotedDisplayName))
            credit.Add(quote.QuotedDisplayName);
        string date = Date(quote, timezone);
        if (date.Length > 0)
            credit.Add(date);

        return credit.Count == 0 ? Line(quote) : $"{Line(quote)} ({string.Join(", ", credit)})";
    }

    /// <summary>The day the quote was said in the channel's zone (UTC when it has none); empty without a date.</summary>
    public static string Date(QuoteDto quote, string? timezone)
    {
        if (quote.QuotedAt is not { } quotedAt)
            return string.Empty;

        DateTimeOffset utc = new(DateTime.SpecifyKind(quotedAt, DateTimeKind.Utc));
        return TimeZoneInfo
            .ConvertTime(utc, TimeZoneOf(timezone))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo TimeZoneOf(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return TimeZoneInfo.Utc;

        return TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out TimeZoneInfo? zone)
            ? zone
            : TimeZoneInfo.Utc;
    }
}
