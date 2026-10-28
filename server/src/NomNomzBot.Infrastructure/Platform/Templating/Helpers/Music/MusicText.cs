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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

internal static class MusicText
{
    private const string Bar = "▬";
    private const string Knob = "🔘";

    public const int DefaultBarWidth = 11;
    public const int MaxBarWidth = 50;
    public const int MaxListLength = 20;

    /// <summary><c>Song A by Artist A</c>; the title alone when the artist is unknown.</summary>
    public static string? Track(string? title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        return string.IsNullOrWhiteSpace(artist) ? title : $"{title} by {artist}";
    }

    /// <summary>A length in milliseconds as a clock: <c>3:30</c>, or <c>1:02:03</c> from an hour.</summary>
    public static string Clock(long milliseconds) =>
        StreamText.Clock(TimeSpan.FromSeconds(Math.Max(0, milliseconds) / 1000));

    /// <summary>A length in seconds in the short style: <c>17m 20s</c>.</summary>
    public static string Short(long seconds) =>
        StreamText.Short(TimeSpan.FromSeconds(Math.Max(0, seconds)));

    /// <summary>How far a track is, from 0 to 1; null when the track has no length.</summary>
    public static double? Fraction(int progressMs, int durationMs) =>
        durationMs <= 0 ? null : Math.Clamp((double)progressMs / durationMs, 0, 1);

    public static string Percent(double fraction) =>
        Math.Round(fraction * 100, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// A bar of <paramref name="width"/> cells with a knob at the track position: cell 0 is the start and the
    /// last cell the end, so the knob sits on the cell nearest to <c>fraction * (width - 1)</c>.
    /// </summary>
    public static string ProgressBar(double fraction, int width)
    {
        int at = (int)Math.Round(fraction * (width - 1), MidpointRounding.AwayFromZero);
        return string.Concat(Enumerable.Repeat(Bar, at))
            + Knob
            + string.Concat(Enumerable.Repeat(Bar, width - 1 - at));
    }

    /// <summary>A whole number from 1 to <paramref name="max"/>.</summary>
    public static bool TryCount(string text, int max, out int value) =>
        StreamText.TryPositive(text, max, out value);

    /// <summary>A bar width from 1 to <see cref="MaxBarWidth"/>.</summary>
    public static bool TryWidth(string text, out int width) =>
        StreamText.TryPositive(text, MaxBarWidth, out width);

    public static bool SameName(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && string.Equals(left.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
