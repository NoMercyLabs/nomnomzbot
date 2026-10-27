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

/// <summary>A whole number looked at digit by digit, and written in base 16 or base 2.</summary>
internal sealed class NumberDigitHelpers()
    : NumberHelperGroup(
        Whole("num.digits", "5", n => Digits(n).Length.ToString(CultureInfo.InvariantCulture)),
        Whole(
            "num.digitSum",
            "6",
            n => Digits(n).Sum(c => c - '0').ToString(CultureInfo.InvariantCulture)
        ),
        Whole("num.hex", "FF", n => InBase(n, 16)),
        Whole("num.binary", "101", n => InBase(n, 2))
    )
{
    private static NumberHelperRow Whole(string key, string sample, Func<long, string> render) =>
        NumberHelperRow.Of(
            key,
            sample,
            args =>
                args is [string text] && NumberArguments.TryWhole(text, out long whole)
                    ? render(whole)
                    : null
        );

    /// <summary>The digits of the number without a sign.</summary>
    private static string Digits(long value) =>
        value.ToString(CultureInfo.InvariantCulture).TrimStart('-');

    /// <summary>The number in base 2 or 16 (capital letters), with a minus sign for a negative number.</summary>
    private static string InBase(long value, int toBase)
    {
        string digits = Convert.ToString(Math.Abs(value), toBase).ToUpperInvariant();
        return value < 0 ? "-" + digits : digits;
    }
}
