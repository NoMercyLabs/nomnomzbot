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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>An exchange rate (one unit of the base buys <see cref="Rate"/> of the quote) and the day it is from.</summary>
internal sealed record CurrencyRate(DateOnly Date, decimal Rate);

/// <summary>The exchange-rate source behind the <c>lookup.currency</c> forms (Frankfurter, catalog section 9).</summary>
internal interface ICurrencyRateClient
{
    /// <summary>The rate from <paramref name="from"/> to <paramref name="to"/> (ISO codes), or null on failure.</summary>
    Task<CurrencyRate?> GetRateAsync(string from, string to, CancellationToken cancellationToken);
}
