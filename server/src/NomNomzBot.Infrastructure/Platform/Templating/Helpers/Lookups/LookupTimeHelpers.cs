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
using NodaTime;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// <c>lookup.time:&lt;city&gt;[.date|.offset]</c>: the wall clock in a city. The city's zone comes from
/// Open-Meteo geocoding, so the form prints its fallback string in saas mode (catalog decision D3).
/// </summary>
internal sealed class LookupTimeHelpers()
    : LookupHelperGroup(
        new LookupRow(
            "lookup.time",
            "23:30",
            "weather",
            TemplateHelperFreshness.Cached,
            1,
            1,
            RenderAsync
        )
    )
{
    private static readonly string[] Fields = ["date", "offset"];

    private static async Task<string?> RenderAsync(LookupCall call)
    {
        (string city, string? field) = LookupArguments.Split(call.Args[0], Fields);
        if (city.Length == 0)
            return null;

        if (!call.Sources.OpenMeteoAllowed)
            return call.Fallback(LookupFallbacks.Time);

        GeoPlace? place = await call.Sources.Location.FindAsync(city, call.Language, call.Ct);
        DateTimeZone? zone = place is null
            ? null
            : DateTimeZoneProviders.Tzdb.GetZoneOrNull(place.Timezone);
        if (zone is null)
            return call.Fallback(LookupFallbacks.Time);

        Instant now = Instant.FromDateTimeOffset(call.Scope.Now);
        ZonedDateTime there = now.InZone(zone);
        return field?.ToLowerInvariant() switch
        {
            "date" => there.ToDateTimeUnspecified().ToString("dddd d MMMM", call.Culture),
            "offset" => Offset(there, now, call),
            _ => there.ToDateTimeUnspecified().ToString("HH:mm", CultureInfo.InvariantCulture),
        };
    }

    private static string Offset(ZonedDateTime there, Instant now, LookupCall call)
    {
        DateTimeZone here =
            call.Scope.Timezone is { } id
            && DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) is { } channelZone
                ? channelZone
                : DateTimeZone.Utc;
        int minutes = (there.Offset.Seconds - now.InZone(here).Offset.Seconds) / 60;
        bool dutch = NumberCulture.IsDutch(call.Culture);
        if (minutes == 0)
            return dutch ? "zelfde tijd als hier" : "same time as here";

        string sign = minutes > 0 ? "+" : "-";
        int whole = Math.Abs(minutes) / 60;
        int rest = Math.Abs(minutes) % 60;
        string amount = rest == 0 ? $"{whole}h" : $"{whole}h{rest:00}m";
        return $"{sign}{amount} {(dutch ? "vanaf hier" : "from here")}";
    }
}
