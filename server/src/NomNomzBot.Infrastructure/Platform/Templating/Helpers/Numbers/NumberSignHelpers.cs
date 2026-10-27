// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>Small one-number steps: the sign, the absolute value, one more and one less.</summary>
internal sealed class NumberSignHelpers()
    : NumberHelperGroup(
        NumberHelperRow.OfNumber("num.sign", "+5", Sign),
        NumberHelperRow.OfNumber("num.abs", "5", n => NumberText.Plain(Math.Abs(n))),
        NumberHelperRow.OfNumber("num.inc", "5", n => NumberText.Plain(n + 1)),
        NumberHelperRow.OfNumber("num.dec", "3", n => NumberText.Plain(n - 1))
    )
{
    /// <summary>A plus sign before a positive number; a negative number keeps its minus; zero has none.</summary>
    private static string Sign(decimal value) =>
        value > 0 ? "+" + NumberText.Plain(value) : NumberText.Plain(value);
}
