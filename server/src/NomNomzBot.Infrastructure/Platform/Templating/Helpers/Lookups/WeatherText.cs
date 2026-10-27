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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>The words and symbols of a weather report: WMO codes, compass points and rounded degrees.</summary>
internal static class WeatherText
{
    private static readonly Dictionary<int, (string English, string Dutch, string Emoji)> Codes =
        new()
        {
            [0] = ("clear sky", "onbewolkt", "☀️"),
            [1] = ("mainly clear", "overwegend helder", "🌤️"),
            [2] = ("partly cloudy", "half bewolkt", "⛅"),
            [3] = ("overcast", "bewolkt", "☁️"),
            [45] = ("fog", "mist", "🌫️"),
            [48] = ("rime fog", "ijsmist", "🌫️"),
            [51] = ("light drizzle", "lichte motregen", "🌦️"),
            [53] = ("drizzle", "motregen", "🌦️"),
            [55] = ("heavy drizzle", "zware motregen", "🌦️"),
            [56] = ("light freezing drizzle", "lichte ijzel", "🌧️"),
            [57] = ("freezing drizzle", "ijzel", "🌧️"),
            [61] = ("light rain", "lichte regen", "🌧️"),
            [63] = ("rain", "regen", "🌧️"),
            [65] = ("heavy rain", "zware regen", "🌧️"),
            [66] = ("light freezing rain", "lichte ijsregen", "🌧️"),
            [67] = ("freezing rain", "ijsregen", "🌧️"),
            [71] = ("light snow", "lichte sneeuw", "🌨️"),
            [73] = ("snow", "sneeuw", "🌨️"),
            [75] = ("heavy snow", "zware sneeuw", "❄️"),
            [77] = ("snow grains", "sneeuwkorrels", "🌨️"),
            [80] = ("showers", "buien", "🌦️"),
            [81] = ("heavy showers", "zware buien", "🌧️"),
            [82] = ("violent showers", "hevige buien", "⛈️"),
            [85] = ("snow showers", "sneeuwbuien", "🌨️"),
            [86] = ("heavy snow showers", "zware sneeuwbuien", "🌨️"),
            [95] = ("thunderstorm", "onweer", "⛈️"),
            [96] = ("thunderstorm with hail", "onweer met hagel", "⛈️"),
            [99] = ("thunderstorm with heavy hail", "onweer met zware hagel", "⛈️"),
        };

    private static readonly string[] Compass = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
    private static readonly string[] CompassNl = ["N", "NO", "O", "ZO", "Z", "ZW", "W", "NW"];

    public static string Condition(int code, CultureInfo culture) =>
        Codes.TryGetValue(code, out (string English, string Dutch, string Emoji) text)
            ? NumberCulture.IsDutch(culture)
                ? text.Dutch
                : text.English
            : string.Empty;

    public static string Emoji(int code) =>
        Codes.TryGetValue(code, out (string English, string Dutch, string Emoji) text)
            ? text.Emoji
            : string.Empty;

    /// <summary>The eight-point compass name of a wind that blows FROM <paramref name="degrees"/>.</summary>
    public static string Direction(double degrees, CultureInfo culture)
    {
        int index = (int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8;
        return (NumberCulture.IsDutch(culture) ? CompassNl : Compass)[index];
    }

    public static string Celsius(double value) => Whole(value) + " °C";

    public static string Fahrenheit(double celsius) => Whole(celsius * 9 / 5 + 32) + " °F";

    /// <summary>A range such as <c>9–14 °C</c> (an en dash between the low and the high).</summary>
    public static string CelsiusRange(double low, double high) => Whole(low) + "–" + Celsius(high);

    private static string Whole(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
}
