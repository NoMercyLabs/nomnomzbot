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
using System.Text.Json;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>Frankfurter daily exchange rates (no key, no quota). A rate is kept for 6 hours.</summary>
internal sealed class FrankfurterCurrencyClient(
    LookupHttp http,
    LookupCache cache,
    TimeSpan? timeout = null
) : ICurrencyRateClient
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(2);

    public Task<CurrencyRate?> GetRateAsync(
        string from,
        string to,
        CancellationToken cancellationToken
    ) =>
        cache.GetOrFetchAsync(
            "frankfurter",
            $"{from}>{to}",
            Lifetime,
            () => FetchAsync(from, to, cancellationToken)
        );

    private async Task<CurrencyRate?> FetchAsync(
        string from,
        string to,
        CancellationToken cancellationToken
    )
    {
        Uri uri = new(
            $"https://api.frankfurter.dev/v2/rate/{from.ToLowerInvariant()}/{to.ToLowerInvariant()}"
        );
        using JsonDocument? body = await http.GetJsonAsync(uri, _timeout, cancellationToken);
        if (
            body is null
            || !body.RootElement.TryGetProperty("rate", out JsonElement rate)
            || rate.ValueKind != JsonValueKind.Number
            || !body.RootElement.TryGetProperty("date", out JsonElement date)
            || !DateOnly.TryParseExact(
                date.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly day
            )
        )
            return null;

        return new CurrencyRate(day, rate.GetDecimal());
    }
}
