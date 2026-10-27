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

/// <summary>The provider of the <c>num</c> namespace (catalog section 7.4): rounding, percent, formats, words, units and bars.</summary>
public sealed class NumTemplateHelpers()
    : NumberHelperProvider(
        "num",
        [
            new NumberBoundsHelpers(),
            new NumberRoundingHelpers(),
            new NumberPercentHelpers(),
            new NumberSignHelpers(),
            new NumberParityHelpers(),
            new NumberDigitHelpers(),
            new NumberFormatHelpers(),
            new NumberCurrencyHelpers(),
            new NumberWordHelpers(),
            new NumberDurationHelpers(),
            new NumberConvertHelpers(),
            new NumberBarHelpers(),
        ]
    );
