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

namespace NomNomzBot.Application.Commands.Builtin;

/// <summary>
/// The time between two moments counted on the calendar: whole years, then whole months, then the days left over.
/// A month is the real length of the months in between (28 to 31 days), never a flat 30, so "6 months and 7 days"
/// means what a calendar says. <see cref="DaysAfterYears"/> is the days left once only the whole years are taken
/// off, for the wording that skips months. <see cref="Span"/> is the plain elapsed time, for spans under a day.
/// A start after the end reads as no time at all.
/// </summary>
public readonly record struct CalendarAge(
    int Years,
    int Months,
    int Days,
    int DaysAfterYears,
    TimeSpan Span
)
{
    public static CalendarAge Between(DateTimeOffset since, DateTimeOffset now)
    {
        DateTime start = since.UtcDateTime;
        DateTime end = now.UtcDateTime;
        if (end <= start)
            return default;

        int years = end.Year - start.Year;
        if (start.AddYears(years) > end)
            years--;
        DateTime afterYears = start.AddYears(years);

        int months = ((end.Year - afterYears.Year) * 12) + end.Month - afterYears.Month;
        if (afterYears.AddMonths(months) > end)
            months--;
        DateTime afterMonths = afterYears.AddMonths(months);

        return new(
            years,
            months,
            (int)(end - afterMonths).TotalDays,
            (int)(end - afterYears).TotalDays,
            end - start
        );
    }
}
