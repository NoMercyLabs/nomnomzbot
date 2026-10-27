// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>How an <c>ad.*</c> row reads the ad schedule.</summary>
internal static class AdRows
{
    private const string Source = "ads";

    /// <summary>A row over the ad schedule; no value (Twitch is down) prints the token as written.</summary>
    public static BroadcastRow<AdData> Over(
        string key,
        string sample,
        Func<TwitchAdSchedule, BroadcastCall<AdData>, string?> print
    ) =>
        BroadcastRow<AdData>.Of(
            key,
            sample,
            Source,
            TemplateHelperFreshness.Cached,
            async call =>
                await call.Data.ScheduleAsync(call.Ct) is { } schedule
                    ? print(schedule, call)
                    : null
        );

    /// <summary>A row over a moment of the schedule (unix seconds, 0 when there is none).</summary>
    public static BroadcastRow<AdData> AtMoment(
        string key,
        string sample,
        Func<TwitchAdSchedule, int> moment,
        Func<DateTimeOffset, BroadcastCall<AdData>, string> print
    ) =>
        Over(
            key,
            sample,
            (schedule, call) =>
                moment(schedule) is > 0 and int unix
                    ? print(DateTimeOffset.FromUnixTimeSeconds(unix), call)
                    : null
        );
}
