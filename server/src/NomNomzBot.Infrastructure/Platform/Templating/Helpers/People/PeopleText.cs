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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>The text forms the viewer rows print: counts, dates in the channel zone, ages and tiers.</summary>
internal static class PeopleText
{
    /// <summary>A state is printed as <c>true</c> or <c>false</c> so <c>if</c> and <c>switch</c> can test it.</summary>
    public static string Flag(bool value) => value ? "true" : "false";

    /// <summary>A whole number with the culture's group separator: <c>2,500</c>.</summary>
    public static string Count(long value, CultureInfo culture) => value.ToString("N0", culture);

    /// <summary>A whole number with no group separator: <c>2500</c>.</summary>
    public static string Raw(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The date (<c>yyyy-MM-dd</c>) of a moment on the channel's calendar.</summary>
    public static string Date(DateTimeOffset moment, string? timezone) =>
        TimeZoneInfo
            .ConvertTime(moment, ZoneOf(timezone))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>How long ago a moment was, in the channel's language: <c>2 days ago</c>.</summary>
    public static string Relative(DateTimeOffset moment, DateTimeOffset now, CultureInfo culture) =>
        moment.UtcDateTime.Humanize(
            utcDate: true,
            dateToCompareAgainst: now.UtcDateTime,
            culture: culture
        );

    /// <summary>A stored UTC timestamp (kind not set by the database) as a moment.</summary>
    public static DateTimeOffset Utc(DateTime stored) =>
        new(DateTime.SpecifyKind(stored, DateTimeKind.Utc));

    /// <summary>Watch time in hours with at most one decimal: <c>12.5</c>.</summary>
    public static string Hours(long seconds) =>
        Math.Round(Math.Max(0, seconds) / 3600d, 1).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Watch time as words, to the minute: <c>12 hours, 30 minutes</c>.</summary>
    public static string WatchTime(long seconds, CultureInfo culture) =>
        TimeSpan
            .FromSeconds(Math.Max(0, seconds))
            .Humanize(2, culture, maxUnit: TimeUnit.Hour, minUnit: TimeUnit.Minute);

    /// <summary>Whole days between two moments (never negative).</summary>
    public static long Days(DateTimeOffset from, DateTimeOffset to) =>
        Math.Max(0, (long)(to - from).TotalDays);

    /// <summary>
    /// The age of an account in calendar years and months: <c>7 years, 5 months</c>; under a month it falls back to days.
    /// </summary>
    public static string Age(DateTimeOffset since, DateTimeOffset now, CultureInfo culture)
    {
        int months = ((now.Year - since.Year) * 12) + now.Month - since.Month;
        if (now.Day < since.Day)
            months--;
        months = Math.Max(0, months);

        // Humanizer splits a span at 365 days a year and 30.44 days a month; 31 keeps a whole month a whole month.
        TimeSpan span =
            months > 0
                ? TimeSpan.FromDays(((months / 12) * 365) + ((months % 12) * 31))
                : TimeSpan.FromDays(Days(since, now));
        return span.Humanize(2, culture, maxUnit: TimeUnit.Year, minUnit: TimeUnit.Day);
    }

    /// <summary>The tier number of a Twitch sub tier (<c>1000</c> is <c>1</c>); empty when there is no tier.</summary>
    public static string TierNumber(string? tier) =>
        tier switch
        {
            "1000" => "1",
            "2000" => "2",
            "3000" => "3",
            null or "" => string.Empty,
            _ => tier,
        };

    /// <summary>The platform's name as people write it.</summary>
    public static string PlatformName(string provider) =>
        provider.ToLowerInvariant() switch
        {
            "youtube" => "YouTube",
            "x" => "X",
            "" => string.Empty,
            string other => string.Concat(other[..1].ToUpperInvariant(), other.AsSpan(1)),
        };

    private static TimeZoneInfo ZoneOf(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
