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
