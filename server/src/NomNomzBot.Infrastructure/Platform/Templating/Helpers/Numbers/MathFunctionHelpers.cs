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

/// <summary>One-number and power functions: square root, absolute value, floor, ceiling, power and logarithm.</summary>
internal sealed class MathFunctionHelpers()
    : NumberHelperGroup(
        NumberHelperRow.OfNumber("math.sqrt", "9", Result(NumberMath.Sqrt)),
        NumberHelperRow.OfNumber("math.abs", "4", n => NumberText.Plain(Math.Abs(n))),
        NumberHelperRow.OfNumber("math.floor", "2", n => NumberText.Plain(Math.Floor(n))),
        NumberHelperRow.OfNumber("math.ceil", "3", n => NumberText.Plain(Math.Ceiling(n))),
        NumberHelperRow.OfNumbers("math.pow", "27", 2, 2, v => Result(NumberMath.Pow)(v[0], v[1])),
        NumberHelperRow.OfNumbers("math.log", "2", 1, 2, Log)
    )
{
    private static Func<decimal, string?> Result(Func<decimal, decimal?> operation) =>
        n => operation(n) is { } value ? NumberText.Plain(value) : null;

    private static Func<decimal, decimal, string?> Result(
        Func<decimal, decimal, decimal?> operation
    ) => (a, b) => operation(a, b) is { } value ? NumberText.Plain(value) : null;

    /// <summary>The logarithm of the first number; the base is the second, or e when it is missing.</summary>
    private static string? Log(IReadOnlyList<decimal> values)
    {
        decimal? logBase = values.Count == 2 ? values[1] : null;
        return NumberMath.Log(values[0], logBase) is { } value ? NumberText.Plain(value) : null;
    }
}
