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
