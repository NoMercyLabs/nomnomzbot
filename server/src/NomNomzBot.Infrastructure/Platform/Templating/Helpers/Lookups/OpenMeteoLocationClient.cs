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

using System.Text.Json;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// Open-Meteo geocoding (no key; free for non-commercial use only, so the host switches it off in saas mode).
/// A city maps to the same place for a month, so the answer is kept for 30 days.
/// </summary>
internal sealed class OpenMeteoLocationClient(
    LookupHttp http,
    LookupCache cache,
    TimeSpan? timeout = null
) : ILocationClient
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(2);

    public Task<GeoPlace?> FindAsync(
        string city,
        string language,
        CancellationToken cancellationToken
    ) =>
        cache.GetOrFetchAsync(
            "open-meteo-geocoding",
            $"{language}:{city}",
            Lifetime,
            () => FetchAsync(city, language, cancellationToken)
        );

    private async Task<GeoPlace?> FetchAsync(
        string city,
        string language,
        CancellationToken cancellationToken
    )
    {
        Uri uri = new(
            "https://geocoding-api.open-meteo.com/v1/search?name="
                + Uri.EscapeDataString(city)
                + "&count=1&language="
                + Uri.EscapeDataString(language)
        );
        using JsonDocument? body = await http.GetJsonAsync(uri, _timeout, cancellationToken);
        if (
            body is null
            || !body.RootElement.TryGetProperty("results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0
        )
            return null;

        JsonElement first = results[0];
        return
            first.TryGetProperty("name", out JsonElement name)
            && first.TryGetProperty("latitude", out JsonElement latitude)
            && first.TryGetProperty("longitude", out JsonElement longitude)
            && first.TryGetProperty("timezone", out JsonElement timezone)
            && name.GetString() is { Length: > 0 } placeName
            && timezone.GetString() is { Length: > 0 } zone
            ? new GeoPlace(placeName, latitude.GetDouble(), longitude.GetDouble(), zone)
            : null;
    }
}
