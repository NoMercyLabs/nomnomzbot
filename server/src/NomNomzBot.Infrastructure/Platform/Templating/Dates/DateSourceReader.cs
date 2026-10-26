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
using NodaTime;
using NodaTime.Text;

namespace NomNomzBot.Infrastructure.Platform.Templating.Dates;

/// <summary>
/// Turns the source text of a date expression into a moment in the channel zone. <c>now</c> is the channel
/// clock, <c>utc</c> is the same instant in UTC, a date or date-time without an offset is read as channel
/// local time, and a value with an offset or <c>Z</c> (stream start, follow date, event time) is converted.
/// </summary>
public static class DateSourceReader
{
    private static readonly LocalDateTimePattern[] LocalDateTimePatterns =
    [
        LocalDateTimePattern.ExtendedIso,
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm"),
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd HH':'mm':'ss"),
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd HH':'mm"),
    ];

    /// <summary>Reads <paramref name="source"/>; null when it is not a date this reader knows.</summary>
    public static ZonedDateTime? Read(string source, DateExpressionContext context)
    {
        string text = source.Trim();
        if (text.Equals("now", StringComparison.OrdinalIgnoreCase))
            return context.Now.InZone(context.Zone);
        if (text.Equals("utc", StringComparison.OrdinalIgnoreCase))
            return context.Now.InUtc();

        ParseResult<LocalDate> date = LocalDatePattern.Iso.Parse(text);
        if (date.Success)
            return date.Value.AtStartOfDayInZone(context.Zone);

        foreach (LocalDateTimePattern pattern in LocalDateTimePatterns)
        {
            ParseResult<LocalDateTime> local = pattern.Parse(text);
            if (local.Success)
                return local.Value.InZoneLeniently(context.Zone);
        }

        return ReadInstant(text)?.InZone(context.Zone);
    }

    private static Instant? ReadInstant(string text)
    {
        // Only a value that carries its own offset or Z is an instant; anything else is not a date here.
        bool hasOffset =
            text.EndsWith('Z')
            || text.EndsWith('z')
            || (text.Length > 6 && (text[^6] == '+' || text[^6] == '-') && text[^3] == ':');
        if (!hasOffset)
            return null;

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset parsed
        )
            ? Instant.FromDateTimeOffset(parsed)
            : null;
    }
}
