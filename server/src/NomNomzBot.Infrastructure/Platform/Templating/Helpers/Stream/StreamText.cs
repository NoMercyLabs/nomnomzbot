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
using System.Text;
using Humanizer;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>Text and time rules the stream and channel helpers share.</summary>
internal static class StreamText
{
    private const int LongPrecision = 2;
    private const string Ellipsis = "…";

    /// <summary>A whole count as plain digits.</summary>
    public static string Plain(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A whole count with the thousands separator of the channel language (<c>1,250</c>).</summary>
    public static string Grouped(long value, CultureInfo culture) => value.ToString("N0", culture);

    /// <summary>1234 becomes <c>1.2k</c>; the same rule as <c>num.compact</c>.</summary>
    public static string Compact(long value) => NumberText.Compact(value);

    /// <summary>The span in words, two units at most (<c>2 hours, 15 minutes</c>).</summary>
    public static string Words(TimeSpan span, CultureInfo culture) =>
        span.Humanize(LongPrecision, culture);

    /// <summary>
    /// The calendar time between two moments in whole years and months (<c>6 years, 2 months</c>). Humanizer
    /// has no calendar maths (it counts a year as 365.25 days and a month as 30.44), so the parts are counted
    /// here and each part is spoken on its own, in the language of the channel. Under a month it speaks the span.
    /// </summary>
    public static string AgeInWords(DateTimeOffset from, DateTimeOffset to, CultureInfo culture)
    {
        DateTime start = from.UtcDateTime;
        DateTime end = to.UtcDateTime;
        int months = (end.Year - start.Year) * 12 + end.Month - start.Month;
        if (start.AddMonths(months) > end)
            months--;

        if (months < 1)
            return (to - from).Humanize(1, culture, maxUnit: TimeUnit.Day);

        List<string> parts = [];
        if (months >= 12)
            parts.Add(
                TimeSpan.FromDays(months / 12 * 366).Humanize(1, culture, maxUnit: TimeUnit.Year)
            );
        if (months % 12 > 0)
            parts.Add(
                TimeSpan.FromDays(months % 12 * 31).Humanize(1, culture, maxUnit: TimeUnit.Month)
            );
        return string.Join(", ", parts);
    }

    /// <summary>The short style of the stream uptime: <c>4h 10m</c>, <c>1m 30s</c>, <c>45s</c>.</summary>
    public static string Short(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{(long)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1)
            return $"{span.Minutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }

    /// <summary><c>2:15:00</c>; below an hour <c>15:00</c>.</summary>
    public static string Clock(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(long)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";

    /// <summary>The span as hours with at most two decimals (<c>2.25</c>).</summary>
    public static string Hours(TimeSpan span) =>
        NumberText.Plain(NumberText.Round((decimal)span.TotalHours, 2));

    /// <summary>
    /// How long ago, rounded to the nearest whole day, hour or minute and spoken with Humanizer's own words
    /// (<c>2 days ago</c> for 1 day and 16 hours).
    /// </summary>
    public static string Ago(DateTimeOffset then, DateTimeOffset now, CultureInfo culture)
    {
        TimeSpan span = now - then;
        TimeSpan rounded =
            span.TotalDays >= 1
                ? TimeSpan.FromDays(Math.Round(span.TotalDays, MidpointRounding.AwayFromZero))
            : span.TotalHours >= 1
                ? TimeSpan.FromHours(Math.Round(span.TotalHours, MidpointRounding.AwayFromZero))
            : span.TotalMinutes >= 1
                ? TimeSpan.FromMinutes(Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero))
            : span;
        return (now - rounded).Humanize(now, culture);
    }

    /// <summary>The channel's time zone, or UTC when it is unset or unknown.</summary>
    public static TimeZoneInfo ZoneOf(string? zoneId) =>
        zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out TimeZoneInfo? zone)
            ? zone
            : TimeZoneInfo.Utc;

    /// <summary>The wall-clock time in the channel's zone, <c>15:30</c>.</summary>
    public static string LocalTime(DateTimeOffset moment, string? zoneId) =>
        TimeZoneInfo
            .ConvertTime(moment, ZoneOf(zoneId))
            .ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The UTC calendar date, <c>2026-10-24</c>.</summary>
    public static string Date(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The text cut to <paramref name="max"/> characters (text elements, as the text helpers count) and closed
    /// with <c>…</c>; text that fits stays as it is.
    /// </summary>
    public static string Cut(string text, int max)
    {
        StringInfo info = new(text);
        return info.LengthInTextElements <= max
            ? text
            : info.SubstringByTextElements(0, max).TrimEnd() + Ellipsis;
    }

    /// <summary>The Twitch directory slug of a category name: lowercase, words joined by one dash.</summary>
    public static string Slug(string name)
    {
        StringBuilder slug = new();
        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                slug.Append(c);
            else if (c != '\'' && slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }

        return slug.ToString().TrimEnd('-');
    }

    /// <summary>The text with its first letter in upper case (a stored tone <c>informative</c> reads <c>Informative</c>).</summary>
    public static string Capitalized(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>A ranked name with its count: <c>ViewerOne (140)</c>.</summary>
    public static string Named(StreamStatsNamedCountDto entry, CultureInfo culture) =>
        $"{entry.Name} ({Grouped(entry.Count, culture)})";

    /// <summary>A whole number of at least 1 and at most <paramref name="max"/>.</summary>
    public static bool TryPositive(string text, int max, out int value) =>
        ParsedArgument.TryWhole(text, out value) && value >= 1 && value <= max;
}
