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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>The prints and time rules the bot rows share.</summary>
internal static class BotText
{
    /// <summary>What a value that is not known prints: nothing.</summary>
    public const string Nothing = "";

    public const string ListSeparator = " ";

    public const string SourceCode = "https://github.com/NoMercyLabs/nomnomzbot";

    public const string License = "AGPL-3.0-or-later";

    /// <summary>The time between two moments, never below zero.</summary>
    public static TimeSpan Since(DateTimeOffset then, DateTimeOffset now) =>
        now > then ? now - then : TimeSpan.Zero;

    /// <summary>A day or more as <c>3d 4h</c>; below a day the stream style (<c>2h 20m</c>, <c>1m 30s</c>).</summary>
    public static string Span(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{span.Days}d {span.Hours}h" : StreamText.Short(span);

    /// <summary>The wall-clock moment in the channel zone: <c>2026-10-23 12:30</c>.</summary>
    public static string Local(DateTimeOffset moment, string? zoneId) =>
        TimeZoneInfo
            .ConvertTime(moment, StreamText.ZoneOf(zoneId))
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The configured public origin without a trailing slash; null for none, a bad text or a loopback host.</summary>
    public static string? PublicOrigin(string? configured)
    {
        if (
            string.IsNullOrWhiteSpace(configured)
            || !Uri.TryCreate(configured.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.IsLoopback
            || uri.Scheme is not ("http" or "https")
        )
            return null;

        return configured.Trim().TrimEnd('/');
    }

    /// <summary>The dashboard page of one channel (<c>/app/u/:channelName</c>).</summary>
    public static string DashboardLink(string origin, string channelName) =>
        $"{origin}/app/u/{channelName}";
}
