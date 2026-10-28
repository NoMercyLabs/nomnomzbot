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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>A count of seconds as a short duration (<c>2h 15m</c>), in words (Humanizer) or as a clock (<c>1:23</c>).</summary>
internal sealed class NumberDurationHelpers()
    : NumberHelperGroup(
        Seconds("num.duration", "2h 15m", (s, _) => Short(s)),
        Seconds("num.duration.long", "2 hours, 15 minutes", Long),
        Seconds("num.duration.clock", "1:23", (s, _) => Clock(s))
    )
{
    /// <summary>The longest span a row accepts: 100000 days keeps every conversion inside <see cref="TimeSpan"/>.</summary>
    private const long MaxSeconds = 8_640_000_000;

    private const int LongPrecision = 2;

    private static NumberHelperRow Seconds(
        string key,
        string sample,
        Func<TimeSpan, CultureInfo, string> render
    ) =>
        NumberHelperRow.WithCulture(
            key,
            sample,
            (args, culture) =>
                args is [string text]
                && NumberArguments.TryWhole(text, out long seconds)
                && seconds is >= 0 and <= MaxSeconds
                    ? render(TimeSpan.FromSeconds(seconds), culture)
                    : null
        );

    /// <summary>The style of the stream uptime: <c>2h 15m</c>, <c>1m 30s</c>, <c>45s</c>.</summary>
    private static string Short(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{(long)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1)
            return $"{span.Minutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }

    private static string Long(TimeSpan span, CultureInfo culture) =>
        span.Humanize(LongPrecision, culture);

    /// <summary><c>1:23</c> below an hour, <c>1:01:40</c> from an hour.</summary>
    private static string Clock(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(long)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
}
