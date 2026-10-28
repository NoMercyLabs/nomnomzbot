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

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The rolling-window rows: messages per minute, who is active, who writes most, and who wrote last.</summary>
internal sealed class ActivityRingHelpers()
    : BroadcastHelperGroup<ActivityData>(
        ActivityRows.OverRing(
            "activity.mpm",
            "18",
            call =>
                call.Args.Count == 0 ? Number(call.Data.Recent(TimeSpan.FromMinutes(1)).Count, call)
                : BroadcastText.TryCount(call.Args[0], MaxMinutes, out int minutes)
                    ? Number(
                        PerMinute(call.Data.Recent(TimeSpan.FromMinutes(minutes)).Count, minutes),
                        call
                    )
                : null,
            minArgs: 0,
            maxArgs: 1
        ),
        ActivityRows.OverRing(
            "activity.messages.hour",
            "1,080",
            call => Number(call.Data.Recent(ChatActivityRing.Window).Count, call)
        ),
        ActivityRows.OverRing(
            "activity.chatters.active",
            "12",
            call =>
                BroadcastText.TryCount(call.Args[0], MaxMinutes, out int minutes)
                    ? Number(
                        call.Data.Recent(TimeSpan.FromMinutes(minutes))
                            .Select(e => e.UserId)
                            .Distinct()
                            .Count(),
                        call
                    )
                    : null,
            minArgs: 1,
            maxArgs: 1
        ),
        ActivityRows.OverRing(
            "activity.top.hour",
            "ViewerTwo",
            call =>
                call.Data.Recent(ChatActivityRing.Window)
                    .GroupBy(e => e.UserId)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()
                    ?.Last()
                    .Name
        ),
        ActivityRows.OverRing(
            "activity.random.recent",
            "ViewerTwo",
            call =>
            {
                if (!BroadcastText.TryCount(call.Args[0], MaxMinutes, out int minutes))
                    return null;

                List<string> authors =
                [
                    .. call
                        .Data.Recent(TimeSpan.FromMinutes(minutes))
                        .GroupBy(e => e.UserId)
                        .Select(g => g.Last().Name),
                ];
                return authors.Count == 0 ? null : authors[call.Data.Random.Next(authors.Count)];
            },
            minArgs: 1,
            maxArgs: 1
        ),
        ActivityRows.OverRing(
            "activity.last.chatter",
            "ViewerTwo",
            call =>
            {
                call.Scope.Variables.TryGetValue("user.id", out string? triggerId);
                ChatActivityEntry? last = call
                    .Data.Recent(ChatActivityRing.Window)
                    .Reverse()
                    .Cast<ChatActivityEntry?>()
                    .FirstOrDefault(e => e!.Value.UserId != triggerId);
                return last?.Name;
            }
        )
    )
{
    private const int MaxMinutes = 60;

    private static long PerMinute(int count, int minutes) =>
        (long)Math.Round((double)count / minutes, MidpointRounding.AwayFromZero);

    private static string Number(long value, BroadcastCall<ActivityData> call) =>
        PeopleText.Count(value, call.Culture);
}
