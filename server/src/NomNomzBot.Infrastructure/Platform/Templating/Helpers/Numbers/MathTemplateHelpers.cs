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
