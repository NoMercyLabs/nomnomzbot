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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The upcoming segments as a list, as the days of this week, and as today's slots.</summary>
internal sealed class ScheduleListHelpers()
    : BroadcastHelperGroup<ScheduleData>(
        ScheduleRows.Over(
            "schedule.list",
            "Fri 20:00 Bot night · Sun 14:00 Chill · Tue 20:00 Bot night",
            (view, call) =>
                BroadcastText.TryCount(call.Args[0], MaxListed, out int count)
                    ? Join(
                        view.Upcoming.Take(count),
                        s =>
                            $"{BroadcastText.ShortDayAndTime(s.StartTime, call.Timezone, call.Culture)} {s.Title}"
                    )
                    : null,
            args: 1
        ),
        ScheduleRows.Over(
            "schedule.week",
            "Fri, Sun",
            (view, call) =>
            {
                DateOnly first = BroadcastText.WeekStart(LocalDay(call.Now, call));
                DateOnly last = first.AddDays(6);
                IEnumerable<string> days = view
                    .Upcoming.Where(s => LocalDay(s.StartTime, call) <= last)
                    .Select(s =>
                        call.Culture.DateTimeFormat.GetAbbreviatedDayName(
                            BroadcastText.Local(s.StartTime, call.Timezone).DayOfWeek
                        )
                    )
                    .Distinct();
                return string.Join(", ", days);
            }
        ),
        ScheduleRows.Over(
            "schedule.today",
            "20:00 Bot night",
            (view, call) =>
            {
                DateOnly today = LocalDay(call.Now, call);
                return Join(
                    view.Upcoming.Where(s => LocalDay(s.StartTime, call) == today),
                    s => $"{BroadcastText.Clock(s.StartTime, call.Timezone)} {s.Title}"
                );
            }
        )
    )
{
    private const int MaxListed = 10;
    private const string Separator = " · ";

    private static string Join(
        IEnumerable<TwitchScheduleSegment> segments,
        Func<TwitchScheduleSegment, string> line
    ) => string.Join(Separator, segments.Select(line));

    private static DateOnly LocalDay(DateTimeOffset moment, BroadcastCall<ScheduleData> call) =>
        DateOnly.FromDateTime(BroadcastText.Local(moment, call.Timezone).DateTime);
}
