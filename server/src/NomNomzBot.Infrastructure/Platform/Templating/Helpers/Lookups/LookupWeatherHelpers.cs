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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// <c>lookup.weather:&lt;city&gt;[.field]</c>: the weather now and tomorrow. Open-Meteo is free for
/// non-commercial use only, so the form prints its fallback string in saas mode (catalog decision D3).
/// </summary>
internal sealed class LookupWeatherHelpers()
    : LookupHelperGroup(
        new LookupRow(
            "lookup.weather",
            "Amsterdam: 12 °C, light rain",
            "weather",
            TemplateHelperFreshness.Cached,
            1,
            1,
            RenderAsync
        )
    )
{
    private static readonly Dictionary<string, Func<WeatherReport, CultureInfo, string>> Fields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["temp"] = (w, _) => WeatherText.Celsius(w.Temperature),
            ["temp.f"] = (w, _) => WeatherText.Fahrenheit(w.Temperature),
            ["feels"] = (w, _) => WeatherText.Celsius(w.Feels),
            ["condition"] = (w, c) => WeatherText.Condition(w.Code, c),
            ["emoji"] = (w, _) => WeatherText.Emoji(w.Code),
            ["wind"] = (w, c) =>
                $"{Math.Round(w.WindKmh, MidpointRounding.AwayFromZero):0} km/h {WeatherText.Direction(w.WindDegrees, c)}",
            ["humidity"] = (w, _) => $"{w.Humidity}%",
            ["tomorrow"] = (w, c) =>
                $"{WeatherText.CelsiusRange(w.Tomorrow.Min, w.Tomorrow.Max)}, {WeatherText.Condition(w.Tomorrow.Code, c)}",
        };

    private static async Task<string?> RenderAsync(LookupCall call)
    {
        (string city, string? field) = LookupArguments.Split(call.Args[0], [.. Fields.Keys]);
        if (city.Length == 0)
            return null;

        if (!call.Sources.OpenMeteoAllowed)
            return call.Fallback(LookupFallbacks.Weather);

        if (await call.Sources.Weather.GetAsync(city, call.Language, call.Ct) is not { } report)
            return call.Fallback(LookupFallbacks.Weather);

        return field is null
            ? $"{report.Place.Name}: {Fields["temp"](report, call.Culture)}, {Fields["condition"](report, call.Culture)}"
            : Fields[field](report, call.Culture);
    }
}
