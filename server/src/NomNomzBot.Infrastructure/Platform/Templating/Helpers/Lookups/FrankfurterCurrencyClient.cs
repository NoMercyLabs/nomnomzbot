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
