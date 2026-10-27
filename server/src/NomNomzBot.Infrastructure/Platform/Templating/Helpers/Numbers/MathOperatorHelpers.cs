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

/// <summary>The two-number operators: add, subtract, multiply, divide, whole division and modulo.</summary>
internal sealed class MathOperatorHelpers()
    : NumberHelperGroup(
        Operator("math.add", "5", (a, b) => a + b),
        Operator("math.sub", "2", (a, b) => a - b),
        Operator("math.mul", "12", (a, b) => a * b),
        Operator("math.div", "3.5", NumberMath.Divide),
        Operator(
            "math.div.int",
            "3",
            (a, b) => NumberMath.Divide(a, b) is { } quotient ? decimal.Truncate(quotient) : null
        ),
        Operator("math.mod", "1", NumberMath.Modulo)
    )
{
    private static NumberHelperRow Operator(
        string key,
        string sample,
        Func<decimal, decimal, decimal?> operation
    ) =>
        NumberHelperRow.OfNumbers(
            key,
            sample,
            2,
            2,
            v => operation(v[0], v[1]) is { } value ? NumberText.Plain(value) : null
        );
}
