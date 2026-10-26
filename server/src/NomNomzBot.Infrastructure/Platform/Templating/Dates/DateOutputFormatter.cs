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
using Humanizer;
using NodaTime;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Platform.Templating.Dates;

/// <summary>
/// Renders the moment a date expression produced: a .NET custom or standard format (with <c>[o]</c> for the
/// ordinal day), relative text, the ordinal day, or the distance to another moment.
/// </summary>
public static class DateOutputFormatter
{
    public const string DefaultPattern = "yyyy-MM-dd HH:mm";
    private const string OrdinalToken = "[o]";

    public static Result<string> Render(
        ZonedDateTime value,
        DateOutput? output,
        DateExpressionContext context,
        CultureInfo culture
    ) =>
        output switch
        {
            null => FormatPattern(value, DefaultPattern, culture),
            FormatOutput format => FormatPattern(value, format.Pattern, culture),
            RelativeOutput => Result<string>.Success(
                value
                    .ToDateTimeUtc()
                    .Humanize(
                        utcDate: true,
                        dateToCompareAgainst: context.Now.ToDateTimeUtc(),
                        culture: culture
                    )
            ),
            OrdinalOutput => Result<string>.Success(value.Day.Ordinalize(culture)),
            SpanOutput span => RenderSpan(value, span, context, culture),
            _ => Result<string>.Failure("unknown output step", "TEMPLATE_INVALID_DATE_EXPRESSION"),
        };

    private static Result<string> RenderSpan(
        ZonedDateTime value,
        SpanOutput span,
        DateExpressionContext context,
        CultureInfo culture
    )
    {
        ZonedDateTime? other = span.Other is null
            ? context.Now.InZone(value.Zone)
            : DateSourceReader.Read(span.Other, context);
        if (other is null)
            return Result<string>.Failure(
                $"'{span.Other}' is not a date",
                "TEMPLATE_INVALID_DATE_EXPRESSION"
            );

        return Result<string>.Success(
            DateSpanFormatter.Format(value, other.Value, span.Unit, culture)
        );
    }

    private static Result<string> FormatPattern(
        ZonedDateTime value,
        string pattern,
        CultureInfo culture
    )
    {
        DateTimeOffset moment = value.ToDateTimeOffset();
        string ordinal = value.Day.Ordinalize(culture);
        try
        {
            IEnumerable<string> pieces = pattern
                .Split(OrdinalToken)
                .Select(piece =>
                    piece.Length == 0 ? string.Empty : moment.ToString(piece, culture)
                );
            return Result<string>.Success(string.Join(ordinal, pieces));
        }
        catch (FormatException)
        {
            return Result<string>.Failure(
                $"'{pattern}' is not a valid date format",
                "TEMPLATE_INVALID_DATE_EXPRESSION"
            );
        }
    }
}
