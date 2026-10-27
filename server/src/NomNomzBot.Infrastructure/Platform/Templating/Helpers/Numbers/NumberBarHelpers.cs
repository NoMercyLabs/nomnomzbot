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

/// <summary>The <c>num.bar</c> row: a value out of a maximum drawn as the plain bar of <c>meter</c>.</summary>
internal sealed class NumberBarHelpers()
    : NumberHelperGroup(NumberHelperRow.OfNumbers("num.bar", "██████░░░░", 2, 2, Bar))
{
    private static string? Bar(IReadOnlyList<decimal> v) =>
        v[1] <= 0
            ? null
            : MeterBars.Draw(
                v[0] / v[1] * 100,
                MeterBars.DefaultWidth,
                MeterBars.Full,
                MeterBars.Empty
            );
}
