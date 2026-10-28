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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// The text a lookup prints when its source is slow, down or unreachable (catalog section 9, rule 1), in the
/// channel language. The sentences are the same as the <c>template_helper_lookup_*_failed</c> strings of
/// <c>strings_template_lookup.xml</c>; a test keeps the two in step, because the server does not read that file.
/// </summary>
internal static class LookupFallbacks
{
    public const string Twitch = "twitch";
    public const string Weather = "weather";
    public const string Time = "time";
    public const string Currency = "currency";
    public const string Translate = "translate";

    private static readonly Dictionary<string, (string English, string Dutch)> Texts = new()
    {
        [Twitch] = ("(Twitch lookup unavailable)", "(Twitch-opzoeking niet beschikbaar)"),
        [Weather] = ("(weather unavailable)", "(weer niet beschikbaar)"),
        [Time] = ("(local time unavailable)", "(lokale tijd niet beschikbaar)"),
        [Currency] = ("(exchange rate unavailable)", "(wisselkoers niet beschikbaar)"),
        [Translate] = ("(translation unavailable)", "(vertaling niet beschikbaar)"),
    };

    public static IEnumerable<string> Sources => Texts.Keys;

    /// <summary>The resource name of the string that holds the same text.</summary>
    public static string ResourceName(string source) => $"template_helper_lookup_{source}_failed";

    public static string For(string source, CultureInfo culture) =>
        NumberCulture.IsDutch(culture) ? Texts[source].Dutch : Texts[source].English;

    public static string English(string source) => Texts[source].English;

    public static string Dutch(string source) => Texts[source].Dutch;
}
