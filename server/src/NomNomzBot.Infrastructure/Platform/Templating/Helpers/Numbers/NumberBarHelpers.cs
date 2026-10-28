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
