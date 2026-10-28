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

/// <summary>Odd or even: the word in the channel language, and the true or false for <c>{if:...}</c>.</summary>
internal sealed class NumberParityHelpers()
    : NumberHelperGroup(
        NumberHelperRow.WithCulture("num.parity", "odd", Parity),
        NumberHelperRow.Of(
            "num.isEven",
            "true",
            args =>
                args is [string text] && NumberArguments.TryWhole(text, out long whole)
                    ? NumberText.Flag(whole % 2 == 0)
                    : null
        )
    )
{
    private static string? Parity(IReadOnlyList<string> args, CultureInfo culture)
    {
        if (args is not [string text] || !NumberArguments.TryWhole(text, out long whole))
            return null;

        if (whole % 2 == 0)
            return "even";
        return NumberCulture.IsDutch(culture) ? "oneven" : "odd";
    }
}
