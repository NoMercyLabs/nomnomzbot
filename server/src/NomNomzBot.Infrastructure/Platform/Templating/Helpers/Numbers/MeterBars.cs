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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>Draws a bar of full and empty marks; shared by <c>meter.*</c> and <c>num.bar</c>.</summary>
internal static class MeterBars
{
    public const int DefaultWidth = 10;
    public const int MaxWidth = 50;
    public const string Full = "█";
    public const string Empty = "░";

    /// <summary>A bar of <paramref name="width"/> marks that is <paramref name="percent"/> full (clamped to 0..100).</summary>
    public static string Draw(decimal percent, int width, string full, string empty)
    {
        int filled = (int)NumberText.Round(Clamp(percent) * width / 100, 0);
        return Repeat(full, filled) + Repeat(empty, width - filled);
    }

    /// <summary>The percent kept between 0 and 100.</summary>
    public static decimal Clamp(decimal percent) => Math.Clamp(percent, 0, 100);

    /// <summary>A width from 1 to <see cref="MaxWidth"/>.</summary>
    public static bool TryWidth(string text, out int width) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out width)
        && width is >= 1 and <= MaxWidth;

    private static string Repeat(string mark, int count) =>
        string.Concat(Enumerable.Repeat(mark, count));
}
