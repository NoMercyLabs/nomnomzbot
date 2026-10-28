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

using System.Globalization;
using Humanizer;
using NodaTime;

namespace NomNomzBot.Infrastructure.Platform.Templating.Dates;

/// <summary>
/// Renders the distance between two moments: a whole total in one unit ("411 days"), a calendar breakdown
/// ("1 year, 1 month, 16 days") or a humanized duration ("2 hours, 15 minutes"). The order of the two moments
/// does not matter. Calendar units count on the wall clock (NodaTime periods); hours, minutes and seconds
/// count real elapsed time, so a night with a clock change is 23 or 25 hours. Unit words come from Humanizer
/// in the channel language.
/// </summary>
public static class DateSpanFormatter
{
    private const string Separator = ", ";

    private static readonly (
        PeriodUnits Units,
        Func<Period, long> Read,
        TimeUnit Word
    )[] BreakdownParts =
    [
        (PeriodUnits.Years, p => p.Years, TimeUnit.Year),
        (PeriodUnits.Months, p => p.Months, TimeUnit.Month),
        (PeriodUnits.Days, p => p.Days, TimeUnit.Day),
        (PeriodUnits.Hours, p => p.Hours, TimeUnit.Hour),
        (PeriodUnits.Minutes, p => p.Minutes, TimeUnit.Minute),
        (PeriodUnits.Seconds, p => p.Seconds, TimeUnit.Second),
    ];

    public static string Format(
        ZonedDateTime a,
        ZonedDateTime b,
        SpanUnit unit,
        CultureInfo culture
    )
    {
        ZonedDateTime earlier = a.ToInstant() <= b.ToInstant() ? a : b;
        ZonedDateTime later = (earlier == a ? b : a).WithZone(earlier.Zone);

        return unit switch
        {
            SpanUnit.Breakdown => Breakdown(earlier.LocalDateTime, later.LocalDateTime, culture),
            SpanUnit.Human => (later.ToInstant() - earlier.ToInstant())
                .ToTimeSpan()
                .Humanize(
                    precision: 2,
                    culture: culture,
                    maxUnit: TimeUnit.Year,
                    minUnit: TimeUnit.Second
                ),
            _ => Total(earlier, later, unit, culture),
        };
    }

    private static string Total(
        ZonedDateTime earlier,
        ZonedDateTime later,
        SpanUnit unit,
        CultureInfo culture
    )
    {
        Duration elapsed = later.ToInstant() - earlier.ToInstant();
        (long count, TimeUnit word) = unit switch
        {
            SpanUnit.Years => (
                CalendarCount(earlier, later, PeriodUnits.Years).Years,
                TimeUnit.Year
            ),
            SpanUnit.Months => (
                CalendarCount(earlier, later, PeriodUnits.Months).Months,
                TimeUnit.Month
            ),
            SpanUnit.Weeks => (
                CalendarCount(earlier, later, PeriodUnits.Days).Days / 7,
                TimeUnit.Week
            ),
            SpanUnit.Days => (CalendarCount(earlier, later, PeriodUnits.Days).Days, TimeUnit.Day),
            SpanUnit.Hours => ((long)elapsed.TotalHours, TimeUnit.Hour),
            SpanUnit.Minutes => ((long)elapsed.TotalMinutes, TimeUnit.Minute),
            _ => ((long)elapsed.TotalSeconds, TimeUnit.Second),
        };
        return Words(word, count, culture);
    }

    private static Period CalendarCount(
        ZonedDateTime earlier,
        ZonedDateTime later,
        PeriodUnits units
    ) => Period.Between(earlier.LocalDateTime, later.LocalDateTime, units);

    private static string Breakdown(LocalDateTime earlier, LocalDateTime later, CultureInfo culture)
    {
        PeriodUnits all = BreakdownParts.Aggregate(
            PeriodUnits.None,
            (acc, part) => acc | part.Units
        );
        Period period = Period.Between(earlier, later, all);

        List<string> words =
        [
            .. BreakdownParts
                .Select(part => (Count: part.Read(period), part.Word))
                .Where(part => part.Count != 0)
                .Select(part => Words(part.Word, part.Count, culture)),
        ];
        return words.Count == 0
            ? TimeSpan.Zero.Humanize(culture: culture)
            : string.Join(Separator, words);
    }

    private static string Words(TimeUnit unit, long count, CultureInfo culture) =>
        Configurator
            .Formatters.ResolveForCulture(culture)
            .TimeSpanHumanize(unit, (int)Math.Min(count, int.MaxValue));
}
