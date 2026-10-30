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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers;

/// <summary>
/// The one place an argument that a helper parses loses the spaces around it (catalog section 3, Quoting).
/// The resolver hands every argument to its helper as written, so a text argument keeps its spaces; a number,
/// a count, a keyword or a name is read here, where <c>{text.repeat: 3 |ha}</c> still means 3.
/// </summary>
internal static class ParsedArgument
{
    /// <summary>A name, mode, keyword or lookup key: the argument without the spaces around it.</summary>
    public static string Word(string argument) => argument.Trim();

    /// <summary>A non-negative whole number made of digits only.</summary>
    public static bool TryWhole(string argument, out int value) =>
        int.TryParse(Word(argument), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>A non-negative whole number made of digits only, as a <see cref="long"/>.</summary>
    public static bool TryWhole(string argument, out long value) =>
        long.TryParse(Word(argument), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>A decimal number with an optional sign and point: <c>-4</c>, <c>2.5</c>. No thousands marks, no exponent.</summary>
    public static bool TryDecimal(string argument, out decimal value) =>
        decimal.TryParse(
            Word(argument),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value
        );
}
