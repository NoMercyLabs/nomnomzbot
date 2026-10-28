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

namespace NomNomzBot.Infrastructure.Platform.Templating.Dates;

/// <summary>
/// What a <c>{date:…}</c> expression renders against: the current instant, the channel time zone (used for
/// <c>now</c>, for local literals and for calendar math) and the channel culture (month and day names).
/// </summary>
public sealed record DateExpressionContext(Instant Now, DateTimeZone Zone, CultureInfo Culture)
{
    // English; the invariant culture (English names too) when the runtime runs without culture data.
    private static readonly CultureInfo Fallback =
        FindCulture("en") ?? CultureInfo.InvariantCulture;

    /// <summary>
    /// Builds the context for one channel. An unset or unknown time zone falls back to UTC and an unset or
    /// unknown language falls back to English, the same way <c>{time}</c> falls back; this never throws.
    /// </summary>
    public static DateExpressionContext For(
        DateTimeOffset utcNow,
        string? timezoneId,
        string? language
    ) => new(Instant.FromDateTimeOffset(utcNow), ResolveZone(timezoneId), ResolveCulture(language));

    /// <summary>Finds an IANA zone, accepting a Windows zone id too. Null when the id is unknown.</summary>
    public static DateTimeZone? FindZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            return null;

        DateTimeZone? zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezoneId.Trim());
        if (zone is not null)
            return zone;

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(timezoneId.Trim(), out string? ianaId)
            ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaId)
            : null;
    }

    /// <summary>Finds a culture by language tag. Null when the runtime does not know it.</summary>
    public static CultureInfo? FindCulture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(language.Trim());
            // Invariant-globalization mode hands back an empty culture instead of real names.
            return culture.Name.Length == 0 ? null : culture;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static DateTimeZone ResolveZone(string? timezoneId) =>
        FindZone(timezoneId) ?? DateTimeZone.Utc;

    private static CultureInfo ResolveCulture(string? language) =>
        FindCulture(language) ?? Fallback;
}
