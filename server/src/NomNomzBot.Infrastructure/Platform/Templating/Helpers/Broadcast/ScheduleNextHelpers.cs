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

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The next scheduled stream: when it starts, how far away it is, and what it is about.</summary>
internal sealed class ScheduleNextHelpers()
    : BroadcastHelperGroup<ScheduleData>(
        ScheduleRows.OverNext(
            "schedule.next.at",
            "Friday 20:00",
            (s, call) => BroadcastText.DayAndTime(s.StartTime, call.Timezone, call.Culture)
        ),
        ScheduleRows.OverNext(
            "schedule.next.date",
            "Friday 30 October",
            (s, call) => BroadcastText.DayAndDate(s.StartTime, call.Timezone, call.Culture)
        ),
        ScheduleRows.OverNext("schedule.next.in", "4 days", (s, call) => Distance(s, call)),
        ScheduleRows.OverNext(
            "schedule.next.relative",
            "in 4 days",
            (s, call) => Relative(s, call)
        ),
        ScheduleRows.OverNext(
            "schedule.next.full",
            "next stream in 4 days, Friday 20:00",
            (s, call) =>
                $"{(NumberCulture.IsDutch(call.Culture) ? "volgende stream" : "next stream")} "
                + $"{Relative(s, call)}, {BroadcastText.DayAndTime(s.StartTime, call.Timezone, call.Culture)}"
        ),
        ScheduleRows.OverNext("schedule.next.title", "Bot night", (s, _) => s.Title),
        ScheduleRows.OverNext(
            "schedule.next.game",
            "Software and Game Development",
            (s, _) => s.Category?.Name
        ),
        ScheduleRows.OverNext(
            "schedule.next.duration",
            "3h",
            (s, _) => BroadcastText.Brief(s.EndTime - s.StartTime)
        ),
        ScheduleRows.OverNext(
            "schedule.next.isRecurring",
            "true",
            (s, _) => PeopleText.Flag(s.IsRecurring)
        ),
        ScheduleRows.OverNext(
            "schedule.next.viewerTime",
            "Fri 15:00",
            (s, call) =>
                IsKnownZone(call.Args[0])
                    ? BroadcastText.ShortDayAndTime(s.StartTime, call.Args[0], call.Culture)
                    : null,
            args: 1
        )
    )
{
    private static string Distance(
        TwitchScheduleSegment segment,
        BroadcastCall<ScheduleData> call
    ) => BroadcastText.Words(segment.StartTime - call.Now, call.Culture);

    private static string Relative(
        TwitchScheduleSegment segment,
        BroadcastCall<ScheduleData> call
    ) => BroadcastText.Ahead(Distance(segment, call), call.Culture);

    private static bool IsKnownZone(string zone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(zone, out TimeZoneInfo? _);
}
