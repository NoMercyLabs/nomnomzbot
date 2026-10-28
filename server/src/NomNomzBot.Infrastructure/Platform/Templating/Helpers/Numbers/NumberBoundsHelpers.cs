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

/// <summary>Where a number sits among others: smallest, largest, clamped into a range, inside a range.</summary>
internal sealed class NumberBoundsHelpers()
    : NumberHelperGroup(
        NumberHelperRow.OfNumbers("num.min", "2", 1, int.MaxValue, v => NumberText.Plain(v.Min())),
        NumberHelperRow.OfNumbers("num.max", "9", 1, int.MaxValue, v => NumberText.Plain(v.Max())),
        NumberHelperRow.OfNumbers("num.clamp", "100", 3, 3, Clamp),
        NumberHelperRow.OfNumbers("num.between", "true", 3, 3, Between)
    )
{
    /// <summary><c>num.clamp:n|min|max</c>; null when min is above max.</summary>
    private static string? Clamp(IReadOnlyList<decimal> v) =>
        v[1] > v[2] ? null : NumberText.Plain(Math.Clamp(v[0], v[1], v[2]));

    /// <summary><c>num.between:n|a|b</c>: true when n is inside the range, ends included, in either order.</summary>
    private static string Between(IReadOnlyList<decimal> v) =>
        NumberText.Flag(v[0] >= Math.Min(v[1], v[2]) && v[0] <= Math.Max(v[1], v[2]));
}
