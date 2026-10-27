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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

internal static class LiveText
{
    /// <summary>A whole number with no group separator: <c>3</c>.</summary>
    public static string Plain(long value) => StreamText.Plain(value);

    /// <summary>A yes-or-no state for <c>{if:...}</c>.</summary>
    public static string Flag(bool value) => NumberText.Flag(value);

    /// <summary>A whole number with the culture's group separator: <c>1,800</c>.</summary>
    public static string Count(long value, CultureInfo culture) =>
        StreamText.Grouped(value, culture);

    /// <summary>How much is left to reach <paramref name="whole"/>; never below zero.</summary>
    public static long Remaining(long part, long whole) => Math.Max(0, whole - part);

    /// <summary><c>60%</c>; null when there is no total to measure against.</summary>
    public static string? Percent(long part, long whole) =>
        whole <= 0 ? null : MusicText.Percent((double)part / whole);

    /// <summary><c>██████░░░░ 60%</c>; null when there is no total to measure against.</summary>
    public static string? Bar(long part, long whole) =>
        whole <= 0
            ? null
            : MeterBars.Draw(
                (decimal)part * 100 / whole,
                MeterBars.DefaultWidth,
                MeterBars.Full,
                MeterBars.Empty
            )
                + " "
                + Percent(part, whole);

    /// <summary>Time left until <paramref name="end"/> in the short style (<c>4m 10s</c>); <c>0s</c> once passed.</summary>
    public static string TimeLeft(DateTimeOffset end, DateTimeOffset now) =>
        StreamText.Short(TimeSpan.FromTicks(Math.Max(0, (end - now).Ticks)));

    /// <summary>
    /// A Twitch charity amount: the currency symbol and the value with its decimal places (<c>€1,000.00</c>); an
    /// unknown currency code follows the value (<c>420.00 XYZ</c>).
    /// </summary>
    public static string Money(TwitchCharityAmount amount, CultureInfo culture)
    {
        decimal value = amount.Value / (decimal)Math.Pow(10, amount.DecimalPlaces);
        string number = value.ToString("N" + amount.DecimalPlaces, culture);
        return NumberCurrencies.TrySymbol(amount.Currency, out string symbol)
            ? symbol + number
            : $"{number} {amount.Currency}";
    }
}
