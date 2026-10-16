// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// The YouTube Data API's daily quota (10,000 units) belongs to the deployment's one app key, not to a
/// channel. Once Google answers <c>quotaExceeded</c>, every further call fails the same way until the
/// quota resets at midnight Pacific time, so calls stop until then instead of each spending a request.
/// </summary>
public interface IYouTubeQuotaCooldown
{
    /// <summary>True while the daily quota is used up; <paramref name="until"/> is when it resets.</summary>
    bool TryGetCoolingUntil(out DateTimeOffset until);

    /// <summary>Records that the quota is used up, until the next midnight America/Los_Angeles.</summary>
    void CoolUntilQuotaReset();
}

/// <inheritdoc />
public sealed class YouTubeQuotaCooldown(TimeProvider time) : IYouTubeQuotaCooldown
{
    private static readonly TimeZoneInfo Pacific = FindPacificTime();

    private long _coolingUntilUtcTicks;

    public bool TryGetCoolingUntil(out DateTimeOffset until)
    {
        long ticks = Interlocked.Read(ref _coolingUntilUtcTicks);
        until = new(ticks, TimeSpan.Zero);
        return ticks > time.GetUtcNow().UtcTicks;
    }

    public void CoolUntilQuotaReset() =>
        Interlocked.Exchange(
            ref _coolingUntilUtcTicks,
            NextPacificMidnight(time.GetUtcNow()).UtcTicks
        );

    /// <summary>The first midnight in Pacific time after <paramref name="now"/>, as a UTC instant.</summary>
    internal static DateTimeOffset NextPacificMidnight(DateTimeOffset now)
    {
        DateTime pacificNow = TimeZoneInfo.ConvertTime(now, Pacific).DateTime;
        DateTime nextMidnight = pacificNow.Date.AddDays(1);
        return new(nextMidnight, Pacific.GetUtcOffset(nextMidnight));
    }

    private static TimeZoneInfo FindPacificTime()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A host without time zone data: fall back to standard time, so the reset is at most an hour late.
            return TimeZoneInfo.CreateCustomTimeZone(
                "Pacific",
                TimeSpan.FromHours(-8),
                "Pacific",
                "Pacific"
            );
        }
    }
}
