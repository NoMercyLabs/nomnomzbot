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

/// <summary>Rows over a list of numbers: sum, average and median.</summary>
internal sealed class MathAggregateHelpers()
    : NumberHelperGroup(
        Aggregate("math.sum", "6", v => v.Sum()),
        Aggregate("math.avg", "3", v => v.Average()),
        Aggregate("math.median", "3", NumberMath.Median)
    )
{
    private static NumberHelperRow Aggregate(
        string key,
        string sample,
        Func<IReadOnlyList<decimal>, decimal> aggregate
    ) =>
        NumberHelperRow.OfNumbers(
            key,
            sample,
            1,
            int.MaxValue,
            values => NumberText.Plain(aggregate(values))
        );
}
