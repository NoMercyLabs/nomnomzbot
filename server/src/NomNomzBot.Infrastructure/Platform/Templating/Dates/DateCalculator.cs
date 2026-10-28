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

using NodaTime;

namespace NomNomzBot.Infrastructure.Platform.Templating.Dates;

/// <summary>
/// Applies the calculation steps of a date expression with NodaTime. Years, months, weeks and days move the
/// wall clock (a day across a clock change stays at the same time of day; Jan 31 + 1 month is the last day of
/// February). Hours, minutes and seconds move real elapsed time.
/// </summary>
public static class DateCalculator
{
    public static ZonedDateTime Apply(ZonedDateTime start, IEnumerable<DateStep> steps)
    {
        ZonedDateTime current = start;
        foreach (DateStep step in steps)
            current = Apply(current, step);
        return current;
    }

    private static ZonedDateTime Apply(ZonedDateTime current, DateStep step) =>
        step switch
        {
            OffsetStep offset => offset.Parts.Aggregate(current, AddPart),
            BoundaryStep boundary => Snap(current, boundary),
            WeekdayStep weekday => current.Zone.AtLeniently(
                weekday.Next
                    ? current.LocalDateTime.Next(weekday.Day)
                    : current.LocalDateTime.Previous(weekday.Day)
            ),
            ZoneStep zone => current.WithZone(zone.Zone),
            _ => current,
        };

    private static ZonedDateTime AddPart(ZonedDateTime current, (int Amount, DateUnit Unit) part)
    {
        (int amount, DateUnit unit) = part;
        LocalDateTime local = current.LocalDateTime;
        return unit switch
        {
            DateUnit.Year => current.Zone.AtLeniently(local.PlusYears(amount)),
            DateUnit.Month => current.Zone.AtLeniently(local.PlusMonths(amount)),
            DateUnit.Week => current.Zone.AtLeniently(local.PlusWeeks(amount)),
            DateUnit.Day => current.Zone.AtLeniently(local.PlusDays(amount)),
            DateUnit.Hour => current.Plus(Duration.FromHours(amount)),
            DateUnit.Minute => current.Plus(Duration.FromMinutes(amount)),
            _ => current.Plus(Duration.FromSeconds(amount)),
        };
    }

    /// <summary>
    /// Start of a day, week (Monday, ISO-8601), month or year; the end is one tick before the next start, so
    /// <c>HH:mm:ss</c> shows <c>23:59:59</c>.
    /// </summary>
    private static ZonedDateTime Snap(ZonedDateTime current, BoundaryStep boundary)
    {
        LocalDate date = current.Date;
        LocalDate start = boundary.Unit switch
        {
            DateUnit.Week => date.With(DateAdjusters.PreviousOrSame(IsoDayOfWeek.Monday)),
            DateUnit.Month => date.With(DateAdjusters.StartOfMonth),
            DateUnit.Year => new LocalDate(date.Year, 1, 1),
            _ => date,
        };
        if (!boundary.End)
            return start.AtStartOfDayInZone(current.Zone);

        LocalDate nextStart = boundary.Unit switch
        {
            DateUnit.Week => start.PlusWeeks(1),
            DateUnit.Month => start.PlusMonths(1),
            DateUnit.Year => start.PlusYears(1),
            _ => start.PlusDays(1),
        };
        return current.Zone.AtLeniently(nextStart.AtMidnight().PlusTicks(-1));
    }
}
