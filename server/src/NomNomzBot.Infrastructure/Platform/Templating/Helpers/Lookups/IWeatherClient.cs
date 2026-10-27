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

/// <summary>One forecast day: the WMO weather code and the low and high in degrees Celsius.</summary>
internal sealed record WeatherDay(int Code, double Min, double Max);

/// <summary>The weather now at a place (Celsius, km/h, degrees) and the forecast for tomorrow.</summary>
internal sealed record WeatherReport(
    GeoPlace Place,
    double Temperature,
    double Feels,
    int Humidity,
    int Code,
    double WindKmh,
    double WindDegrees,
    WeatherDay Tomorrow
);

/// <summary>
/// The weather source behind the <c>lookup.weather</c> forms. The source can change (catalog decision D3)
/// without a template changing.
/// </summary>
internal interface IWeatherClient
{
    /// <summary>The report for <paramref name="city"/>, or null when the city is unknown or the source fails.</summary>
    Task<WeatherReport?> GetAsync(
        string city,
        string language,
        CancellationToken cancellationToken
    );
}
