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

/// <summary>Unit conversion over the fixed table of <see cref="NumberUnits"/>: length, mass, volume and temperature.</summary>
internal sealed class NumberConvertHelpers()
    : NumberHelperGroup(NumberHelperRow.Of("num.convert", "6.21", Convert))
{
    private const int Places = 2;

    /// <summary><c>num.convert:n|from|to</c>, rounded to 2 places; the target <c>ft</c> prints feet and inches.</summary>
    private static string? Convert(IReadOnlyList<string> args)
    {
        if (args.Count != 3 || !NumberArguments.TryNumber(args[0], out decimal value))
            return null;

        string from = ParsedArgument.Word(args[1]);
        string to = ParsedArgument.Word(args[2]);
        if (to.Equals("ft", StringComparison.OrdinalIgnoreCase))
            return NumberUnits.TryFeetAndInches(value, from, out string feet) ? feet : null;

        return NumberUnits.TryConvert(value, from, to, out decimal result)
            ? NumberText.Plain(NumberText.Round(result, Places))
            : null;
    }
}
