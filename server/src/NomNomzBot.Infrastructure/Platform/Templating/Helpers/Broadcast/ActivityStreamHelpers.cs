// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The activity of the current stream: its message count, its top chatters and the channel's live counters.</summary>
internal sealed class ActivityStreamHelpers()
    : BroadcastHelperGroup<ActivityData>(
        ActivityRows.OverSource<StreamStatsChatDto>(
            "activity.messages.stream",
            "2,430",
            "streamStats",
            async call => (await call.Data.StatsAsync("chat", call.Ct))?.Chat,
            (chat, call) => PeopleText.Count(chat.Messages, call.Culture)
        ),
        ActivityRows.OverSource<StreamStatsNamedCountDto>(
            "activity.top.stream",
            "ViewerOne (140)",
            "streamStats",
            async call =>
                (await call.Data.StatsAsync("topChatters", call.Ct))?.TopChatters?.FirstOrDefault(),
            (top, call) => StreamText.Named(top, call.Culture)
        ),
        ActivityRows.OverSource<List<StreamStatsNamedCountDto>>(
            "activity.top.list",
            "ViewerOne 140, ViewerTwo 96, ViewerThree 40",
            "streamStats",
            async call => (await call.Data.StatsAsync("topChatters", call.Ct))?.TopChatters,
            (top, call) =>
                BroadcastText.TryCount(call.Args[0], MaxListed, out int count)
                    ? string.Join(
                        ", ",
                        top.Take(count)
                            .Select(t => $"{t.Name} {StreamText.Grouped(t.Count, call.Culture)}")
                    )
                    : null,
            args: 1
        ),
        ActivityRows.OverChannel(
            "activity.chatters.unique",
            "33",
            (channel, call) => PeopleText.Count(channel.SessionChatters.Count, call.Culture)
        ),
        ActivityRows.OverChannel(
            "activity.last.message.ago",
            "4s",
            (channel, call) => StreamText.Short(call.Now - channel.LastActivityAt)
        ),
        ActivityRows.OverChannel(
            "activity.commands.stream",
            "88",
            (channel, call) => PeopleText.Count(channel.CommandsUsed, call.Culture)
        )
    )
{
    private const int MaxListed = 25;
}
