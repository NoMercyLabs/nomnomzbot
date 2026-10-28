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

using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>Chat today (the channel's local day), who is in chat now, and when chat is busiest over the last 90 days.</summary>
internal sealed class ActivityDayHelpers()
    : BroadcastHelperGroup<ActivityData>(
        ActivityRows.OverCount(
            "activity.messages.today",
            "2,610",
            "analytics",
            call => call.Data.MessagesTodayAsync(call.Ct),
            (count, call) => PeopleText.Count(count, call.Culture)
        ),
        ActivityRows.OverSource<TopViewerDto>(
            "activity.top.today",
            "ViewerOne",
            "analytics",
            call => call.Data.TopTodayAsync(call.Ct),
            (top, _) => top.DisplayName
        ),
        ActivityRows.OverSource<TopViewerDto>(
            "activity.top.today.count",
            "140",
            "analytics",
            call => call.Data.TopTodayAsync(call.Ct),
            (top, call) => PeopleText.Count(top.MetricValue, call.Culture)
        ),
        ActivityRows.OverCount(
            "activity.chatters.now",
            "51",
            "twitchChatters",
            call => call.Data.ChattersNowAsync(call.Ct),
            (total, call) => PeopleText.Count(total, call.Culture)
        ),
        ActivityRows.OverCount(
            "activity.chatters.lurkers",
            "18",
            "twitchChatters",
            call => call.Data.ChattersNowAsync(call.Ct),
            (total, call) =>
                PeopleText.Count(
                    Math.Max(0, total - (call.Scope.Channel?.SessionChatters.Count ?? 0)),
                    call.Culture
                )
        ),
        ActivityRows.OverSource<ChannelChatHoursDto>(
            "activity.busiestHour",
            "21:00",
            "analytics",
            call => call.Data.ChatHoursAsync(call.Ct),
            (grid, _) => Busiest(grid, c => c.Hour) is { } hour ? $"{hour:00}:00" : null
        ),
        ActivityRows.OverSource<ChannelChatHoursDto>(
            "activity.busiestDay",
            "Friday",
            "analytics",
            call => call.Data.ChatHoursAsync(call.Ct),
            (grid, call) =>
                Busiest(grid, c => c.DayOfWeek) is { } day
                    ? call.Culture.DateTimeFormat.GetDayName((DayOfWeek)day)
                    : null
        )
    )
{
    /// <summary>The bucket (hour or weekday) with the most messages; a tie goes to the earliest bucket.</summary>
    private static int? Busiest(ChannelChatHoursDto grid, Func<ChatHourCellDto, int> bucket) =>
        grid.Cells.Count == 0
            ? null
            : grid
                .Cells.GroupBy(bucket)
                .OrderByDescending(g => g.Sum(c => c.Messages))
                .ThenBy(g => g.Key)
                .First()
                .Key;
}
