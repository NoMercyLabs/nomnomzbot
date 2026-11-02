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
/// The duration wording of the old bot's <c>!accountage</c> and <c>!followage</c> replies, counted on the calendar
/// (<see cref="CalendarAge"/>), not in flat 30-day months. The two commands word the same span differently
/// (legacy parity), so each keeps its own format.
/// </summary>
public static class LegacyAgeText
{
    /// <summary>"1 year, 3 months, 2 days" — the old <c>!accountage</c> wording (comma-joined parts).</summary>
    public static string AccountAge(DateTimeOffset since, DateTimeOffset now)
    {
        CalendarAge age = CalendarAge.Between(since, now);
        if (age.Years > 0)
            return Plural(age.Years, "year")
                + Remainder(age.Months, "month")
                + Remainder(age.Days, "day");

        if (age.Months > 0)
            return Plural(age.Months, "month") + Remainder(age.Days, "day");

        return ShortSpan(age.Span);
    }

    /// <summary>"1 year and 20 days" — the old <c>!followage</c> wording (the remainder is always days).</summary>
    public static string FollowAge(DateTimeOffset since, DateTimeOffset now)
    {
        CalendarAge age = CalendarAge.Between(since, now);
        if (age.Years > 0)
            return Plural(age.Years, "year") + AndDays(age.DaysAfterYears);

        if (age.Months > 0)
            return Plural(age.Months, "month") + AndDays(age.Days);

        return ShortSpan(age.Span);
    }

    private static string ShortSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return Plural((int)span.TotalDays, "day");
        if (span.TotalHours >= 1)
            return Plural((int)span.TotalHours, "hour");
        return Plural(Math.Max(1, (int)span.TotalMinutes), "minute");
    }

    private static string Plural(int count, string unit) =>
        count == 1 ? $"1 {unit}" : $"{count} {unit}s";

    private static string Remainder(int count, string unit) =>
        count > 0 ? $", {Plural(count, unit)}" : string.Empty;

    private static string AndDays(int days) =>
        days > 0 ? $" and {Plural(days, "day")}" : string.Empty;
}
