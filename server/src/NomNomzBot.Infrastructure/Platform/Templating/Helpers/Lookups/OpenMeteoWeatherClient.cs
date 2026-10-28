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
/// Open-Meteo forecast (no key; free for non-commercial use only, so the host switches it off in saas mode).
/// A busy bot stays far below the 600 calls per minute limit because the answer for a place is kept 10 minutes.
/// </summary>
internal sealed class OpenMeteoWeatherClient(
    ILocationClient locations,
    LookupHttp http,
    LookupCache cache,
    TimeSpan? timeout = null
) : IWeatherClient
{
    private const string Fields =
        "current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m"
        + "&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=2&timezone=auto";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(2);

    public async Task<WeatherReport?> GetAsync(
        string city,
        string language,
        CancellationToken cancellationToken
    )
    {
        GeoPlace? place = await locations.FindAsync(city, language, cancellationToken);
        return place is null
            ? null
            : await cache.GetOrFetchAsync(
                "open-meteo-forecast",
                FormattableString.Invariant($"{place.Latitude:F2},{place.Longitude:F2}"),
                Lifetime,
                () => FetchAsync(place, cancellationToken)
            );
    }

    private async Task<WeatherReport?> FetchAsync(
        GeoPlace place,
        CancellationToken cancellationToken
    )
    {
        Uri uri = new(
            FormattableString.Invariant(
                $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude:F4}&longitude={place.Longitude:F4}&{Fields}"
            )
        );
        using JsonDocument? body = await http.GetJsonAsync(uri, _timeout, cancellationToken);
        return body is null ? null : Parse(place, body.RootElement);
    }

    private static WeatherReport? Parse(GeoPlace place, JsonElement root)
    {
        if (
            !root.TryGetProperty("current", out JsonElement current)
            || !root.TryGetProperty("daily", out JsonElement daily)
            || DayAt(daily, 1) is not { } tomorrow
        )
            return null;

        return
            Number(current, "temperature_2m") is { } temperature
            && Number(current, "apparent_temperature") is { } feels
            && Number(current, "relative_humidity_2m") is { } humidity
            && Number(current, "weather_code") is { } code
            && Number(current, "wind_speed_10m") is { } wind
            && Number(current, "wind_direction_10m") is { } direction
            ? new WeatherReport(
                place,
                temperature,
                feels,
                (int)Math.Round(humidity),
                (int)code,
                wind,
                direction,
                tomorrow
            )
            : null;
    }

    private static WeatherDay? DayAt(JsonElement daily, int index) =>
        Item(daily, "weather_code", index) is { } code
        && Item(daily, "temperature_2m_min", index) is { } min
        && Item(daily, "temperature_2m_max", index) is { } max
            ? new WeatherDay((int)code, min, max)
            : null;

    private static double? Item(JsonElement block, string name, int index) =>
        block.TryGetProperty(name, out JsonElement values)
        && values.ValueKind == JsonValueKind.Array
        && values.GetArrayLength() > index
        && values[index].ValueKind == JsonValueKind.Number
            ? values[index].GetDouble()
            : null;

    private static double? Number(JsonElement block, string name) =>
        block.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
