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

using System.Globalization;
using System.Text.RegularExpressions;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// <c>lookup.currency</c>, <c>lookup.currency.rate</c> and <c>lookup.currency.date</c>: exchange rates from
/// Frankfurter. The date form takes an optional <c>from|to</c> pair and defaults to EUR to USD; the reference
/// rates change once a day, so the date is the same for every pair.
/// </summary>
internal sealed partial class LookupCurrencyHelpers()
    : LookupHelperGroup(
        new LookupRow(
            "lookup.currency",
            "10 EUR = 11.62 USD",
            "currency",
            TemplateHelperFreshness.Cached,
            3,
            3,
            ConvertAsync
        ),
        new LookupRow(
            "lookup.currency.rate",
            "1.162",
            "currency",
            TemplateHelperFreshness.Cached,
            2,
            2,
            RateAsync
        ),
        new LookupRow(
            "lookup.currency.date",
            "2026-10-26",
            "currency",
            TemplateHelperFreshness.Cached,
            0,
            2,
            DateAsync
        )
    )
{
    private const decimal MaxAmount = 1_000_000_000_000m;

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CodePattern();

    private static async Task<string?> ConvertAsync(LookupCall call)
    {
        if (
            !decimal.TryParse(
                call.Args[0],
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount
            )
            || amount is < 0 or > MaxAmount
            || Pair(call.Args[1], call.Args[2]) is not { } pair
        )
            return null;

        if (await call.Sources.Currency.GetRateAsync(pair.From, pair.To, call.Ct) is not { } rate)
            return call.Fallback(LookupFallbacks.Currency);

        string shown = amount.ToString("#,0.##", call.Culture);
        string converted = (amount * rate.Rate).ToString("#,0.00", call.Culture);
        return $"{shown} {pair.From} = {converted} {pair.To}";
    }

    private static async Task<string?> RateAsync(LookupCall call)
    {
        if (Pair(call.Args[0], call.Args[1]) is not { } pair)
            return null;

        return await call.Sources.Currency.GetRateAsync(pair.From, pair.To, call.Ct) is { } rate
            ? rate.Rate.ToString("0.####", call.Culture)
            : call.Fallback(LookupFallbacks.Currency);
    }

    private static async Task<string?> DateAsync(LookupCall call)
    {
        if (call.Args.Count == 1)
            return null;

        (string from, string to) =
            call.Args.Count == 2 ? (call.Args[0], call.Args[1]) : ("EUR", "USD");
        if (Pair(from, to) is not { } pair)
            return null;

        return await call.Sources.Currency.GetRateAsync(pair.From, pair.To, call.Ct) is { } rate
            ? rate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : call.Fallback(LookupFallbacks.Currency);
    }

    private static (string From, string To)? Pair(string from, string to) =>
        CodePattern().IsMatch(from) && CodePattern().IsMatch(to)
            ? (from.ToUpperInvariant(), to.ToUpperInvariant())
            : null;
}
