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

/// <summary>Argument readers shared by the number groups (catalog section 3). All parsing is invariant.</summary>
internal static class NumberArguments
{
    /// <summary>The most decimal places any row keeps or accepts.</summary>
    public const int MaxPlaces = 10;

    /// <summary>A decimal number with an optional sign and point: <c>-4</c>, <c>2.5</c>. No thousands marks, no exponent.</summary>
    public static bool TryNumber(string text, out decimal value) =>
        decimal.TryParse(
            text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value
        );

    /// <summary>Every argument as a number; false when one is not.</summary>
    public static bool TryNumbers(IReadOnlyList<string> args, out List<decimal> values)
    {
        values = [];
        foreach (string arg in args)
        {
            if (!TryNumber(arg, out decimal value))
                return false;
            values.Add(value);
        }
        return true;
    }

    /// <summary>A whole number that fits a <see cref="long"/>.</summary>
    public static bool TryWhole(decimal value, out long whole)
    {
        whole = 0;
        if (value != decimal.Truncate(value) || value < long.MinValue || value > long.MaxValue)
            return false;
        whole = (long)value;
        return true;
    }

    /// <summary>A whole number text that fits a <see cref="long"/>.</summary>
    public static bool TryWhole(string text, out long whole)
    {
        whole = 0;
        return TryNumber(text, out decimal value) && TryWhole(value, out whole);
    }

    /// <summary>A count of decimal places, 0 to <see cref="MaxPlaces"/>.</summary>
    public static bool TryPlaces(string text, out int places) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out places)
        && places <= MaxPlaces;

    /// <summary>The places argument at <paramref name="index"/>, or <paramref name="fallback"/> when it is absent; false when present and bad.</summary>
    public static bool TryPlacesAt(
        IReadOnlyList<string> args,
        int index,
        int fallback,
        out int places
    )
    {
        places = fallback;
        return args.Count <= index || TryPlaces(args[index], out places);
    }
}
