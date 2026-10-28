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

/// <summary>The symbol of an ISO 4217 currency code, read once from the regions the platform knows.</summary>
internal static class NumberCurrencies
{
    private static readonly Lazy<Dictionary<string, string>> Symbols = new(Build);

    /// <summary>The symbol (<c>$</c>, <c>€</c>) of a three-letter code in any letter case; false for an unknown code.</summary>
    public static bool TrySymbol(string code, out string symbol)
    {
        symbol = string.Empty;
        return code.Length == 3 && Symbols.Value.TryGetValue(code, out symbol!);
    }

    private static Dictionary<string, string> Build()
    {
        Dictionary<string, string> symbols = new(StringComparer.OrdinalIgnoreCase);
        foreach (
            CultureInfo culture in CultureInfo
                .GetCultures(CultureTypes.SpecificCultures)
                .OrderBy(c => c.Name, StringComparer.Ordinal)
        )
        {
            RegionInfo region = new(culture.Name);
            symbols.TryAdd(region.ISOCurrencySymbol, region.CurrencySymbol);
        }
        return symbols;
    }
}
