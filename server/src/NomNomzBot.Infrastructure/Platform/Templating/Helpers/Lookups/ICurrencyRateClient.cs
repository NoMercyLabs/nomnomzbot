// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>An exchange rate (one unit of the base buys <see cref="Rate"/> of the quote) and the day it is from.</summary>
internal sealed record CurrencyRate(DateOnly Date, decimal Rate);

/// <summary>The exchange-rate source behind the <c>lookup.currency</c> forms (Frankfurter, catalog section 9).</summary>
internal interface ICurrencyRateClient
{
    /// <summary>The rate from <paramref name="from"/> to <paramref name="to"/> (ISO codes), or null on failure.</summary>
    Task<CurrencyRate?> GetRateAsync(string from, string to, CancellationToken cancellationToken);
}
