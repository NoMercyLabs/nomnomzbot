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

/// <summary>A city found by name: its display name, position and IANA time zone.</summary>
internal sealed record GeoPlace(string Name, double Latitude, double Longitude, string Timezone);

/// <summary>Turns a city name into a <see cref="GeoPlace"/> (Open-Meteo geocoding, catalog section 9).</summary>
internal interface ILocationClient
{
    /// <summary>The best match for <paramref name="city"/>, or null when none is found or the source fails.</summary>
    Task<GeoPlace?> FindAsync(string city, string language, CancellationToken cancellationToken);
}
