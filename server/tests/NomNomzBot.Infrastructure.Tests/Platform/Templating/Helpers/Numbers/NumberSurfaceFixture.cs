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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Numbers;

/// <summary>Valid unit, currency, and Roman numeral arguments for the matrix.</summary>
internal static class NumberSurfaceFixture
{
    public static IEnumerable<string> ShapesFor(string key) =>
        key switch
        {
            "num.currency" or "num.currency.compact" => [":1500|USD"],
            "num.fromRoman" => [":XIV"],
            "num.convert" => [":10|km|mi"],
            _ => [],
        };
}
