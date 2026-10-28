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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>The culture of the channel language, for the few rows that render in it (catalog section 7.4).</summary>
internal static class NumberCulture
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The culture of the channel language; English when it is unset or not a known culture.</summary>
    public static CultureInfo For(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return English;

        try
        {
            return CultureInfo.GetCultureInfo(language.Trim());
        }
        catch (CultureNotFoundException)
        {
            return English;
        }
    }

    /// <summary>True for a Dutch channel.</summary>
    public static bool IsDutch(CultureInfo culture) => culture.TwoLetterISOLanguageName == "nl";
}
