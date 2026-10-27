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

/// <summary>A city found by name: its display name, position and IANA time zone.</summary>
internal sealed record GeoPlace(string Name, double Latitude, double Longitude, string Timezone);

/// <summary>Turns a city name into a <see cref="GeoPlace"/> (Open-Meteo geocoding, catalog section 9).</summary>
internal interface ILocationClient
{
    /// <summary>The best match for <paramref name="city"/>, or null when none is found or the source fails.</summary>
    Task<GeoPlace?> FindAsync(string city, string language, CancellationToken cancellationToken);
}
