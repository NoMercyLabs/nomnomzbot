// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>
/// Money with an explicit ISO 4217 code. The form without a code needs the channel money currency, which is the
/// open decision D2 of the catalog; until it is decided that form is not built and renders as written.
/// </summary>
internal sealed class NumberCurrencyHelpers()
    : NumberHelperGroup(
        NumberHelperRow.WithCulture("num.currency", "$5.00", Currency),
        NumberHelperRow.Of("num.currency.compact", "$1.5k", CurrencyCompact)
    )
{
    /// <summary><c>num.currency:n|code</c>: the amount with the symbol of the code, laid out in the channel culture.</summary>
    private static string? Currency(IReadOnlyList<string> args, CultureInfo culture)
    {
        if (!TryAmountAndSymbol(args, out decimal amount, out string symbol))
            return null;

        NumberFormatInfo format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = symbol;
        return NumberText.Round(amount, 2).ToString("C2", format);
    }

    /// <summary><c>num.currency.compact:n|code</c>: the symbol before the short form, <c>$1.5k</c>.</summary>
    private static string? CurrencyCompact(IReadOnlyList<string> args)
    {
        if (!TryAmountAndSymbol(args, out decimal amount, out string symbol))
            return null;

        string compact = NumberText.Compact(Math.Abs(amount));
        return amount < 0 ? "-" + symbol + compact : symbol + compact;
    }

    private static bool TryAmountAndSymbol(
        IReadOnlyList<string> args,
        out decimal amount,
        out string symbol
    )
    {
        amount = 0;
        symbol = string.Empty;
        return args.Count == 2
            && NumberArguments.TryNumber(args[0], out amount)
            && NumberCurrencies.TrySymbol(args[1], out symbol);
    }
}
