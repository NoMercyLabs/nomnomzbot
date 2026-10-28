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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// The channel's stream history, from its newest 100 streams: the categories streamed, the stream count and the
/// hours streamed this month (in the channel's time zone). The count is the channel's full total.
/// </summary>
internal sealed class StreamHistoryHelpers()
    : StreamHelperGroup(
        "stream",
        History(
            "stream.games.history",
            "Software and Game Development, Just Chatting, Minecraft",
            (page, call) =>
                StreamText.TryPositive(call.Args[0], GamesMax, out int count)
                && Games(page).Take(count).ToList() is { Count: > 0 } games
                    ? string.Join(", ", games)
                    : null,
            args: 1
        ),
        History(
            "stream.count",
            "214",
            (page, call) => StreamText.Grouped(page.TotalCount, call.Culture)
        ),
        History(
            "stream.count.month",
            "11",
            (page, call) => StreamText.Grouped(ThisMonth(page, call).Count(), call.Culture)
        ),
        History(
            "stream.hours.month",
            "46h",
            (page, call) =>
                StreamText.Hours(
                    TimeSpan.FromSeconds(ThisMonth(page, call).Sum(s => s.DurationSeconds ?? 0))
                ) + "h"
        ),
        History(
            "stream.number",
            "stream #214",
            (page, call) => "stream #" + StreamText.Plain(page.TotalCount)
        )
    )
{
    private const int GamesMax = 25;

    private static StreamHelperRow History(
        string key,
        string sample,
        Func<PagedList<StreamListItemDto>, StreamHelperCall, string?> render,
        int args = 0
    ) =>
        StreamHelperRow.Read(
            key,
            sample,
            StreamSources.History,
            call => call.Data.HistoryAsync(call.Ct),
            render,
            args
        );

    private static IEnumerable<string> Games(PagedList<StreamListItemDto> page) =>
        page
            .Items.Select(s => s.GameName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal);

    private static IEnumerable<StreamListItemDto> ThisMonth(
        PagedList<StreamListItemDto> page,
        StreamHelperCall call
    )
    {
        TimeZoneInfo zone = StreamText.ZoneOf(call.Channel?.Timezone);
        DateTimeOffset now = TimeZoneInfo.ConvertTime(call.Scope.Now, zone);
        return page.Items.Where(s =>
            s.StartedAt is { } started
            && TimeZoneInfo.ConvertTime(started, zone) is { } local
            && local.Year == now.Year
            && local.Month == now.Month
        );
    }
}
