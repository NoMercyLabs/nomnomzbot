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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The time and day wording the ad, schedule and activity rows share.</summary>
internal static class BroadcastText
{
    private const int SecondsPerMinute = 60;
    private const int MinutesPerHour = 60;

    /// <summary>
    /// A span in the short style, rounded to the minute: <c>12m</c>, <c>3h</c>, <c>1h 5m</c>; under a minute it
    /// is spoken in seconds (<c>45s</c>). A negative span is zero.
    /// </summary>
    public static string Brief(TimeSpan span)
    {
        double seconds = Math.Max(0, span.TotalSeconds);
        if (seconds < SecondsPerMinute)
            return $"{(int)seconds}s";

        int minutes = (int)Math.Round(seconds / SecondsPerMinute, MidpointRounding.AwayFromZero);
        int hours = minutes / MinutesPerHour;
        int rest = minutes % MinutesPerHour;
        if (hours == 0)
            return $"{rest}m";

        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    /// <summary>The span in words, one unit (<c>4 days</c>), or two (<c>1 hour, 5 minutes</c>) when asked.</summary>
    public static string Words(TimeSpan span, CultureInfo culture, int precision = 1) =>
        TimeSpan.FromSeconds(Math.Max(0, span.TotalSeconds)).Humanize(precision, culture);

    /// <summary>The span as a phrase ahead of now: <c>in 4 days</c>, <c>over 4 dagen</c>.</summary>
    public static string Ahead(string words, CultureInfo culture) =>
        $"{(NumberCulture.IsDutch(culture) ? "over" : "in")} {words}";

    /// <summary>The moment on the clock of the channel zone: <c>15:42</c>.</summary>
    public static string Clock(DateTimeOffset moment, string? zone) =>
        StreamText.LocalTime(moment, zone);

    /// <summary>The moment in the channel zone.</summary>
    public static DateTimeOffset Local(DateTimeOffset moment, string? zone) =>
        TimeZoneInfo.ConvertTime(moment, StreamText.ZoneOf(zone));

    /// <summary>The day name and clock time: <c>Friday 20:00</c>.</summary>
    public static string DayAndTime(DateTimeOffset moment, string? zone, CultureInfo culture)
    {
        DateTimeOffset local = Local(moment, zone);
        return $"{culture.DateTimeFormat.GetDayName(local.DayOfWeek)} {Clock(moment, zone)}";
    }

    /// <summary>The day name and calendar date: <c>Friday 30 October</c>.</summary>
    public static string DayAndDate(DateTimeOffset moment, string? zone, CultureInfo culture)
    {
        DateTimeOffset local = Local(moment, zone);
        return $"{culture.DateTimeFormat.GetDayName(local.DayOfWeek)} {local.Day} "
            + culture.DateTimeFormat.GetMonthName(local.Month);
    }

    /// <summary>The short day name and clock time: <c>Fri 15:00</c>.</summary>
    public static string ShortDayAndTime(DateTimeOffset moment, string? zone, CultureInfo culture)
    {
        DateTimeOffset local = Local(moment, zone);
        return $"{culture.DateTimeFormat.GetAbbreviatedDayName(local.DayOfWeek)} {Clock(moment, zone)}";
    }

    /// <summary>The short day name, day of the month and short month: <c>Fri 6 Nov</c>.</summary>
    public static string ShortDate(DateTimeOffset moment, string? zone, CultureInfo culture)
    {
        DateTimeOffset local = Local(moment, zone);
        return $"{culture.DateTimeFormat.GetAbbreviatedDayName(local.DayOfWeek)} {local.Day} "
            + culture.DateTimeFormat.GetAbbreviatedMonthName(local.Month);
    }

    /// <summary>The start of the week (Monday) that holds the local date.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>A whole number from 1 to <paramref name="max"/>.</summary>
    public static bool TryCount(string text, int max, out int value) =>
        StreamText.TryPositive(text, max, out value);
}
