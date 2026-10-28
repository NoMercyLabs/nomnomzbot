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

/// <summary>The provider of the <c>math</c> namespace (catalog section 7.4): the expression and the arithmetic rows.</summary>
public sealed class MathTemplateHelpers()
    : NumberHelperProvider(
        "math",
        [
            new MathExpressionHelpers(),
            new MathFunctionHelpers(),
            new MathOperatorHelpers(),
            new MathAggregateHelpers(),
        ]
    );
