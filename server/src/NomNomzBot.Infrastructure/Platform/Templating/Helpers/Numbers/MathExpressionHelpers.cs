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

/// <summary>The <c>{math:expression}</c> row: the whole text is one expression for <see cref="MathExpression"/>.</summary>
internal sealed class MathExpressionHelpers()
    : NumberHelperGroup(NumberHelperRow.Of("math", "2.5", Evaluate))
{
    private static string? Evaluate(IReadOnlyList<string> args)
    {
        if (args.Count != 1)
            return null;

        decimal? value = MathExpression.Evaluate(args[0]);
        return value is null ? null : NumberText.Plain(value.Value);
    }
}
