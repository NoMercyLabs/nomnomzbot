// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Validates the IANA zone id (<c>Europe/Amsterdam</c>) that Twitch's schedule endpoints take. A Windows
/// display id (<c>W. Europe Standard Time</c>) also resolves on a Windows host, but Twitch wants IANA, so
/// anything with a space is refused.
/// </summary>
public static class ScheduleTimezone
{
    /// <summary>True when <paramref name="zone"/> is a non-blank id the host knows and Twitch accepts.</summary>
    public static bool IsValid(string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone) || zone.Contains(' '))
            return false;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(zone);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    /// The schedule with <paramref name="zone"/> (the streamer's saved zone, null when none) stamped on the
    /// schedule and on every segment, so a client renders and seeds in it without a second call.
    /// </summary>
    public static TwitchSchedule Stamp(TwitchSchedule schedule, string? zone) =>
        schedule with
        {
            Segments = schedule.Segments.Select(s => s with { Timezone = zone }).ToList(),
            Timezone = zone,
        };
}
