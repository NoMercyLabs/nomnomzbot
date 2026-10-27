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

        if (args[2].Equals("ft", StringComparison.OrdinalIgnoreCase))
            return NumberUnits.TryFeetAndInches(value, args[1], out string feet) ? feet : null;

        return NumberUnits.TryConvert(value, args[1], args[2], out decimal result)
            ? NumberText.Plain(NumberText.Round(result, Places))
            : null;
    }
}
