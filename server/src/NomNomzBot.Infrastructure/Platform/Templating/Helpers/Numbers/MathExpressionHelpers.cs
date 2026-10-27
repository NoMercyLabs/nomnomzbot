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
