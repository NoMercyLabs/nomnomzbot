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
